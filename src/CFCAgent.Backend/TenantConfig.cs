namespace CFCAgent.Backend;

// The non-secret half of a tenant's setup, kept in tenant.json: a name for the install and,
// optionally, which product's schema map to serve. The database connection string is the secret
// half and lives in the credential store, never in this file.
public sealed class TenantConfig
{
    public required string TenantName { get; init; }

    public string? ProductConfigPath { get; init; }
}
