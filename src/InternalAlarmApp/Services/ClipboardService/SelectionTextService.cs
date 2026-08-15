using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;
using InternalAlarmApp.Utils;
using Forms = System.Windows.Forms;

namespace InternalAlarmApp.Services.ClipboardService;

public sealed class SelectionTextService
{
    public async Task<string> GetSelectedTextAsync()
    {
        var automationText = TryReadThroughAutomation();
        if (!string.IsNullOrWhiteSpace(automationText))
        {
            Logger.Info($"선택 텍스트 수집 성공(UI Automation, {automationText.Length}자)");
            return automationText.Trim();
        }
        var clipboardText = (await CopyWithoutChangingClipboardAsync()).Trim();
        Logger.Info(clipboardText.Length > 0
            ? $"선택 텍스트 수집 성공(Clipboard, {clipboardText.Length}자)"
            : "선택 텍스트 수집 실패(UI Automation/Clipboard 모두 빈 값)");
        return clipboardText;
    }

    private static string TryReadThroughAutomation()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null || (bool)focused.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty)) return "";
            if (!focused.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)) return "";
            var selection = ((TextPattern)pattern).GetSelection();
            return string.Join(Environment.NewLine, selection.Select(x => x.GetText(-1))).Trim();
        }
        catch { return ""; }
    }

    private static async Task<string> CopyWithoutChangingClipboardAsync()
    {
        var backup = TrySnapshotClipboard();
        try
        {
            TryClearClipboard();
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var sent = attempt == 0 ? SendCopyWithNativeInput() : SendCopyWithSendKeys();
                Logger.Info($"Clipboard 복사 시도 {attempt + 1}: 입력 전송={(sent ? "성공" : "실패")}");
                for (var i = 0; i < 24; i++)
                {
                    await Task.Delay(50);
                    try
                    {
                        var text = TryGetClipboardText();
                        if (!string.IsNullOrWhiteSpace(text)) return text;
                    }
                    catch (COMException) { }
                }
                await Task.Delay(100);
            }
            return "";
        }
        finally
        {
            if (backup != null)
            {
                await Task.Delay(40);
                TrySetClipboard(backup);
            }
            else TryClearClipboard();
        }
    }

    private static string TryGetClipboardText()
    {
        var data = Clipboard.GetDataObject();
        if (data is null) return "";
        foreach (var format in new[] { DataFormats.UnicodeText, DataFormats.Text, DataFormats.OemText })
        {
            if (!data.GetDataPresent(format, false)) continue;
            if (data.GetData(format, false) is string text && !string.IsNullOrWhiteSpace(text)) return text;
        }
        return Clipboard.ContainsText() ? Clipboard.GetText() : "";
    }

    private static DataObject? TrySnapshotClipboard()
    {
        try
        {
            var source = Clipboard.GetDataObject();
            if (source == null) return null;
            var copy = new DataObject();
            foreach (var format in source.GetFormats(false))
            {
                try
                {
                    var value = source.GetData(format, false);
                    if (value != null) copy.SetData(format, value);
                }
                catch { }
            }
            return copy;
        }
        catch { return null; }
    }

    private static void TryClearClipboard() { try { Clipboard.Clear(); } catch { } }
    private static void TrySetClipboard(IDataObject data)
    {
        for (var i = 0; i < 5; i++)
        {
            try { Clipboard.SetDataObject(data, true); return; }
            catch (COMException) { Thread.Sleep(20); }
        }
    }

    private static bool SendCopyWithNativeInput()
    {
        var inputs = new[]
        {
            new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = 0x11 } } },
            new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = 0x43 } } },
            new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = 0x43, Flags = 2 } } },
            new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = 0x11, Flags = 2 } } }
        };
        return Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) == inputs.Length;
    }

    private static bool SendCopyWithSendKeys()
    {
        try { Forms.SendKeys.SendWait("^c"); return true; }
        catch { return false; }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort VirtualKey; public ushort ScanCode; public uint Flags; public uint Time; public IntPtr ExtraInfo; }
    private static class Native
    {
        [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, Input[] inputs, int size);
    }
}
