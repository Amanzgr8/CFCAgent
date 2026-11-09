using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace CFCAgent.App.Core;

public sealed record ConnectionTestResult(bool Success, string Message);

// Checks a database connection string without saving it, for the settings screen's Test connection button.
public interface IConnectionTester
{
    Task<ConnectionTestResult> TestAsync(string connectionString, string? productConfigPath, CancellationToken cancellationToken);
}

// The real tester: starts the MCP server read-only with the connection string handed to it, runs its
// connection check (a plain "SELECT 1"), and shuts the server down again. Whatever it reports is scrubbed
// of the connection string and its password first, so a failure message can never put the secret on screen.
public sealed class LocalBackendConnectionTester : IConnectionTester
{
    public async Task<ConnectionTestResult> TestAsync(string connectionString, string? productConfigPath, CancellationToken cancellationToken)
    {
        try
        {
            LocalBackendConnector connector = new LocalBackendConnector(new LocalBackendOptions
            {
                ReadOnly = true,
                ForceStubModel = true,
                ConnectionStringOverride = connectionString,
                ProductConfigPath = string.IsNullOrWhiteSpace(productConfigPath) ? null : productConfigPath,
            });

            await using ChatConnection connection = await connector.ConnectAsync(cancellationToken);
            AIFunction? check = connection.Tools.OfType<AIFunction>().FirstOrDefault(tool => tool.Name == "check_database_connection");
            if (check is null)
            {
                return new ConnectionTestResult(false, "The server did not offer its connection check, so the connection could not be tested.");
            }

            object? result = await check.InvokeAsync(new AIFunctionArguments(), cancellationToken);
            string text = Redact(result?.ToString() ?? string.Empty, connectionString);
            return new ConnectionTestResult(text.Contains("Database connection OK", StringComparison.Ordinal), text);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult(false, Redact(ex.Message, connectionString));
        }
    }

    // Removes the connection string, and the password inside it, from a piece of text.
    public static string Redact(string text, string connectionString)
    {
        string cleaned = connectionString.Length > 0 ? text.Replace(connectionString, "***", StringComparison.Ordinal) : text;
        foreach (Match match in Regex.Matches(connectionString, @"(?i)(?:password|pwd)\s*=\s*([^;]*)"))
        {
            string secret = match.Groups[1].Value.Trim().Trim('\'', '"');
            if (secret.Length >= 3)
            {
                cleaned = cleaned.Replace(secret, "***", StringComparison.Ordinal);
            }
        }

        return cleaned;
    }
}
