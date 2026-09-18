using System.ComponentModel;
using System.Windows;
using TypeSense.Services;
using TypeSense.ViewModels;

namespace TypeSense.Views;

public partial class PromptManagerWindow : Window
{
    private bool _allowClose;

    public PromptManagerWindow(PromptCatalogService catalog)
    {
        InitializeComponent();
        DataContext = new PromptManagerViewModel(catalog);
        Closing += HandleClosing;
    }

    public void CloseWithoutHiding()
    {
        if (!IsVisible)
        {
            return;
        }

        _allowClose = true;
        Close();
    }

    private void HandleClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }
}
