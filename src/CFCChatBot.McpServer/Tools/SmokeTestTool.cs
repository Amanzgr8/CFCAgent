using System.ComponentModel;
using ModelContextProtocol.Server;

namespace CFCChatBot.McpServer.Tools;

// Temporary tool used only to prove the server boots and exposes tools correctly
// over stdio. Remove once a real read tool (e.g. search_ingredients) replaces it.
[McpServerToolType]
public static class SmokeTestTool
{
    [McpServerTool(Name = "ping"), Description("Returns 'pong' plus the given message, to confirm the server is reachable.")]
    public static string Ping(string message = "") => $"pong{(string.IsNullOrEmpty(message) ? "" : $": {message}")}";
}
