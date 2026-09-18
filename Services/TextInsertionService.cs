using System.Runtime.InteropServices;
using TypeSense.Infrastructure;
using WpfClipboard = System.Windows.Clipboard;
using WpfDataObject = System.Windows.IDataObject;
using WpfTextDataFormat = System.Windows.TextDataFormat;

namespace TypeSense.Services;

public sealed class TextInsertionService
{
    public async Task<bool> ReplaceAsync(IntPtr targetWindow, int deleteLength, string replacement)
    {
        if (targetWindow == IntPtr.Zero || NativeMethods.GetForegroundWindow() != targetWindow)
        {
            return false;
        }

        var originalClipboard = TryGetClipboardData(out var clipboardCaptured);
        var clipboardChanged = false;

        try
        {
            WpfClipboard.SetText(replacement ?? string.Empty, WpfTextDataFormat.UnicodeText);
            clipboardChanged = true;

            if (deleteLength > 0 && !SendBackspaces(deleteLength))
            {
                return false;
            }

            if (!SendPaste())
            {
                return false;
            }

            // 给目标应用一个短暂时间读取剪贴板，再恢复用户原有内容。
            await Task.Delay(80).ConfigureAwait(true);
            return true;
        }
        catch (ExternalException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        finally
        {
            if (clipboardChanged && clipboardCaptured)
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
                }
                catch (ExternalException)
                {
                    // 剪贴板被其他进程占用时，不能影响已完成的文本替换。
                }
                catch (InvalidOperationException)
                {
                    // 剪贴板被其他进程占用时，不能影响已完成的文本替换。
                }
            }
        }
    }

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

    private static WpfDataObject? TryGetClipboardData(out bool captured)
    {
        try
        {
            var data = WpfClipboard.GetDataObject();
            captured = true;
            return data;
        }
        catch (ExternalException)
        {
            captured = false;
            return null;
        }
        catch (InvalidOperationException)
        {
            captured = false;
            return null;
        }
    }
}
