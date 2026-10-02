using System.Windows;
using System.Windows.Input;
using ZCue.Views;
using WpfTextBox = System.Windows.Controls.TextBox;
using WinFormsClipboard = System.Windows.Forms.Clipboard;
using WinFormsDataObject = System.Windows.Forms.DataObject;
using WinFormsTextDataFormat = System.Windows.Forms.TextDataFormat;

namespace ZCue.Infrastructure;

public static class TextContextMenuService
{
    private static int _initialized;

    public static void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
        {
            return;
        }

        EventManager.RegisterClassHandler(
            typeof(WpfTextBox),
            CommandManager.PreviewExecutedEvent,
            new ExecutedRoutedEventHandler(HandlePreviewExecuted));
    }

    private static void HandlePreviewExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Command != ApplicationCommands.Copy
            || sender is not WpfTextBox textBox
            || textBox.SelectionLength == 0)
        {
            return;
        }

        e.Handled = true;
        try
        {
            // 与已有剪切和列表复制使用同一写入方式，避开 WPF 默认复制的等待。
            var clipboardData = new WinFormsDataObject();
            clipboardData.SetText(textBox.SelectedText, WinFormsTextDataFormat.UnicodeText);
            WinFormsClipboard.SetDataObject(clipboardData, true, 20, 25);
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.ExternalException
            or InvalidOperationException)
        {
            AppDialogWindow.ShowMessage(
                Window.GetWindow(textBox),
                "复制失败",
                "无法写入系统剪贴板，请稍后重试。");
        }
    }
}
