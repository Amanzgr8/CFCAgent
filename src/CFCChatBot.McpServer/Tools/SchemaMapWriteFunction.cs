using System.Text;
using System.Text.Json;
using CFCChatBot.McpServer.Config;
using Microsoft.Extensions.AI;
using ModelContextProtocol;
using Npgsql;

namespace CFCChatBot.McpServer.Tools;

// Generic engine for "simple" writes per the MVP architecture doc's complexity split:
// a bounded set of single-row field updates on one table, identified by one business
// key - no C# needed per writable table, same spirit as SchemaMapSqlFunction for reads.
// Always previews first: a call without confirm=true never touches the database, it
// only reads the current values and reports what WOULD change. Only confirm=true runs
// the UPDATE, inside a transaction, and logs an audit line to stderr.
internal sealed class SchemaMapWriteFunction : AIFunction
{
    private readonly WriteToolDefinition _definition;
    private readonly NpgsqlDataSource _dataSource;
    private readonly JsonElement _jsonSchema;

    public SchemaMapWriteFunction(WriteToolDefinition definition, NpgsqlDataSource dataSource)
    {
        _definition = definition;
        _dataSource = dataSource;
        _jsonSchema = BuildJsonSchema(definition);
    }

    public override string Name => _definition.Name;
    public override string Description => _definition.Description;
    public override JsonElement JsonSchema => _jsonSchema;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        if (!TryGetString(arguments, _definition.KeyParameter, out string? keyValue) || string.IsNullOrWhiteSpace(keyValue))
        {
            throw new McpException($"Missing required parameter '{_definition.KeyParameter}'.");
        }

        List<(WriteFieldDefinition Field, object NewValue)> changes = [];
        foreach (WriteFieldDefinition field in _definition.Fields)
        {
            if (TryGetFieldValue(arguments, field, out object? newValue))
            {
                changes.Add((field, newValue!));
            }
        }

        if (changes.Count == 0)
        {
            string fieldList = string.Join(", ", _definition.Fields.Select(f => f.Parameter));
            throw new McpException($"No fields to update were supplied. Provide at least one of: {fieldList}.");
        }

        bool confirm = TryGetBool(arguments, "confirm", out bool confirmValue) && confirmValue;

        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Read current values of every field we might change, plus the display column,
        // so the preview (and the audit log) can show real old -> new values.
        List<string> selectColumns = [.. changes.Select(c => c.Field.Column)];
        if (_definition.DisplayColumn is { Length: > 0 } displayColumn && !selectColumns.Contains(displayColumn))
        {
            selectColumns.Add(displayColumn);
        }

        await using NpgsqlCommand selectCommand = connection.CreateCommand();
        selectCommand.Transaction = transaction;
        selectCommand.CommandText = $"SELECT {string.Join(", ", selectColumns)} FROM {_definition.Table} WHERE {_definition.KeyColumn} = @key";
        selectCommand.Parameters.AddWithValue("key", keyValue);

        await using NpgsqlDataReader reader = await selectCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new McpException($"No row found in {_definition.Table} where {_definition.KeyColumn} = '{keyValue}'.");
        }

        Dictionary<string, object?> currentValues = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < reader.FieldCount; i++)
        {
            currentValues[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
        }

        await reader.CloseAsync();

        string? displayName = _definition.DisplayColumn is { Length: > 0 } dc && currentValues.TryGetValue(dc, out object? dv)
            ? dv?.ToString()
            : null;
        string subject = displayName is not null ? $"{keyValue} ({displayName})" : keyValue;

        StringBuilder summary = new();
        foreach ((WriteFieldDefinition field, object newValue) in changes)
        {
            object? oldValue = currentValues.TryGetValue(field.Column, out object? v) ? v : null;
            summary.AppendLine($"  {field.Column}: {FormatValue(oldValue, field.Type)} -> {FormatValue(newValue, field.Type)}");
        }

        if (!confirm)
        {
            await transaction.RollbackAsync(cancellationToken);
            return $"Preview (nothing applied yet) - {_definition.Table} {subject}:\n{summary}" +
                   $"Call {_definition.Name} again with the same arguments plus confirm=true to apply this exact change.";
        }

        await using NpgsqlCommand updateCommand = connection.CreateCommand();
        updateCommand.Transaction = transaction;
        string setClause = string.Join(", ", changes.Select((c, i) => $"{c.Field.Column} = @set{i}"));
        updateCommand.CommandText = $"UPDATE {_definition.Table} SET {setClause} WHERE {_definition.KeyColumn} = @key";
        updateCommand.Parameters.AddWithValue("key", keyValue);
        for (int i = 0; i < changes.Count; i++)
        {
            updateCommand.Parameters.AddWithValue($"set{i}", ToDbValue(changes[i].Field, changes[i].NewValue));
        }

        await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        Console.Error.WriteLine(
            $"[Audit] {DateTimeOffset.UtcNow:O} {_definition.Name} {_definition.Table}.{_definition.KeyColumn}={keyValue}: " +
            string.Join("; ", changes.Select(c => $"{c.Field.Column} {FormatValue(currentValues.GetValueOrDefault(c.Field.Column), c.Field.Type)} -> {FormatValue(c.NewValue, c.Field.Type)}")));

        return $"Applied - {_definition.Table} {subject}:\n{summary}Change committed.";
    }

    private static bool TryGetString(AIFunctionArguments arguments, string name, out string? value)
    {
        if (arguments.TryGetValue(name, out object? raw) && raw is JsonElement { ValueKind: JsonValueKind.String } element)
        {
            value = element.GetString();
            return true;
        }

        value = raw as string;
        return value is not null;
    }

    private static bool TryGetBool(AIFunctionArguments arguments, string name, out bool value)
    {
        if (arguments.TryGetValue(name, out object? raw))
        {
            if (raw is JsonElement { ValueKind: JsonValueKind.True or JsonValueKind.False } element)
            {
                value = element.GetBoolean();
                return true;
            }

            if (raw is bool b)
            {
                value = b;
                return true;
            }
        }

        value = false;
        return false;
    }

    private static bool TryGetFieldValue(AIFunctionArguments arguments, WriteFieldDefinition field, out object? value)
    {
        if (!arguments.TryGetValue(field.Parameter, out object? raw) || raw is null
            || raw is JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined })
        {
            value = null;
            return false;
        }

        if (raw is JsonElement element)
        {
            value = field.Type switch
            {
                ToolParameterType.Integer => element.GetInt32(),
                ToolParameterType.Number => element.GetDouble(),
                ToolParameterType.Boolean => element.GetBoolean(),
                _ => element.GetString() ?? "",
            };
            return true;
        }

        value = raw;
        return true;
    }

    // ingredient.isactive/isinstock etc. are smallint, not a real boolean column, and
    // VB6-imported data may hold -1 for "yes" (see ground rules: test with <> 0). So a
    // boolean-typed field's *old* value read back from the database needs normalizing
    // the same way, or the preview would show "isactive: -1 -> true" instead of
    // "isactive: true -> true".
    private static string FormatValue(object? value, ToolParameterType type)
    {
        if (value is null)
        {
            return "(none)";
        }

        if (type == ToolParameterType.Boolean)
        {
            bool truthy = value switch
            {
                bool b => b,
                _ => Convert.ToInt64(value) != 0,
            };
            return truthy ? "true" : "false";
        }

        return value.ToString() ?? "(none)";
    }

    // The actual database column backing a boolean-typed field is smallint, not a real
    // boolean type, so a C# bool can't be bound directly - write a clean 1/0 (not -1;
    // that's a VB6-import legacy quirk for "yes", not something new writes should repeat).
    private static object ToDbValue(WriteFieldDefinition field, object value) =>
        field.Type == ToolParameterType.Boolean && value is bool b ? (short)(b ? 1 : 0) : value;

    private static JsonElement BuildJsonSchema(WriteToolDefinition definition)
    {
        Dictionary<string, object> properties = new()
        {
            [definition.KeyParameter] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = definition.KeyDescription ?? $"Exact {definition.KeyColumn}, as shown in the app.",
            },
        };

        foreach (WriteFieldDefinition field in definition.Fields)
        {
            properties[field.Parameter] = new Dictionary<string, object?>
            {
                ["type"] = field.Type switch
                {
                    ToolParameterType.Integer => "integer",
                    ToolParameterType.Number => "number",
                    ToolParameterType.Boolean => "boolean",
                    _ => "string",
                },
                ["description"] = field.Description,
            };
        }

        properties["confirm"] = new Dictionary<string, object?>
        {
            ["type"] = "boolean",
            ["description"] = "Set to true to apply the change. Omit or set to false to preview it first (recommended default) - nothing is written to the database until confirm=true.",
        };

        Dictionary<string, object> schema = new()
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new List<string> { definition.KeyParameter },
        };

        return JsonSerializer.SerializeToElement(schema);
    }
}
