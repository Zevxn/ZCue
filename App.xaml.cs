using TypeSense.Services;

namespace TypeSense;

public partial class App : System.Windows.Application
{
    private AppController? _controller;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);

        try
        {
            _controller = new AppController(Dispatcher);
            _controller.Start();
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                $"TypeSense 启动失败：{exception.Message}",
                "TypeSense",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _controller?.Dispose();
        base.OnExit(e);
    }
}
