using System.Text.Json;
using System.Text.RegularExpressions;
using CFCAgent.Backend;
using Microsoft.Extensions.AI;

namespace CFCAgent.Backend.Tests;

// Drives the real MCP server through McpToolHost with scripted answers in place of a person.
// These tests need the server built (the test project builds it) and a database connection
// string in CFCCHATBOT_DB_CONNECTION_STRING, otherwise they are skipped. Every test that could
// write uses a no-op change (a value set to what it already is), so even a failure of the
// enforcement under test would not alter data. The two tests that make real changes (one per
// write path) are skipped unless CFCAGENT_ALLOW_WRITE_TESTS=1, and each restores what it changed.
public class WriteApprovalIntegrationTests
{
    private const string IngredientCode = "D066";

    [FactIfDatabase]
    public async Task Preview_DoesNotAskTheUser()
    {
        int asked = 0;
        await using McpToolHost host = await ConnectAsync((message, ct) => { asked++; return Task.FromResult(true); });
        string cost = await CurrentCostAsync(host);

        string result = await CallAsync(host, "update_ingredient", new Dictionary<string, object?> { ["code"] = IngredientCode, ["owncost"] = double.Parse(cost) });

        Assert.Contains("Preview", result);
        Assert.Equal(0, asked);
    }

    [FactIfDatabase]
    public async Task ConfirmTrue_UserDeclines_NothingIsApplied()
    {
        int asked = 0;
        List<string> serverLog = new List<string>();
        await using McpToolHost host = await ConnectAsync((message, ct) => { asked++; return Task.FromResult(false); }, line => { lock (serverLog) { serverLog.Add(line); } });
        string costBefore = await CurrentCostAsync(host);

        string result = await CallAsync(host, "update_ingredient", new Dictionary<string, object?> { ["code"] = IngredientCode, ["owncost"] = double.Parse(costBefore), ["confirm"] = true });

        Assert.Equal(1, asked);
        Assert.Contains("not applied", result);
        Assert.DoesNotContain("Change committed", result);
        Assert.Equal(costBefore, await CurrentCostAsync(host));

        // A decline is a normal outcome: one audit line, and the server must not log it as a crash.
        await WaitForLogAsync(serverLog, line => line.StartsWith("[Audit]", StringComparison.Ordinal) && line.Contains("not applied"));
        lock (serverLog)
        {
            Assert.DoesNotContain(serverLog, line => line.Contains("unhandled exception"));
        }
    }

    [FactIfDatabase]
    public async Task ConfirmTrue_UserCancels_NothingIsApplied()
    {
        await using McpToolHost host = await ConnectAsync((message, ct) => throw new OperationCanceledException());
        string costBefore = await CurrentCostAsync(host);

        string result = await CallAsync(host, "update_ingredient", new Dictionary<string, object?> { ["code"] = IngredientCode, ["owncost"] = double.Parse(costBefore), ["confirm"] = true });

        Assert.Contains("not applied", result);
        Assert.DoesNotContain("Change committed", result);
    }

    [FactIfDatabase]
    public async Task ConfirmTrue_ClientCannotPrompt_IsBlocked()
    {
        await using McpToolHost host = await ConnectAsync(null);
        string costBefore = await CurrentCostAsync(host);

        string result = await CallAsync(host, "update_ingredient", new Dictionary<string, object?> { ["code"] = IngredientCode, ["owncost"] = double.Parse(costBefore), ["confirm"] = true });

        Assert.Contains("cannot show the confirmation prompt", result);
        Assert.DoesNotContain("Change committed", result);
        Assert.Equal(costBefore, await CurrentCostAsync(host));
    }

    [FactIfDatabase]
    public async Task BinderTool_UserDeclines_NothingIsApplied()
    {
        int asked = 0;
        await using McpToolHost host = await ConnectAsync((message, ct) => { asked++; return Task.FromResult(false); });

        // Clearing the binder of an ingredient that has none is a no-op (0 -> 0).
        string result = await CallAsync(host, "set_ingredient_binder", new Dictionary<string, object?> { ["code"] = IngredientCode, ["confirm"] = true });

        Assert.Equal(1, asked);
        Assert.Contains("not applied", result);
        Assert.DoesNotContain("Change committed", result);
    }

    [FactIfDatabase]
    public async Task BinderTool_ClientCannotPrompt_IsBlocked()
    {
        await using McpToolHost host = await ConnectAsync(null);

        string result = await CallAsync(host, "set_ingredient_binder", new Dictionary<string, object?> { ["code"] = IngredientCode, ["confirm"] = true });

        Assert.Contains("cannot show the confirmation prompt", result);
        Assert.DoesNotContain("Change committed", result);
    }

    [FactIfWritesAllowed]
    public async Task ConfirmTrue_UserApproves_ChangeIsApplied_ThenRestored()
    {
        int asked = 0;
        string? shownToUser = null;
        await using McpToolHost host = await ConnectAsync((message, ct) => { asked++; shownToUser = message; return Task.FromResult(true); });
        string original = await CurrentCostAsync(host);
        double changed = double.Parse(original) + 0.01;

        try
        {
            string applied = await CallAsync(host, "update_ingredient", new Dictionary<string, object?> { ["code"] = IngredientCode, ["owncost"] = changed, ["confirm"] = true });

            Assert.Equal(1, asked);
            Assert.Contains(IngredientCode, shownToUser);
            Assert.Contains("Change committed", applied);
            Assert.Equal(changed, double.Parse(await CurrentCostAsync(host)), 6);
        }
        finally
        {
            await CallAsync(host, "update_ingredient", new Dictionary<string, object?> { ["code"] = IngredientCode, ["owncost"] = double.Parse(original), ["confirm"] = true });
        }

        Assert.Equal(double.Parse(original), double.Parse(await CurrentCostAsync(host)), 6);
    }

    [FactIfDatabase]
    public async Task FindBinder_ReadsABindingAgentFromTheServer()
    {
        // Read-only. Guards the lookup the opt-in binder test depends on, so a change in the
        // list_binding_agents reply shows up here without anyone having to enable writes.
        await using McpToolHost host = await ConnectAsync(null);

        (string code, bool _) = await FindBinderAsync(host);

        Assert.False(string.IsNullOrWhiteSpace(code));
    }

    [FactIfWritesAllowed]
    public async Task BinderTool_UserApproves_ChangeIsApplied_ThenRestored()
    {
        List<string> shownToUser = new List<string>();
        await using McpToolHost host = await ConnectAsync((message, ct) =>
        {
            lock (shownToUser)
            {
                shownToUser.Add(message);
            }

            return Task.FromResult(true);
        });

        // Refuse to start unless the ingredient has no binder, so the restore below puts back exactly
        // what was there. This preview writes nothing.
        string before = await CallAsync(host, "set_ingredient_binder", new Dictionary<string, object?> { ["code"] = IngredientCode });
        Assert.True(before.Contains("binder: (none) -> (none)"), $"{IngredientCode} already has a binder; refusing to change it. Preview was: {before}");

        (string binderCode, bool binderWasActive) = await FindBinderAsync(host);
        bool activatedBinder = false;
        bool bindingAttempted = false;

        try
        {
            if (!binderWasActive)
            {
                // The tool rejects an inactive binder, so switch it on for the test and back off after.
                activatedBinder = true;
                string activated = await CallAsync(host, "update_ingredient", new Dictionary<string, object?> { ["code"] = binderCode, ["is_active"] = true, ["confirm"] = true });
                Assert.Contains("Change committed", activated);
            }

            bindingAttempted = true;
            Dictionary<string, object?> setArguments = new Dictionary<string, object?>
            {
                ["code"] = IngredientCode,
                ["binder_code"] = binderCode,
                ["base_amount"] = 100.0,
                ["required_amount"] = 5.0,
                ["confirm"] = true,
            };
            string applied = await CallAsync(host, "set_ingredient_binder", setArguments);

            Assert.Contains("Change committed", applied);
            lock (shownToUser)
            {
                Assert.Contains(shownToUser, m => m.Contains("set_ingredient_binder") && m.Contains(IngredientCode) && m.Contains(binderCode));
            }

            // Read it back through the tool's own preview: the current binder now equals the new one.
            setArguments["confirm"] = false;
            string readBack = await CallAsync(host, "set_ingredient_binder", setArguments);
            Assert.Contains($"binder: {binderCode} (", readBack);
            Assert.DoesNotContain("binder: (none)", readBack);
        }
        finally
        {
            if (bindingAttempted)
            {
                await CallAsync(host, "set_ingredient_binder", new Dictionary<string, object?> { ["code"] = IngredientCode, ["confirm"] = true });
            }

            if (activatedBinder)
            {
                await CallAsync(host, "update_ingredient", new Dictionary<string, object?> { ["code"] = binderCode, ["is_active"] = false, ["confirm"] = true });
            }
        }

        string after = await CallAsync(host, "set_ingredient_binder", new Dictionary<string, object?> { ["code"] = IngredientCode });
        Assert.True(after.Contains("binder: (none) -> (none)"), $"{IngredientCode} was not restored to having no binder. Clear it with set_ingredient_binder. Preview was: {after}");

        (string _, bool binderActiveAfter) = await FindBinderAsync(host);
        Assert.Equal(binderWasActive, binderActiveAfter);
    }

    private static async Task<(string Code, bool IsActive)> FindBinderAsync(McpToolHost host)
    {
        string json = await CallAsync(host, "list_binding_agents", new Dictionary<string, object?>());
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement first = document.RootElement[0];
            return (first.GetProperty("ingredientcode").GetString()!, first.GetProperty("is_active").GetBoolean());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentOutOfRangeException)
        {
            Assert.Fail($"Could not find a binding agent to use. list_binding_agents returned: {json}");
            throw;
        }
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CFCAgent.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root (CFCAgent.sln).");
    }

    private static async Task WaitForLogAsync(List<string> serverLog, Func<string, bool> predicate)
    {
        // The server's stderr is read on another thread, so the line can arrive just after the call returns.
        for (int attempt = 0; attempt < 40; attempt++)
        {
            lock (serverLog)
            {
                if (serverLog.Any(predicate))
                {
                    return;
                }
            }

            await Task.Delay(50);
        }

        lock (serverLog)
        {
            Assert.Fail("Expected server log line never arrived. Lines seen: " + string.Join(" | ", serverLog));
        }
    }

    private static Task<McpToolHost> ConnectAsync(Func<string, CancellationToken, Task<bool>>? confirm, Action<string>? onServerLog = null)
    {
        string root = FindRepoRoot();
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string serverDll = Path.Combine(root, "src", "CFCAgent.McpServer", "bin", configuration, "net10.0", "CFCAgent.McpServer.dll");

        return McpToolHost.ConnectAsync(new McpConnectionOptions
        {
            Command = "dotnet",
            Arguments = new[] { serverDll },
            ProductConfigPath = Path.Combine(root, "products", "cfc-fertilizer", "schema-map.yaml"),
            ConfirmAsync = confirm,
            OnServerLog = onServerLog,
        });
    }

    private static async Task<string> CallAsync(McpToolHost host, string toolName, Dictionary<string, object?> arguments)
    {
        AIFunction function = host.Tools.OfType<AIFunction>().First(t => t.Name == toolName);
        try
        {
            object? result = await function.InvokeAsync(new AIFunctionArguments(arguments));
            return result?.ToString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static async Task<string> CurrentCostAsync(McpToolHost host)
    {
        string json = await CallAsync(host, "get_ingredient", new Dictionary<string, object?> { ["code"] = IngredientCode });
        Match match = Regex.Match(json, "owncost\\\\?\"\\s*:\\s*([0-9.]+)");
        Assert.True(match.Success, $"Could not read owncost from: {json}");
        return match.Groups[1].Value;
    }
}

public sealed class FactIfDatabaseAttribute : FactAttribute
{
    public FactIfDatabaseAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CFCCHATBOT_DB_CONNECTION_STRING")))
        {
            Skip = "CFCCHATBOT_DB_CONNECTION_STRING is not set.";
        }
    }
}

public sealed class FactIfWritesAllowedAttribute : FactAttribute
{
    public FactIfWritesAllowedAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CFCCHATBOT_DB_CONNECTION_STRING")))
        {
            Skip = "CFCCHATBOT_DB_CONNECTION_STRING is not set.";
        }
        else if (Environment.GetEnvironmentVariable("CFCAGENT_ALLOW_WRITE_TESTS") != "1")
        {
            Skip = "Set CFCAGENT_ALLOW_WRITE_TESTS=1 to run the test that makes (and restores) a real change.";
        }
    }
}
