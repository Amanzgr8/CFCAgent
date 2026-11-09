using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CFCAgent.Backend;

// Narrows the tool list to the ones a server explicitly declares read-only, for sessions that
// must never change data (the CLI's --read-only flag). It fails closed: a tool that declares
// nothing, declares itself not read-only, or is not an MCP tool at all is hidden.
public static class ToolFilter
{
    // Added to the system prompt of a read-only session, so the model does not promise changes it cannot make.
    // Shared by every host (the CLI and the desktop app) so the wording cannot drift apart.
    public const string ReadOnlySessionNote =
        "\n\n## This session is read-only\n\nNo write tools are available in this session, whatever the instructions above say. " +
        "If the user asks for a change, say you cannot make it here and tell them how to make it in the app.";

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
