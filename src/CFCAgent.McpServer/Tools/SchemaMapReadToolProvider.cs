using CFCAgent.McpServer.Config;
using ModelContextProtocol.Server;
using Npgsql;

namespace CFCAgent.McpServer.Tools;

public static class SchemaMapReadToolProvider
{
    public static IReadOnlyList<McpServerTool> BuildTools(ProductConfig config, NpgsqlDataSource dataSource)
    {
        List<McpServerTool> tools = new(config.Tools.Count);

        foreach (ToolDefinition definition in config.Tools)
        {
            SchemaMapReadFunction function = new(definition, dataSource);
            tools.Add(McpServerTool.Create(function, new McpServerToolCreateOptions
            {
                ReadOnly = definition.ReadOnly,
            }));
        }

        return tools;
    }
}
