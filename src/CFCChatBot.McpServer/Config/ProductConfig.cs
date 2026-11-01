namespace CFCChatBot.McpServer.Config;

// Deserialized from CFC's product's schema-map.yaml (e.g. products/cfc-fertilizer/schema-map.yaml).
// YamlDotNet is configured with the underscored naming convention, so snake_case YAML
// keys (db_engine, ground_rules, read_only, ...) map onto these PascalCase properties.
public sealed class ProductConfig
{
    public string Product { get; set; } = "";
    public string DbEngine { get; set; } = "";
    public List<string> GroundRules { get; set; } = [];
    public List<ToolDefinition> Tools { get; set; } = [];
}

public sealed class ToolDefinition
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool ReadOnly { get; set; } = true;
    public string Sql { get; set; } = "";
    public List<ToolParameter> Parameters { get; set; } = [];
}

public enum ToolParameterType
{
    String,
    Integer,
    Number,
    Boolean,
}

public sealed class ToolParameter
{
    public string Name { get; set; } = "";
    public ToolParameterType Type { get; set; } = ToolParameterType.String;
    public bool Required { get; set; }
    public string? Description { get; set; }
    public object? Default { get; set; }
}
