using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ProtocolMcpServer = ModelContextProtocol.Server.McpServer;

namespace CFCAgent.McpServer.Tools;

// Server-side enforcement of "the user said yes" for every write. A call with confirm=true is
// only a request: before anything is committed, the server asks the connected client to put
// the exact preview in front of the user (MCP elicitation) and applies the change only when
// the answer is an accepted form whose confirm box is an explicit true. It fails closed: a
// client that cannot show the prompt, a decline or cancel, no answer in time, or any error
// while asking all mean the change is not applied. The rule mirrors the backend's
// ElicitationApproval; the two projects do not reference each other, so each keeps its own copy.
public static class WriteApproval
{
    public const string ConfirmField = "confirm";

    public static readonly TimeSpan AnswerTimeout = TimeSpan.FromMinutes(5);

    public static bool IsApproved(string? action, IDictionary<string, JsonElement>? content)
    {
        if (!string.Equals(action, "accept", StringComparison.Ordinal))
        {
            return false;
        }

        if (content is null || content.Count == 0 || !content.ContainsKey(ConfirmField))
        {
            return false;
        }

        foreach (KeyValuePair<string, JsonElement> answer in content)
        {
            if (answer.Value.ValueKind != JsonValueKind.True)
            {
                return false;
            }
        }

        return true;
    }

    public static async Task RequireAsync(ProtocolMcpServer? server, string message, CancellationToken cancellationToken)
    {
        if (server is null)
        {
            throw new McpException(
                "This change was not applied: the server could not reach the client to ask the user for confirmation. Nothing was changed.");
        }

        if (server.ClientCapabilities?.Elicitation is null)
        {
            throw new McpException(
                "This change was not applied: this client cannot show the confirmation prompt the server requires for every change. " +
                "Nothing was changed. Make the change from a client that supports it (the CFC Agent app, or Claude Code in a terminal), or in the CFC app.");
        }

        using CancellationTokenSource answerWindow = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        answerWindow.CancelAfter(AnswerTimeout);

        ElicitResult result;
        try
        {
            result = await server.ElicitAsync(BuildRequest(message), answerWindow.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new McpException(
                $"This change was not applied: the user did not answer within {AnswerTimeout.TotalMinutes:0} minutes. Nothing was changed.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new McpException(
                "This change was not applied: the confirmation prompt could not be completed. Nothing was changed.", ex);
        }

        if (!IsApproved(result.Action, result.Content))
        {
            throw new McpException("This change was not applied: the user did not approve it. Nothing was changed.");
        }
    }

    private static ElicitRequestParams BuildRequest(string message)
    {
        return new ElicitRequestParams
        {
            Message = message,
            RequestedSchema = new ElicitRequestParams.RequestSchema
            {
                Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                {
                    [ConfirmField] = new ElicitRequestParams.BooleanSchema
                    {
                        Title = "Apply this change",
                        Description = "Tick to apply exactly the change described above.",
                    },
                },
                Required = new List<string> { ConfirmField },
            },
        };
    }
}
