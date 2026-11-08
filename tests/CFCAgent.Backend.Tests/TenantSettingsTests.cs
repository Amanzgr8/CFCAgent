using CFCAgent.Backend;

namespace CFCAgent.Backend.Tests;

public sealed class InMemoryCredentialStore : ICredentialStore
{
    public Dictionary<string, string> Secrets { get; } = new Dictionary<string, string>();

    public string? Get(string target)
    {
        return Secrets.TryGetValue(target, out string? secret) ? secret : null;
    }

    public void Set(string target, string userName, string secret)
    {
        Secrets[target] = secret;
    }

    public bool Delete(string target)
    {
        return Secrets.Remove(target);
    }
}

public sealed class TenantSettingsTests : IDisposable
{
    private const string SecretConnectionString = "Host=db.example;Database=cfc;Username=app;Password=hunter2-secret";

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
    public void Load_WhenNothingHasBeenSetUp_ReturnsNull()
    {
        Assert.Null(NewSettings().Load());
    }

    [Fact]
    public void SaveThenLoad_RoundTripsTheTenantAndItsConnectionString()
    {
        TenantSettings settings = NewSettings();

        settings.Save(new TenantConfig { TenantName = "acme", ProductConfigPath = @"C:\products\schema-map.yaml" }, SecretConnectionString);

        TenantConfig? loaded = settings.Load();
        Assert.NotNull(loaded);
        Assert.Equal("acme", loaded.TenantName);
        Assert.Equal(@"C:\products\schema-map.yaml", loaded.ProductConfigPath);
        Assert.Equal(SecretConnectionString, settings.LoadConnectionString(loaded));
    }

    [Fact]
    public void Save_NeverWritesTheSecretToTheConfigFile()
    {
        TenantSettings settings = NewSettings();

        settings.Save(new TenantConfig { TenantName = "acme" }, SecretConnectionString);

        string fileText = File.ReadAllText(settings.ConfigPath);
        Assert.DoesNotContain("hunter2", fileText);
        Assert.DoesNotContain("Password", fileText);
        Assert.Equal(SecretConnectionString, _credentials.Get(TenantSettings.CredentialTarget("acme")));
    }

    [Theory]
    [InlineData("", "Host=x")]
    [InlineData("acme", "")]
    [InlineData("acme", "   ")]
    public void Save_RejectsABlankNameOrConnectionString(string tenantName, string connectionString)
    {
        TenantSettings settings = NewSettings();

        Assert.Throws<ArgumentException>(() => settings.Save(new TenantConfig { TenantName = tenantName }, connectionString));
        Assert.False(File.Exists(settings.ConfigPath));
        Assert.Empty(_credentials.Secrets);
    }

    [Fact]
    public void LoadConnectionString_WhenNothingIsStored_ReturnsNull()
    {
        TenantSettings settings = NewSettings();

        Assert.Null(settings.LoadConnectionString(new TenantConfig { TenantName = "acme" }));
    }

    [Fact]
    public void Forget_RemovesTheConfigFileAndTheStoredSecret()
    {
        TenantSettings settings = NewSettings();
        settings.Save(new TenantConfig { TenantName = "acme" }, SecretConnectionString);

        bool removedSecret = settings.Forget();

        Assert.True(removedSecret);
        Assert.False(File.Exists(settings.ConfigPath));
        Assert.Empty(_credentials.Secrets);
        Assert.Null(settings.Load());
    }

    [Fact]
    public void Forget_WhenNothingIsSetUp_ReportsNothingRemoved()
    {
        Assert.False(NewSettings().Forget());
    }

    [Fact]
    public void Load_WithoutATenantName_Throws()
    {
        TenantSettings settings = NewSettings();
        Directory.CreateDirectory(_directory);
        File.WriteAllText(settings.ConfigPath, "{ \"productConfigPath\": \"x\" }");

        Assert.ThrowsAny<Exception>(() => settings.Load());
    }
}
