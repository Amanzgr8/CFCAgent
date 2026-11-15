using CFCAgent.App.Core;
using CFCAgent.Backend;
using Microsoft.Extensions.AI;

namespace CFCAgent.App.Core.Tests;

// Tests the real connector against the real, built MCP server. The offline ones give the server a connection
// string that points nowhere (it only fails when a query is run, so starting up and listing tools still work),
// which proves the read-only filtering and the setup flow without touching any database.
public class LocalBackendConnectorTests : IDisposable
{
    // A connection string for a database that does not exist. The password is a marker the tests look for to
    // prove it never turns up in anything the app shows.
    private const string Unreachable = "Host=127.0.0.1;Port=1;Database=nothing;Username=nobody;Password=SECRET-MARKER-9137;Timeout=1";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "cfcagent-connector-tests-" + Guid.NewGuid().ToString("N"));

    private sealed class InMemoryCredentials : ICredentialStore
    {
        private readonly Dictionary<string, string> _secrets = new Dictionary<string, string>();

        public string? Get(string target) => _secrets.TryGetValue(target, out string? value) ? value : null;

        public void Set(string target, string userName, string secret) => _secrets[target] = secret;

        public bool Delete(string target) => _secrets.Remove(target);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private LocalBackendOptions SetUpTenant(bool readOnly, out InMemoryCredentials credentials)
    {
        credentials = new InMemoryCredentials();
        string tenantConfig = Path.Combine(_folder, "tenant.json");
        new TenantSettings(tenantConfig, credentials).Save(new TenantConfig { TenantName = "unit-test" }, Unreachable);
        return new LocalBackendOptions
        {
            ReadOnly = readOnly,
            TenantConfigPath = tenantConfig,
            Credentials = credentials,
            ForceStubModel = true,
        };
    }

    [Fact]
    public async Task ReadOnlyConnection_OffersOnlyToolsDeclaredReadOnly()
    {
        LocalBackendOptions options = SetUpTenant(readOnly: true, out InMemoryCredentials _);

        await using ChatConnection connection = await new LocalBackendConnector(options).ConnectAsync(CancellationToken.None);

        Assert.NotEmpty(connection.Tools);
        Assert.All(connection.Tools, tool => Assert.True(ToolFilter.IsDeclaredReadOnly(tool), $"{tool.Name} is not declared read-only"));
        Assert.DoesNotContain(connection.Tools, tool => tool.Name.StartsWith("update_") || tool.Name.StartsWith("set_"));
        Assert.Contains("tenant 'unit-test'", connection.Description);
        Assert.Contains("read-only", connection.Description);
    }

    [Fact]
    public async Task ReadOnlyConnection_HidesTheWriteToolsThatAreOtherwiseThere()
    {
        LocalBackendOptions readOnly = SetUpTenant(readOnly: true, out InMemoryCredentials _);
        await using ChatConnection narrow = await new LocalBackendConnector(readOnly).ConnectAsync(CancellationToken.None);

        LocalBackendOptions everything = SetUpTenant(readOnly: false, out InMemoryCredentials _);
        await using ChatConnection wide = await new LocalBackendConnector(everything).ConnectAsync(CancellationToken.None);

        Assert.True(wide.Tools.Count > narrow.Tools.Count, "a connection that allows changes should offer more tools");
        Assert.Contains(wide.Tools, tool => tool.Name == "update_ingredient");
        Assert.DoesNotContain(narrow.Tools, tool => tool.Name == "update_ingredient");
    }

    [Fact]
    public async Task TheConnectionStringNeverAppearsInWhatTheAppShows()
    {
        LocalBackendOptions options = SetUpTenant(readOnly: true, out InMemoryCredentials _);

        await using ChatConnection connection = await new LocalBackendConnector(options).ConnectAsync(CancellationToken.None);

        Assert.DoesNotContain("SECRET-MARKER-9137", connection.Description);
        Assert.DoesNotContain(connection.ServerLog.RecentLines(10), line => line.Contains("SECRET-MARKER-9137"));
    }

    [Fact]
    public async Task UnreachableDatabase_IsReportedByTheCheckToolWithoutLeakingTheConnectionString()
    {
        LocalBackendOptions options = SetUpTenant(readOnly: true, out InMemoryCredentials _);
        await using ChatConnection connection = await new LocalBackendConnector(options).ConnectAsync(CancellationToken.None);
        AIFunction check = connection.Tools.OfType<AIFunction>().Single(tool => tool.Name == "check_database_connection");

        object? result = await check.InvokeAsync(new AIFunctionArguments());

        Assert.DoesNotContain("Database connection OK", result?.ToString() ?? "");
        Assert.DoesNotContain("SECRET-MARKER-9137", result?.ToString() ?? "");
    }

    [Fact]
    public async Task MissingServer_ExplainsWhatToDo()
    {
        LocalBackendOptions options = new LocalBackendOptions { ServerDllPath = Path.Combine(_folder, "nope", "CFCAgent.McpServer.dll") };

        FileNotFoundException error = await Assert.ThrowsAsync<FileNotFoundException>(
            () => new LocalBackendConnector(options).ConnectAsync(CancellationToken.None));

        Assert.Contains("has not been built", error.Message);
    }

    [Fact]
    public async Task TenantWithoutAStoredConnection_FailsWithTheSetupMessage()
    {
        InMemoryCredentials empty = new InMemoryCredentials();
        string tenantConfig = Path.Combine(_folder, "tenant.json");
        Directory.CreateDirectory(_folder);
        File.WriteAllText(tenantConfig, "{ \"tenantName\": \"no-secret\" }");
        LocalBackendOptions options = new LocalBackendOptions { TenantConfigPath = tenantConfig, Credentials = empty, ForceStubModel = true };

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new LocalBackendConnector(options).ConnectAsync(CancellationToken.None));

        Assert.Contains("no stored database connection", error.Message);
    }

    // Opt-in: uses this machine's real tenant setup (Windows Credential Manager) and the real database.
    // It only reads: the connection is read-only and the one tool called is the connection check.
    [FactIfLive]
    public async Task RealTenant_ConnectsReadOnlyAndTheDatabaseAnswers()
    {
        LocalBackendOptions options = new LocalBackendOptions { ReadOnly = true, ForceStubModel = true };

        await using ChatConnection connection = await new LocalBackendConnector(options).ConnectAsync(CancellationToken.None);
        AIFunction check = connection.Tools.OfType<AIFunction>().Single(tool => tool.Name == "check_database_connection");
        object? result = await check.InvokeAsync(new AIFunctionArguments());

        Assert.Contains("Database connection OK", result?.ToString() ?? "");
        Assert.All(connection.Tools, tool => Assert.True(ToolFilter.IsDeclaredReadOnly(tool)));
    }
}

public sealed class FactIfLiveAttribute : FactAttribute
{
    public FactIfLiveAttribute()
    {
        if (Environment.GetEnvironmentVariable("CFCAGENT_LIVE_TESTS") != "1")
        {
            Skip = "Set CFCAGENT_LIVE_TESTS=1 to run the test that uses the real tenant setup and database (read-only).";
        }
    }
}
