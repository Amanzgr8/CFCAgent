using CFCAgent.Backend;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CFCAgent.App.Core;

// The settings screen: this install's tenant name, product config, and database connection string. It is the
// window-side twin of the CLI's --setup and uses the same TenantSettings, so there is one tested path for
// reading and writing a connection. The connection string is a secret: it is typed into a masked box, goes
// straight to the credential store, and is never shown again, not even to its owner. A saved connection
// appears only as "a connection is saved".
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly TenantSettings _settings;
    private readonly IConnectionTester _tester;
    private string? _loadedTenantName;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _tenantName = "default";

    // Blank means the product's default config.
    [ObservableProperty]
    private string _productConfigPath = "";

    // What the user typed. Blank means "keep the saved one". Cleared as soon as it is saved or the panel closes.
    [ObservableProperty]
    private string _connectionString = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SecretHint))]
    private bool _hasStoredConnection;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _statusIsError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestConnectionCommand))]
    private bool _isBusy;

    public SettingsViewModel(TenantSettings settings, IConnectionTester tester, string defaultProductConfigPath, string storeName)
    {
        _settings = settings;
        _tester = tester;
        DefaultProductConfigPath = defaultProductConfigPath;
        StoreName = storeName;
    }

    // Raised after the settings were saved. The chat reconnects with them.
    public event EventHandler? Saved;

    public string DefaultProductConfigPath { get; }

    // Where the connection string is kept, named for the person reading the screen.
    public string StoreName { get; }

    public string SecretHint => HasStoredConnection
        ? "A connection is saved. Leave this blank to keep it, or paste a new one to replace it."
        : "Paste the database connection string here";

    // True when there is no tenant setup or no saved connection to go with it.
    public bool NeedsSetup()
    {
        try
        {
            TenantConfig? tenant = _settings.Load();
            return tenant is null || _settings.LoadConnectionString(tenant) is null;
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException)
        {
            return true;
        }
    }

    // Shows the screen with what is saved now (the name and product config; never the connection string).
    public void Open(string? message = null, bool isError = false)
    {
        TenantConfig? tenant = null;
        try
        {
            tenant = _settings.Load();
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException)
        {
            message ??= $"The saved tenant setup could not be read ({ex.Message}). Saving will replace it.";
            isError = true;
        }

        _loadedTenantName = tenant?.TenantName;
        TenantName = tenant?.TenantName ?? "default";
        ProductConfigPath = tenant?.ProductConfigPath ?? "";
        HasStoredConnection = tenant is not null && SafeHasStoredConnection(tenant);
        ConnectionString = "";
        SetStatus(message ?? "", isError);
        IsOpen = true;
    }

    // Closes the screen and drops anything typed into the secret box.
    [RelayCommand]
    public void Close()
    {
        ConnectionString = "";
        IsOpen = false;
    }

    private bool CanRun()
    {
        return !IsBusy;
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void Save()
    {
        string name = TenantName.Trim();
        if (name.Length == 0)
        {
            SetStatus("Enter a tenant name.", isError: true);
            return;
        }

        string? connection = EffectiveConnectionString(name);
        if (connection is null)
        {
            SetStatus("Enter the database connection string. Nothing is saved yet.", isError: true);
            return;
        }

        try
        {
            string? configPath = string.IsNullOrWhiteSpace(ProductConfigPath) ? null : ProductConfigPath.Trim();
            _settings.Save(new TenantConfig { TenantName = name, ProductConfigPath = configPath }, connection);

            // A renamed tenant: the connection string is now saved under the new name, so remove the old copy.
            if (_loadedTenantName is not null && !string.Equals(_loadedTenantName, name, StringComparison.Ordinal))
            {
                _settings.RemoveStoredConnection(_loadedTenantName);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetStatus($"Could not save: {ConnectionTestRedaction(ex.Message, connection)}", isError: true);
            return;
        }

        _loadedTenantName = name;
        HasStoredConnection = true;
        ConnectionString = "";
        SetStatus($"Saved. The connection string is in {StoreName}.", isError: false);
        IsOpen = false;
        Saved?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanRun), IncludeCancelCommand = true)]
    private async Task TestConnectionAsync(CancellationToken cancellationToken)
    {
        string name = TenantName.Trim();
        string? connection = EffectiveConnectionString(name);
        if (connection is null)
        {
            SetStatus("Enter the database connection string to test.", isError: true);
            return;
        }

        IsBusy = true;
        SetStatus("Testing the connection...", isError: false);
        try
        {
            ConnectionTestResult result = await _tester.TestAsync(connection, ProductConfigPath.Trim(), cancellationToken);
            SetStatus(result.Success ? "Connection OK. This only tested it; nothing is saved until you press Save." : $"Connection failed: {result.Message}", !result.Success);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Test cancelled.", isError: false);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // What was typed, or else what is already saved under this tenant. Null when there is neither.
    private string? EffectiveConnectionString(string tenantName)
    {
        string typed = ConnectionString.Trim();
        if (typed.Length > 0)
        {
            return typed;
        }

        if (!HasStoredConnection || _loadedTenantName is null)
        {
            return null;
        }

        try
        {
            return _settings.LoadConnectionString(new TenantConfig { TenantName = _loadedTenantName });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private bool SafeHasStoredConnection(TenantConfig tenant)
    {
        try
        {
            return _settings.LoadConnectionString(tenant) is not null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    private static string ConnectionTestRedaction(string text, string connection)
    {
        return LocalBackendConnectionTester.Redact(text, connection);
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        StatusIsError = isError;
    }
}
