using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace CFCAgent.Backend;

// Builds the handler that answers the server's confirmation requests (elicitation). It routes
// the request to a confirmation callback and answers accept (with true) only when the user
// said yes; otherwise it declines, or cancels if the callback was cancelled.
public static class ElicitationHandlers
{
    public static Func<ElicitRequestParams?, CancellationToken, ValueTask<ElicitResult>> Create(Func<string, CancellationToken, Task<bool>> confirmAsync)
    {
        return async (ElicitRequestParams? request, CancellationToken cancellationToken) =>
        {
            bool approved;
            try
            {
                approved = await confirmAsync(request?.Message ?? string.Empty, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return new ElicitResult { Action = "cancel" };
            }

            if (!approved)
            {
                return new ElicitResult { Action = "decline" };
            }

            Dictionary<string, JsonElement> content = new Dictionary<string, JsonElement>();
            IEnumerable<string> keys = request?.RequestedSchema?.Properties?.Keys ?? Enumerable.Empty<string>();
            foreach (string key in keys)
            {
                content[key] = JsonSerializer.SerializeToElement(true);
            }

            if (content.Count == 0)
            {
                content["confirm"] = JsonSerializer.SerializeToElement(true);
            }

            return new ElicitResult { Action = "accept", Content = content };
        };
    }
}
