namespace CFCAgent.Backend;

// Where a session's connection settings came from: the product config to serve, the database
// connection string to hand to the server (null means "leave it to the environment"), and a short
// description for display.
public sealed record ResolvedConnection(string ProductConfigPath, string? DatabaseConnectionString, string Source);

// Chooses a session's connection settings. A set-up tenant wins: its stored connection string is
// handed to the server directly. With no tenant.json the behaviour is exactly what it was before
// tenants existed - the product config comes from the override or the default path, and the server
// finds its connection string in the CFCCHATBOT_DB_CONNECTION_STRING environment variable - so an
// existing setup keeps working.
public static class TenantConnection
{
    public static ResolvedConnection Resolve(TenantSettings settings, string defaultProductConfigPath, string? productConfigOverride)
    {
        TenantConfig? tenant = settings.Load();
        if (tenant is null)
        {
            return new ResolvedConnection(
                productConfigOverride ?? defaultProductConfigPath,
                null,
                "environment variables (no tenant setup found)");
        }

        string? connectionString = settings.LoadConnectionString(tenant);
        if (connectionString is null)
        {
            throw new InvalidOperationException(
                $"Tenant '{tenant.TenantName}' has no stored database connection. Run the CLI with --setup to enter it, or --forget to remove the tenant setup.");
        }

        return new ResolvedConnection(
            productConfigOverride ?? tenant.ProductConfigPath ?? defaultProductConfigPath,
            connectionString,
            $"tenant '{tenant.TenantName}'");
    }
}
