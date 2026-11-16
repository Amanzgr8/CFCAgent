using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CFCAgent.App.Core;

namespace CFCAgent.App;

// The settings screen's view. When it opens, the focus goes to the first box that needs attention: the
// connection string if nothing is saved yet, otherwise the tenant name. All the logic is in SettingsViewModel.
public partial class SettingsPanel : UserControl
{
    public SettingsPanel()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(
            () =>
            {
                if (DataContext is SettingsViewModel { HasStoredConnection: false })
                {
                    SecretBox.Focus();
                }
                else
                {
                    TenantBox.Focus();
                }
            },
            DispatcherPriority.Background);
    }
}
