using System.Runtime.InteropServices;

namespace CFCAgent.Backend;

// Picks the credential store for the operating system the app is running on, so no caller has to name
// a platform-specific class. Windows uses Credential Manager; anywhere else gets a store that explains
// the gap until a Keychain or Secret Service implementation is added here.
public static class CredentialStoreFactory
{
    public static ICredentialStore Create()
    {
        return Create(OperatingSystem.IsWindows());
    }

    public static ICredentialStore Create(bool isWindows)
    {
        return isWindows
            ? new WindowsCredentialStore()
            : new UnsupportedCredentialStore(RuntimeInformation.OSDescription);
    }
}
