using System.Text;
using System.Text.Json;
using CFCAgent.McpServer.Config;
using Microsoft.Extensions.AI;
using ModelContextProtocol;
using Npgsql;
using ProtocolMcpServer = ModelContextProtocol.Server.McpServer;

namespace CFCAgent.McpServer.Tools;

// Generic engine for "simple" writes per the MVP architecture doc's complexity split:
// a bounded set of single-row field updates on one table, identified by one or more
// business keys - no C# needed per writable table, same spirit as SchemaMapReadFunction
// for reads. Always previews first: a call without confirm=true never touches the
// database, it only reads the current values and reports what WOULD change. Only
// confirm=true asks the user to approve the exact change (WriteApproval) and, only on a yes,
// runs the UPDATE inside a transaction and logs an audit line to stderr.
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
        List<(WriteFieldDefinition Field, object NewValue)> changes = [];
        foreach (WriteFieldDefinition field in _definition.Fields)
        {
            if (TryGetTypedValue(arguments, field.Parameter, field.Type, out object? newValue))
            {
                changes.Add((field, newValue!));
            }
        }

        if (changes.Count == 0)
        {
            string fieldList = string.Join(", ", _definition.Fields.Select(f => f.Parameter));
            throw new McpException($"No fields to update were supplied. Provide at least one of: {fieldList}.");
        }

        bool confirm = TryGetTypedValue(arguments, "confirm", ToolParameterType.Boolean, out object? confirmRaw)
            && confirmRaw is true;

        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Resolve each key component to the value the target table's own column actually
        // holds - either the business value the model supplied directly (most tables), or
        // an internal ID looked up from it via a separate SELECT (junction tables like
        // productnutrient/productingredient, keyed internally - see WriteKeyResolve).
        List<(WriteKeyDefinition Key, object BoundValue, string DisplayValue)> resolvedKeys = [];
        foreach (WriteKeyDefinition key in _definition.Keys)
        {
            if (!TryGetTypedValue(arguments, key.Parameter, key.Type, out object? rawKeyValue) || rawKeyValue is null)
            {
                throw new McpException($"Missing required parameter '{key.Parameter}'.");
            }

            object boundValue = rawKeyValue;
            if (key.Resolve is { } resolve)
            {
                await using NpgsqlCommand resolveCommand = connection.CreateCommand();
                resolveCommand.Transaction = transaction;
                resolveCommand.CommandText = $"SELECT {resolve.IdColumn} FROM {resolve.Table} WHERE {resolve.BusinessColumn} = @val";
                resolveCommand.Parameters.AddWithValue("val", rawKeyValue);
                object? resolved = await resolveCommand.ExecuteScalarAsync(cancellationToken);
                if (resolved is null or DBNull)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw new McpException($"No {resolve.Table} found where {resolve.BusinessColumn} = '{rawKeyValue}' (parameter '{key.Parameter}').");
                }

                boundValue = resolved;
            }

            resolvedKeys.Add((key, boundValue, rawKeyValue.ToString() ?? ""));
        }

        string whereClause = string.Join(" AND ", resolvedKeys.Select((rk, i) => $"{rk.Key.Column} = @keyval{i}"));

        // Read current values of every field we might change, plus the display column,
        // so the preview (and the audit log) can show real old -> new values.
        List<string> selectColumns = [.. changes.Select(c => c.Field.Column)];
        if (_definition.DisplayColumn is { Length: > 0 } displayColumn && !selectColumns.Contains(displayColumn))
        {
            selectColumns.Add(displayColumn);
        }

        await using NpgsqlCommand selectCommand = connection.CreateCommand();
        selectCommand.Transaction = transaction;
        selectCommand.CommandText = $"SELECT {string.Join(", ", selectColumns)} FROM {_definition.Table} WHERE {whereClause}";
        for (int i = 0; i < resolvedKeys.Count; i++)
        {
            selectCommand.Parameters.AddWithValue($"keyval{i}", resolvedKeys[i].BoundValue);
        }

        await using NpgsqlDataReader reader = await selectCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            await reader.CloseAsync();
            await transaction.RollbackAsync(cancellationToken);
            string keyDescription = string.Join(", ", resolvedKeys.Select(rk => $"{rk.Key.Parameter}='{rk.DisplayValue}'"));
            throw new McpException($"No row found in {_definition.Table} where {keyDescription}.");
        }

        Dictionary<string, object?> currentValues = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < reader.FieldCount; i++)
        {
            currentValues[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
        }

        await reader.CloseAsync();

        string keySubject = string.Join(", ", resolvedKeys.Select(rk => rk.DisplayValue));
        string? displayName = _definition.DisplayColumn is { Length: > 0 } dc && currentValues.TryGetValue(dc, out object? dv)
            ? dv?.ToString()
            : null;
        string subject = displayName is not null ? $"{keySubject} ({displayName})" : keySubject;

        StringBuilder summary = new();
        foreach ((WriteFieldDefinition field, object newValue) in changes)
        {
            object? oldValue = currentValues.TryGetValue(field.Column, out object? v) ? v : null;
            string unitNote = field.Unit is { Length: > 0 } unit ? $" ({unit})" : "";
            summary.AppendLine($"  {field.Column}{unitNote}: {FormatValue(oldValue, field.Type)} -> {FormatValue(newValue, field.Type)}");
        }

        if (!confirm)
        {
            await transaction.RollbackAsync(cancellationToken);
            return $"Preview (nothing applied yet) - {_definition.Table} {subject}:\n{summary}" +
                   $"Call {_definition.Name} again with the same arguments plus confirm=true to apply this exact change.";
        }

        // confirm=true is only a request. The server asks the client to put this exact preview
        // in front of the user and applies the change only on an explicit yes; anything else
        // (no prompt support, decline, timeout) throws here and nothing is written.
        ProtocolMcpServer? server = arguments.Services?.GetService(typeof(ProtocolMcpServer)) as ProtocolMcpServer;
        string? notApproved = await WriteApproval.RequestApprovalAsync(
            server,
            $"{_definition.Name}: apply this change to {_definition.Table} {subject}?\n{summary}",
            cancellationToken);
        if (notApproved is not null)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            Console.Error.WriteLine($"[Audit] {DateTimeOffset.UtcNow:O} {_definition.Name} {_definition.Table}[{keySubject}]: not applied - {notApproved}");
            return notApproved;
        }

        await using NpgsqlCommand updateCommand = connection.CreateCommand();
        updateCommand.Transaction = transaction;
        string setClause = string.Join(", ", changes.Select((c, i) => $"{c.Field.Column} = @set{i}"));
        updateCommand.CommandText = $"UPDATE {_definition.Table} SET {setClause} WHERE {whereClause}";
        for (int i = 0; i < resolvedKeys.Count; i++)
        {
            updateCommand.Parameters.AddWithValue($"keyval{i}", resolvedKeys[i].BoundValue);
        }

        for (int i = 0; i < changes.Count; i++)
        {
            updateCommand.Parameters.AddWithValue($"set{i}", ToDbValue(changes[i].Field, changes[i].NewValue));
        }

        await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        Console.Error.WriteLine(
            $"[Audit] {DateTimeOffset.UtcNow:O} {_definition.Name} {_definition.Table}[{keySubject}] (approved by user via elicitation): " +
            string.Join("; ", changes.Select(c => $"{c.Field.Column} {FormatValue(currentValues.GetValueOrDefault(c.Field.Column), c.Field.Type)} -> {FormatValue(c.NewValue, c.Field.Type)}")));

        return $"Applied - {_definition.Table} {subject}:\n{summary}Change committed.";
    }

    private static bool TryGetTypedValue(AIFunctionArguments arguments, string name, ToolParameterType type, out object? value)
    {
        if (!arguments.TryGetValue(name, out object? raw) || raw is null
            || raw is JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined })
        {
            value = null;
            return false;
        }

        if (raw is JsonElement element)
        {
            value = type switch
            {
                ToolParameterType.Integer => element.GetInt32(),
                ToolParameterType.Number => element.GetDouble(),
                ToolParameterType.Boolean => element.ValueKind is JsonValueKind.True or JsonValueKind.False && element.GetBoolean(),
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
        Dictionary<string, object> properties = [];
        List<string> required = [];

        foreach (WriteKeyDefinition key in definition.Keys)
        {
            properties[key.Parameter] = new Dictionary<string, object?>
            {
                ["type"] = JsonTypeName(key.Type),
                ["description"] = key.Description ?? $"Exact {key.Column}, as shown in the app.",
            };
            required.Add(key.Parameter);
        }

        foreach (WriteFieldDefinition field in definition.Fields)
        {
            properties[field.Parameter] = new Dictionary<string, object?>
            {
                ["type"] = JsonTypeName(field.Type),
                ["description"] = field.Description,
            };
        }

        properties["confirm"] = new Dictionary<string, object?>
        {
            ["type"] = "boolean",
            ["description"] = "Set to true to request the change. Omit or set to false to preview it first (recommended default) - nothing is written to the database until confirm=true AND the user approves the confirmation prompt the server then shows.",
        };

        Dictionary<string, object> schema = new()
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
        };

        return JsonSerializer.SerializeToElement(schema);
    }

    private static string JsonTypeName(ToolParameterType type) => type switch
    {
        ToolParameterType.Integer => "integer",
        ToolParameterType.Number => "number",
        ToolParameterType.Boolean => "boolean",
        _ => "string",
    };
}
