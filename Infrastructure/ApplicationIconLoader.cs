using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace ZCue.Infrastructure;

internal static class ApplicationIconLoader
{
    public static BitmapSource? Load(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return null;
        }

        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(executablePath);
            if (icon is null)
            {
                return null;
            }

            var bitmap = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            // 后台读取后交给 WPF 绑定；位图冻结后不再依赖已释放的图标句柄。
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
