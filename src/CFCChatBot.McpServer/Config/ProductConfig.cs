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
    public List<WriteToolDefinition> WriteTools { get; set; } = [];
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

// A "simple write" per the MVP architecture doc's complexity split: a bounded set of
// single-row field updates on one table, identified by one business key. No cascading
// logic, no multi-table writes, no system-wide changes - those stay hand-written C# or
// read/advisory-only. Always previews before applying (see SchemaMapWriteFunction).
public sealed class WriteToolDefinition
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Table { get; set; } = "";
    public string KeyColumn { get; set; } = "";
    public string KeyParameter { get; set; } = "";
    public string? KeyDescription { get; set; }
    public string? DisplayColumn { get; set; }
    public List<WriteFieldDefinition> Fields { get; set; } = [];
}

public sealed class WriteFieldDefinition
{
    public string Column { get; set; } = "";
    public string Parameter { get; set; } = "";
    public ToolParameterType Type { get; set; } = ToolParameterType.String;
    public string? Description { get; set; }
}
