using CFCAgent.Backend;

namespace CFCAgent.Backend.Tests;

public class CredentialStoreFactoryTests
{
    [Fact]
    public void Create_OnWindows_ReturnsTheCredentialManagerStore()
    {
        Assert.IsType<WindowsCredentialStore>(CredentialStoreFactory.Create(isWindows: true));
    }

    [Fact]
    public void Create_OffWindows_ReturnsTheStoreThatExplainsTheGap()
    {
        Assert.IsType<UnsupportedCredentialStore>(CredentialStoreFactory.Create(isWindows: false));
    }

    [Fact]
    public void Create_ForTheCurrentOs_MatchesTheOperatingSystem()
    {
        ICredentialStore store = CredentialStoreFactory.Create();

        if (OperatingSystem.IsWindows())
        {
            Assert.IsType<WindowsCredentialStore>(store);
        }
        else
        {
            Assert.IsType<UnsupportedCredentialStore>(store);
        }
    }

    [Fact]
    public void UnsupportedStore_ReadsNothingAndRemovesNothing()
    {
        UnsupportedCredentialStore store = new UnsupportedCredentialStore("TestOS");

        Assert.Null(store.Get("CFCAgent:database:any"));
        Assert.False(store.Delete("CFCAgent:database:any"));
    }

    [Fact]
    public void UnsupportedStore_RefusesToSaveAndNamesTheFallback()
    {
        UnsupportedCredentialStore store = new UnsupportedCredentialStore("TestOS");

        PlatformNotSupportedException ex = Assert.Throws<PlatformNotSupportedException>(
            () => store.Set("CFCAgent:database:any", "user", "secret-value"));

        Assert.Contains("TestOS", ex.Message);
        Assert.Contains("CFCCHATBOT_DB_CONNECTION_STRING", ex.Message);
        Assert.DoesNotContain("secret-value", ex.Message);
    }
}
