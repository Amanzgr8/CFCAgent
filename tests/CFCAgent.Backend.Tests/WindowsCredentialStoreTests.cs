using CFCAgent.Backend;

namespace CFCAgent.Backend.Tests;

// These use the real Windows Credential Manager of whoever runs them, under a throwaway target
// name that is removed again afterwards, so they leave nothing behind. The secret is a dummy.
public class WindowsCredentialStoreTests
{
    [FactIfWindows]
    public void SetGetDelete_RoundTripsASecretIncludingSpecialCharacters()
    {
        WindowsCredentialStore store = new WindowsCredentialStore();
        string target = "CFCAgent-test:" + Guid.NewGuid().ToString("N");
        string secret = "Host=localhost;Password=\"p;a'ss=word\";Note=héllo ✓";

        try
        {
            Assert.Null(store.Get(target));

            store.Set(target, "test-user", secret);
            Assert.Equal(secret, store.Get(target));

            store.Set(target, "test-user", "replaced");
            Assert.Equal("replaced", store.Get(target));

            Assert.True(store.Delete(target));
            Assert.Null(store.Get(target));
            Assert.False(store.Delete(target));
        }
        finally
        {
            store.Delete(target);
        }
    }

    [FactIfWindows]
    public void Set_RejectsASecretTooLongForCredentialManager()
    {
        WindowsCredentialStore store = new WindowsCredentialStore();
        string target = "CFCAgent-test:" + Guid.NewGuid().ToString("N");

        try
        {
            Assert.Throws<ArgumentException>(() => store.Set(target, "test-user", new string('x', 2000)));
            Assert.Null(store.Get(target));
        }
        finally
        {
            store.Delete(target);
        }
    }
}

public sealed class FactIfWindowsAttribute : FactAttribute
{
    public FactIfWindowsAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Windows Credential Manager is only available on Windows.";
        }
    }
}
