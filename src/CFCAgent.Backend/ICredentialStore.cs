namespace CFCAgent.Backend;

// Where secrets (a tenant's database connection string) are kept between runs. The backend only
// depends on this interface, so the Windows implementation can be swapped for another platform's
// store, or an in-memory one in tests, without touching the code that uses it.
public interface ICredentialStore
{
    // Returns the stored secret, or null when nothing is stored under this target.
    string? Get(string target);

    void Set(string target, string userName, string secret);

    // Returns true when something was stored and has now been removed.
    bool Delete(string target);
}
