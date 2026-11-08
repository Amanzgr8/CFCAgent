using CFCAgent.Backend;
using Microsoft.Extensions.AI;

namespace CFCAgent.Backend.Tests;

// Both tests only read. They check that a connection string handed to the server by the backend (as
// a tenant's stored one will be) is the one the server really uses, rather than whatever the
// environment holds.
public class ConnectionCheckIntegrationTests
{
    [FactIfDatabase]
    public async Task CheckDatabaseConnection_WithTheEnvironmentsConnectionString_ReportsOk()
    {
        string connectionString = Environment.GetEnvironmentVariable("CFCCHATBOT_DB_CONNECTION_STRING")!;
        await using McpToolHost host = await TestServer.ConnectAsync(null, databaseConnectionString: connectionString);

        string result = await CheckAsync(host);

        Assert.Contains("Database connection OK", result);
    }

    [FactIfDatabase]
    public async Task CheckDatabaseConnection_WithAConnectionStringHandedToTheServer_UsesThatOne()
    {
        // The environment holds a good connection string; the one passed in is deliberately wrong. If
        // the server used the inherited variable instead, this would report OK.
        const string wrong = "Host=127.0.0.1;Port=1;Database=nope;Username=nobody;Password=none;Timeout=3;Pooling=false";
        await using McpToolHost host = await TestServer.ConnectAsync(null, databaseConnectionString: wrong);

        string result = await CheckAsync(host);

        Assert.Contains("Could not connect to the database", result);
        Assert.DoesNotContain("Database connection OK", result);
    }

    private static async Task<string> CheckAsync(McpToolHost host)
    {
        AIFunction function = host.Tools.OfType<AIFunction>().First(t => t.Name == "check_database_connection");
        try
        {
            object? result = await function.InvokeAsync(new AIFunctionArguments());
            return result?.ToString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
