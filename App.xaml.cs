using TypeSense.Services;
using TypeSense.Views;

namespace TypeSense;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Local\TypeSense.SingleInstance";
    private const string ActivationEventName = @"Local\TypeSense.ActivateExistingInstance";

    private AppController? _controller;
    private System.Threading.Mutex? _singleInstanceMutex;
    private System.Threading.EventWaitHandle? _activationEvent;
    private System.Threading.RegisteredWaitHandle? _activationWaitRegistration;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);

        try
        {
            _singleInstanceMutex = new System.Threading.Mutex(
                initiallyOwned: false,
                name: SingleInstanceMutexName,
                createdNew: out var createdNew);
            if (!createdNew)
            {
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
                NotifyExistingInstance();
                Shutdown();
                return;
            }

            _activationEvent = new System.Threading.EventWaitHandle(
                initialState: false,
                mode: System.Threading.EventResetMode.AutoReset,
                name: ActivationEventName);
            _controller = new AppController(Dispatcher);
            _controller.Start();
            _activationWaitRegistration = System.Threading.ThreadPool.RegisterWaitForSingleObject(
                _activationEvent,
                (_, _) =>
                {
                    if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                    {
                        Dispatcher.BeginInvoke(new Action(() => _controller?.ActivateManager()));
                    }
                },
                null,
                System.Threading.Timeout.Infinite,
                executeOnlyOnce: false);
        }
        catch (Exception exception)
        {
            AppDialogWindow.ShowMessage(
                null,
                "TypeSense",
                $"TypeSense 启动失败：{exception.Message}",
                AppDialogTone.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _activationWaitRegistration?.Unregister(null);
        _activationEvent?.Dispose();
        _controller?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static void NotifyExistingInstance()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var activationEvent = System.Threading.EventWaitHandle.OpenExisting(ActivationEventName);
                activationEvent.Set();
                return;
            }
            catch (System.Threading.WaitHandleCannotBeOpenedException)
            {
                if (attempt < 9)
                {
                    System.Threading.Thread.Sleep(50);
                }
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
        }
    }
}
