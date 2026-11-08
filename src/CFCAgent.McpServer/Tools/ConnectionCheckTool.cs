using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Npgsql;

namespace CFCAgent.McpServer.Tools;

// Hand-written read tool that answers one question: can the server reach its database right now?
// Opens a connection and runs a trivial query, so a wrong host, port, user or password shows up as a
// clear message. Used by the backend's connection test after a tenant's connection string is
// entered. Built in Program.cs with the data source handed in, like the other hand-written tools,
// so a server started without a connection string does not offer it.
public sealed class ConnectionCheckTool(NpgsqlDataSource dataSource)
{
    [McpServerTool(Name = "check_database_connection")]
    [Description("Checks that the server can connect to the database and run a query. Reports success, or the reason the connection failed. Changes nothing. Only use it when the user asks to test the connection or when diagnosing why other tools fail to reach the database.")]
    public async Task<string> CheckDatabaseConnection(CancellationToken cancellationToken = default)
    {
        try
        {
            await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);
            return "Database connection OK.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new McpException($"Could not connect to the database: {ex.Message}");
        }
    }
}
