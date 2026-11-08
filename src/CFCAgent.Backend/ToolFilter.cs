using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CFCAgent.Backend;

// Narrows the tool list to the ones a server explicitly declares read-only, for sessions that
// must never change data (the CLI's --read-only flag). It fails closed: a tool that declares
// nothing, declares itself not read-only, or is not an MCP tool at all is hidden.
public static class ToolFilter
{
    public static bool IsDeclaredReadOnly(Tool tool)
    {
        return tool.Annotations?.ReadOnlyHint == true;
    }

    public static bool IsDeclaredReadOnly(AITool tool)
    {
        return tool is McpClientTool mcpTool && IsDeclaredReadOnly(mcpTool.ProtocolTool);
    }

    public static IReadOnlyList<AITool> ReadOnlyTools(IEnumerable<AITool> tools)
    {
        List<AITool> kept = new List<AITool>();
        foreach (AITool tool in tools)
        {
            if (IsDeclaredReadOnly(tool))
            {
                kept.Add(tool);
            }
        }

        return kept;
    }
}
