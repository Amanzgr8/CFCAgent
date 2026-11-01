using System.Text.Json;
using CFCChatBot.McpServer.Config;
using Microsoft.Extensions.AI;
using ModelContextProtocol;
using Npgsql;

namespace CFCChatBot.McpServer.Tools;

// Wraps one schema-map.yaml tool definition (name + parameterized SQL template) as an
// AIFunction, so it can be handed to McpServerTool.Create(AIFunction, ...) and registered
// like any other tool. This is the generic read-tool engine: adding a tool to a product's
// schema-map.yaml is enough to expose it, no C# code needed, as long as the query is a
// simple parameterized SELECT. Anything with cascading/calculated logic still needs a
// hand-written tool instead (see the MVP architecture doc's write-tool complexity split).
internal sealed class SchemaMapSqlFunction : AIFunction
{
    private readonly ToolDefinition _definition;
    private readonly NpgsqlDataSource _dataSource;
    private readonly JsonElement _jsonSchema;

    public SchemaMapSqlFunction(ToolDefinition definition, NpgsqlDataSource dataSource)
    {
        _definition = definition;
        _dataSource = dataSource;
        _jsonSchema = BuildJsonSchema(definition.Parameters);
    }

    public override string Name => _definition.Name;
    public override string Description => _definition.Description;
    public override JsonElement JsonSchema => _jsonSchema;

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        //creates a connection
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = _definition.Sql; //loads the sql from the schema-map.yaml file into the command text

        // Add parameters to the command based
        foreach (ToolParameter parameter in _definition.Parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, ResolveValue(parameter, arguments));
        }

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);  //execute the command
        List<Dictionary<string, object?>> rows = [];
        while (await reader.ReadAsync(cancellationToken)) //reads the data from the database and adds it to a list of dictionaries, where each dictionary represents a row in the result set
        {
            Dictionary<string, object?> row = new(reader.FieldCount);
            for (int i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return JsonSerializer.Serialize(rows);
    }

    private static object ResolveValue(ToolParameter parameter, AIFunctionArguments arguments)
    {
        if (!arguments.TryGetValue(parameter.Name, out object? raw) || raw is null || (raw is JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined }))
        {
            if (parameter.Required)
            {
                throw new McpException($"Missing required parameter '{parameter.Name}'.");
            }

            return ConvertDefault(parameter);
        }

        return raw is JsonElement element ? ConvertJsonElement(element, parameter) : raw;
    }

    private static object ConvertDefault(ToolParameter parameter)
    {
        if (parameter.Default is not null)
        {
            return parameter.Type switch
            {
                ToolParameterType.Integer => Convert.ToInt32(parameter.Default),
                ToolParameterType.Number => Convert.ToDouble(parameter.Default),
                ToolParameterType.Boolean => Convert.ToBoolean(parameter.Default),
                _ => parameter.Default.ToString() ?? "",
            };
        }

        // No default supplied for an optional parameter with no value: pass DBNull so the
        // SQL template's own COALESCE/IS NULL handling (or an "OR @param IS NULL" clause)
        // decides what "not provided" means for that query.
        return DBNull.Value;
    }

    private static object ConvertJsonElement(JsonElement element, ToolParameter parameter) =>
        parameter.Type switch
        {
            ToolParameterType.Integer => element.GetInt32(),
            ToolParameterType.Number => element.GetDouble(),
            ToolParameterType.Boolean => element.GetBoolean(),
            _ => element.GetString() ?? "",
        };

    private static JsonElement BuildJsonSchema(List<ToolParameter> parameters)
    {
        Dictionary<string, object> properties = [];
        List<string> required = [];

        foreach (ToolParameter parameter in parameters)
        {
            properties[parameter.Name] = new Dictionary<string, object?>
            {
                ["type"] = parameter.Type switch
                {
                    ToolParameterType.Integer => "integer",
                    ToolParameterType.Number => "number",
                    ToolParameterType.Boolean => "boolean",
                    _ => "string",
                },
                ["description"] = parameter.Description,
            };

            if (parameter.Required)
            {
                required.Add(parameter.Name);
            }
        }

        Dictionary<string, object> schema = new()
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
        };

        return JsonSerializer.SerializeToElement(schema);
    }
}
