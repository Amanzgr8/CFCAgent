namespace CFCAgent.McpServer.Config;

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
// single-row field updates on one table, identified by one or more business keys. No
// cascading logic, no multi-table writes, no system-wide changes - those stay
// hand-written C# or read/advisory-only. Always previews before applying (see
// SchemaMapWriteFunction).
public sealed class WriteToolDefinition
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Table { get; set; } = "";
    public List<WriteKeyDefinition> Keys { get; set; } = [];
    public string? DisplayColumn { get; set; }
    public List<WriteFieldDefinition> Fields { get; set; } = [];
}

// One component of a write tool's business key. Most tables are keyed directly by a
// user-facing code (ingredientcode, customercode, productcode, nutrientnumber) already
// present as a column on Table itself - Resolve is omitted for those. Junction tables
// like productnutrient/productingredient are keyed internally by IDs that are never
// exposed to the model (per the "never expose internal IDs" ground rule); Resolve
// tells the engine how to look up that internal ID from the business key the model
// actually supplies, in a separate SELECT before the real read/update.
public sealed class WriteKeyDefinition
{
    public string Column { get; set; } = "";
    public string Parameter { get; set; } = "";
    public ToolParameterType Type { get; set; } = ToolParameterType.String;
    public string? Description { get; set; }
    public WriteKeyResolve? Resolve { get; set; }
}

public sealed class WriteKeyResolve
{
    public string Table { get; set; } = "";
    public string BusinessColumn { get; set; } = "";
    public string IdColumn { get; set; } = "";
}

public sealed class WriteFieldDefinition
{
    public string Column { get; set; } = "";
    public string Parameter { get; set; } = "";
    public ToolParameterType Type { get; set; } = ToolParameterType.String;
    public string? Description { get; set; }
}
