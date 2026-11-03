using System.Text.Json;

namespace CFCChatBot.Backend;

public static class ElicitationApproval
{
    // Anything short of an accepted form whose every answer is a JSON boolean true is "not approved".
    // A decline, a cancel, a missing/empty answer, an unticked box, or the string "true" must never count as yes.
    public static bool IsApproved(string? action, IEnumerable<KeyValuePair<string, JsonElement>>? content)
    {
        if (!string.Equals(action, "accept", StringComparison.Ordinal))
        {
            return false;
        }

        if (content is null)
        {
            return false;
        }

        bool any = false;
        foreach (KeyValuePair<string, JsonElement> entry in content)
        {
            any = true;
            if (entry.Value.ValueKind != JsonValueKind.True)
            {
                return false;
            }
        }

        return any;
    }
}
