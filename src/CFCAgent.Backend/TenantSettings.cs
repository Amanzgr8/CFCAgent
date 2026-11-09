using System.Text.Json;

namespace CFCAgent.Backend;

// Reads and writes one tenant's setup: the non-secret tenant.json plus the connection string in the
// credential store. The CLI's --setup uses this today; a settings screen in the desktop app would
// call the same methods, so there is one tested path for entering and reading a connection.
public sealed class TenantSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _configPath;
    private readonly ICredentialStore _credentials;

    public TenantSettings(string configPath, ICredentialStore credentials)
    {
        _configPath = configPath;
        _credentials = credentials;
    }

    public static string DefaultConfigPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CFCAgent", "tenant.json");

    public string ConfigPath => _configPath;

    public static string CredentialTarget(string tenantName) => $"CFCAgent:database:{tenantName}";

    // Returns null when this install has not been set up (no tenant.json).
    public TenantConfig? Load()
    {
        if (!File.Exists(_configPath))
        {
            return null;
        }

        TenantConfig? config = JsonSerializer.Deserialize<TenantConfig>(File.ReadAllText(_configPath), JsonOptions);
        if (config is null || string.IsNullOrWhiteSpace(config.TenantName))
        {
            throw new InvalidDataException($"{_configPath} is not a valid tenant config: it needs a tenantName.");
        }

        return config;
    }

    // Returns null when the tenant has no stored connection string.
    public string? LoadConnectionString(TenantConfig config)
    {
        string? stored = _credentials.Get(CredentialTarget(config.TenantName));
        return string.IsNullOrWhiteSpace(stored) ? null : stored;
    }

    public void Save(TenantConfig config, string connectionString)
    {
        if (string.IsNullOrWhiteSpace(config.TenantName))
        {
            throw new ArgumentException("A tenant name is required.", nameof(config));
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A connection string is required.", nameof(connectionString));
        }

        _credentials.Set(CredentialTarget(config.TenantName), config.TenantName, connectionString);

        string? directory = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_configPath, JsonSerializer.Serialize(config, JsonOptions));
    }

    // Removes one tenant's stored connection string without touching tenant.json. For when a tenant is renamed:
    // the connection string is saved under the new name, and the old name's copy must not be left behind.
    // Returns true when something was removed.
    public bool RemoveStoredConnection(string tenantName)
    {
        return _credentials.Delete(CredentialTarget(tenantName));
    }

    // Removes the stored connection string and tenant.json. Returns true when a stored connection
    // string was removed.
    public bool Forget()
    {
        TenantConfig? config = Load();
        bool removedSecret = config is not null && _credentials.Delete(CredentialTarget(config.TenantName));

        if (File.Exists(_configPath))
        {
            File.Delete(_configPath);
        }

        return removedSecret;
    }
}
