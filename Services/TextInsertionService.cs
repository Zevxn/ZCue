using System.Runtime.InteropServices;
using System.Text;
using ZCue.Infrastructure;
using WpfClipboard = System.Windows.Clipboard;
using WpfDataObject = System.Windows.IDataObject;

namespace ZCue.Services;

public sealed class TextInsertionService
{
    private const int ClipboardPasteLengthThreshold = 32;
    private const int ClipboardRetryCount = 5;
    private const int ClipboardRetryDelayMilliseconds = 25;
    private const int ClipboardWriteRetryCount = 20;
    private const int ClipboardReadDelayAfterPasteMilliseconds = 150;

    public async Task<bool> ReplaceAsync(
        IntPtr targetWindow,
        int deleteLength,
        string replacement,
        IntPtr clipboardOwnerWindow)
    {
        var text = replacement ?? string.Empty;
        if (!IsTargetForeground(targetWindow))
        {
            return false;
        }

        if (ShouldUseClipboardPaste(text))
        {
            return await PasteTextAsync(
                targetWindow,
                clipboardOwnerWindow,
                deleteLength,
                text).ConfigureAwait(true);
        }

        if (deleteLength > 0 && !SendBackspaces(deleteLength))
        {
            return false;
        }

        // 短文本优先用 Unicode 键盘包；少数不处理 VK_PACKET 的程序再退回粘贴。
        return SendUnicodeText(text)
            || await PasteTextAsync(
                targetWindow,
                clipboardOwnerWindow,
                0,
                text).ConfigureAwait(true);
    }

    private static bool ShouldUseClipboardPaste(string text)
    {
        return text.Length >= ClipboardPasteLengthThreshold
            || text.Contains('\r')
            || text.Contains('\n')
            || text.Contains('\u2028')
            || text.Contains('\u2029');
    }

    // SECTION 剪贴板文本粘贴

    private static async Task<bool> PasteTextAsync(
        IntPtr targetWindow,
        IntPtr clipboardOwnerWindow,
        int deleteLength,
        string replacement)
    {
        if (!IsTargetForeground(targetWindow))
        {
            return false;
        }

        var (clipboardCaptured, originalClipboard) = await TryGetClipboardDataAsync().ConfigureAwait(true);
        var clipboardChanged = false;

        try
        {
            var (clipboardWriteSucceeded, clipboardWasChanged) = await TrySetClipboardTextAsync(
                NormalizeClipboardLineEndings(replacement),
                clipboardOwnerWindow).ConfigureAwait(true);
            clipboardChanged = clipboardWasChanged;
            if (!clipboardWriteSucceeded)
            {
                return false;
            }

            if (!IsTargetForeground(targetWindow))
            {
                return false;
            }

            if (deleteLength > 0 && !SendBackspaces(deleteLength))
            {
                return false;
            }

            if (!IsTargetForeground(targetWindow))
            {
                return false;
            }

            if (!SendPaste())
            {
                return false;
            }

            // 留出时间让目标应用读取剪贴板，再恢复用户原有内容。
            await Task.Delay(ClipboardReadDelayAfterPasteMilliseconds).ConfigureAwait(true);
            return true;
        }
        finally
        {
            if (clipboardChanged && clipboardCaptured)
            {
                await RestoreClipboardAsync(originalClipboard).ConfigureAwait(true);
            }
            // 原剪贴板读取失败时仍完成粘贴；此时无法安全恢复之前的剪贴板内容。
        }
    }

    private static async Task<(bool Captured, WpfDataObject? Data)> TryGetClipboardDataAsync()
    {
        for (var attempt = 0; attempt < ClipboardRetryCount; attempt++)
        {
            try
            {
                return (true, WpfClipboard.GetDataObject());
            }
            catch (ExternalException)
            {
                // 剪贴板可能正被其他程序短暂占用，稍后重试。
            }
            catch (InvalidOperationException)
            {
                // 剪贴板可能正被其他程序短暂占用，稍后重试。
            }

            if (attempt + 1 < ClipboardRetryCount)
            {
                await Task.Delay(ClipboardRetryDelayMilliseconds).ConfigureAwait(true);
            }
        }

        return (false, null);
    }

    private static async Task<(bool Succeeded, bool ClipboardChanged)> TrySetClipboardTextAsync(
        string text,
        IntPtr clipboardOwnerWindow)
    {
        var clipboardChanged = false;
        for (var attempt = 0; attempt < ClipboardWriteRetryCount; attempt++)
        {
            if (TrySetNativeClipboardText(text, clipboardOwnerWindow, out var changedThisAttempt))
            {
                return (true, true);
            }
            clipboardChanged |= changedThisAttempt;

            if (attempt + 1 < ClipboardWriteRetryCount)
            {
                await Task.Delay(ClipboardRetryDelayMilliseconds).ConfigureAwait(true);
            }
        }

        return (false, clipboardChanged);
    }

    private static bool TrySetNativeClipboardText(
        string text,
        IntPtr clipboardOwnerWindow,
        out bool clipboardChanged)
    {
        clipboardChanged = false;
        if (clipboardOwnerWindow == IntPtr.Zero)
        {
            return false;
        }

        var unicodeText = Encoding.Unicode.GetBytes(text + '\0');
        var clipboardMemory = NativeMethods.GlobalAlloc(
            NativeMethods.GMEM_MOVEABLE | NativeMethods.GMEM_ZEROINIT,
            new UIntPtr((uint)unicodeText.Length));
        if (clipboardMemory == IntPtr.Zero)
        {
            return false;
        }

        var clipboardOwnsMemory = false;
        try
        {
            var memoryPointer = NativeMethods.GlobalLock(clipboardMemory);
            if (memoryPointer == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                Marshal.Copy(unicodeText, 0, memoryPointer, unicodeText.Length);
            }
            finally
            {
                NativeMethods.GlobalUnlock(clipboardMemory);
            }

            if (!NativeMethods.OpenClipboard(clipboardOwnerWindow))
            {
                return false;
            }

            try
            {
                if (!NativeMethods.EmptyClipboard())
                {
                    return false;
                }
                clipboardChanged = true;

                if (NativeMethods.SetClipboardData(NativeMethods.CF_UNICODETEXT, clipboardMemory) == IntPtr.Zero)
                {
                    return false;
                }

                clipboardOwnsMemory = true;
                return true;
            }
            finally
            {
                NativeMethods.CloseClipboard();
            }
        }
        finally
        {
            if (!clipboardOwnsMemory)
            {
                NativeMethods.GlobalFree(clipboardMemory);
            }
        }
    }

    private static async Task RestoreClipboardAsync(WpfDataObject? originalClipboard)
    {
        for (var attempt = 0; attempt < ClipboardRetryCount; attempt++)
        {
            try
            {
                if (originalClipboard is null)
                {
                    WpfClipboard.Clear();
                }
                else
                {
                    WpfClipboard.SetDataObject(originalClipboard, true);
                }

                return;
            }
            catch (ExternalException)
            {
                // 剪贴板被其他进程占用时，稍后重试恢复。
            }
            catch (InvalidOperationException)
            {
                // 剪贴板被其他进程占用时，稍后重试恢复。
            }

            if (attempt + 1 < ClipboardRetryCount)
            {
                await Task.Delay(ClipboardRetryDelayMilliseconds).ConfigureAwait(true);
            }
        }

    }

    private static string NormalizeClipboardLineEndings(string text)
    {
        return text.Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Replace('\u2028', '\n')
            .Replace('\u2029', '\n')
            .Replace("\n", "\r\n");
    }

    // !SECTION 剪贴板文本粘贴

    // SECTION SendInput 文本注入

    private static bool SendBackspaces(int count)
    {
        var inputs = new List<NativeMethods.Input>(count * 2);
        for (var index = 0; index < count; index++)
        {
            inputs.Add(CreateKeyInput(NativeMethods.VK_BACK, keyUp: false));
            inputs.Add(CreateKeyInput(NativeMethods.VK_BACK, keyUp: true));
        }

        return SendInputs(inputs);
    }

    private static bool SendPaste()
    {
        var inputs = new[]
        {
            CreateKeyInput(NativeMethods.VK_CONTROL, keyUp: false),
            CreateKeyInput(NativeMethods.VK_V, keyUp: false),
            CreateKeyInput(NativeMethods.VK_V, keyUp: true),
            CreateKeyInput(NativeMethods.VK_CONTROL, keyUp: true)
        };

        return SendInputs(inputs);
    }

    private static bool SendUnicodeText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        var inputs = new List<NativeMethods.Input>(text.Length * 2);
        foreach (var character in text)
        {
            inputs.Add(CreateUnicodeInput(character, keyUp: false));
            inputs.Add(CreateUnicodeInput(character, keyUp: true));
        }

        return SendInputs(inputs);
    }

    private static NativeMethods.Input CreateKeyInput(int virtualKeyCode, bool keyUp)
    {
        return new NativeMethods.Input
        {
            Type = NativeMethods.INPUT_KEYBOARD,
            Data = new NativeMethods.InputUnion
            {
                Keyboard = new NativeMethods.KeyboardInput
                {
                    VirtualKey = (ushort)virtualKeyCode,
                    ScanCode = 0,
                    Flags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0,
                    Time = 0,
                    ExtraInfo = IntPtr.Zero
                }
            }
        };
    }

    private static NativeMethods.Input CreateUnicodeInput(char character, bool keyUp)
    {
        return new NativeMethods.Input
        {
            Type = NativeMethods.INPUT_KEYBOARD,
            Data = new NativeMethods.InputUnion
            {
                Keyboard = new NativeMethods.KeyboardInput
                {
                    VirtualKey = 0,
                    ScanCode = character,
                    Flags = NativeMethods.KEYEVENTF_UNICODE
                        | (keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0),
                    Time = 0,
                    ExtraInfo = IntPtr.Zero
                }
            }
        };
    }

    private static bool SendInputs(IReadOnlyList<NativeMethods.Input> inputs)
    {
        if (inputs.Count == 0)
        {
            return true;
        }

        var nativeInputs = inputs.ToArray();
        var sent = NativeMethods.SendInput(
            (uint)nativeInputs.Length,
            nativeInputs,
            Marshal.SizeOf<NativeMethods.Input>());
        return sent == nativeInputs.Length;
    }

    // !SECTION SendInput 文本注入

    private static bool IsTargetForeground(IntPtr targetWindow)
    {
        return targetWindow != IntPtr.Zero
            && NativeMethods.GetForegroundWindow() == targetWindow;
    }
}
