using CFCAgent.Backend;

namespace CFCAgent.Backend.Tests;

public sealed class TenantConnectionTests : IDisposable
{
    private const string DefaultProductConfig = @"C:\repo\products\cfc-fertilizer\schema-map.yaml";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "cfcagent-tests-" + Guid.NewGuid().ToString("N"));
    private readonly InMemoryCredentialStore _credentials = new InMemoryCredentialStore();

    private TenantSettings NewSettings()
    {
        return new TenantSettings(Path.Combine(_directory, "tenant.json"), _credentials);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Resolve_WithNoTenantSetUp_FallsBackToTheDefaultsAndTheEnvironment()
    {
        ResolvedConnection resolved = TenantConnection.Resolve(NewSettings(), DefaultProductConfig, null);

        Assert.Equal(DefaultProductConfig, resolved.ProductConfigPath);
        Assert.Null(resolved.DatabaseConnectionString);
    }

    [Fact]
    public void Resolve_WithNoTenantSetUp_HonoursAProductConfigOverride()
    {
        ResolvedConnection resolved = TenantConnection.Resolve(NewSettings(), DefaultProductConfig, @"C:\other\schema-map.yaml");

        Assert.Equal(@"C:\other\schema-map.yaml", resolved.ProductConfigPath);
        Assert.Null(resolved.DatabaseConnectionString);
    }

    [Fact]
    public void Resolve_WithATenant_UsesItsStoredConnectionAndProductConfig()
    {
        TenantSettings settings = NewSettings();
        settings.Save(new TenantConfig { TenantName = "acme", ProductConfigPath = @"C:\acme\schema-map.yaml" }, "Host=acme-db");

        ResolvedConnection resolved = TenantConnection.Resolve(settings, DefaultProductConfig, null);

        Assert.Equal("Host=acme-db", resolved.DatabaseConnectionString);
        Assert.Equal(@"C:\acme\schema-map.yaml", resolved.ProductConfigPath);
        Assert.Contains("acme", resolved.Source);
    }

    [Fact]
    public void Resolve_WithATenantWithoutAProductConfig_UsesTheDefault()
    {
        TenantSettings settings = NewSettings();
        settings.Save(new TenantConfig { TenantName = "acme" }, "Host=acme-db");

        ResolvedConnection resolved = TenantConnection.Resolve(settings, DefaultProductConfig, null);

        Assert.Equal(DefaultProductConfig, resolved.ProductConfigPath);
    }

    [Fact]
    public void Resolve_AnExplicitProductConfigOverrideBeatsTheTenants()
    {
        TenantSettings settings = NewSettings();
        settings.Save(new TenantConfig { TenantName = "acme", ProductConfigPath = @"C:\acme\schema-map.yaml" }, "Host=acme-db");

        ResolvedConnection resolved = TenantConnection.Resolve(settings, DefaultProductConfig, @"C:\override.yaml");

        Assert.Equal(@"C:\override.yaml", resolved.ProductConfigPath);
        Assert.Equal("Host=acme-db", resolved.DatabaseConnectionString);
    }

    [Fact]
    public void Resolve_WhenTheTenantsConnectionIsMissing_FailsAndPointsAtSetup()
    {
        TenantSettings settings = NewSettings();
        settings.Save(new TenantConfig { TenantName = "acme" }, "Host=acme-db");
        _credentials.Secrets.Clear();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => TenantConnection.Resolve(settings, DefaultProductConfig, null));

        Assert.Contains("--setup", ex.Message);
        Assert.Contains("acme", ex.Message);
    }
}
