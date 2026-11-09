namespace CFCAgent.Backend;

// Stands in for a credential store on an operating system that has no implementation yet (macOS
// Keychain and Linux Secret Service are planned). Reading finds nothing, so a machine that has never
// been set up behaves normally and the server falls back to its environment variable. Saving fails
// with a message that says what to do instead, rather than pretending to store a secret.
public sealed class UnsupportedCredentialStore : ICredentialStore
{
    private readonly string _operatingSystem;

    public UnsupportedCredentialStore(string operatingSystem)
    {
        _operatingSystem = operatingSystem;
    }

    public string? Get(string target)
    {
        return null;
    }

    public void Set(string target, string userName, string secret)
    {
        throw new PlatformNotSupportedException(
            $"There is no secure credential store for {_operatingSystem} yet, so the database connection string cannot be saved here. " +
            "Set the CFCCHATBOT_DB_CONNECTION_STRING environment variable instead.");
    }

    public bool Delete(string target)
    {
        return false;
    }
}
