using Avalonia.Controls;
using CFCAgent.App.Core;

namespace CFCAgent.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        RuntimeText.Text = RuntimeSummary.Describe();
    }
}
