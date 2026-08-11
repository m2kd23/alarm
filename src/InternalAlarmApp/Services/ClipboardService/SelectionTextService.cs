using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;

namespace InternalAlarmApp.Services.ClipboardService;

public sealed class SelectionTextService
{
    public async Task<string> GetSelectedTextAsync()
    {
        var automationText = TryReadThroughAutomation();
        if (!string.IsNullOrWhiteSpace(automationText)) return automationText.Trim();
        return (await CopyWithoutChangingClipboardAsync()).Trim();
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
            SendCopy();
            for (var i = 0; i < 10; i++)
            {
                await Task.Delay(35);
                try
                {
                    if (Clipboard.ContainsText()) return Clipboard.GetText();
                }
                catch (COMException) { }
            }
            return "";
        }
        finally
        {
            if (backup != null)
            {
                await Task.Delay(20);
                TrySetClipboard(backup);
            }
        }
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

    private static void SendCopy()
    {
        var inputs = new[]
        {
            new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = 0x11 } } },
            new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = 0x43 } } },
            new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = 0x43, Flags = 2 } } },
            new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = 0x11, Flags = 2 } } }
        };
        Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort VirtualKey; public ushort ScanCode; public uint Flags; public uint Time; public IntPtr ExtraInfo; }
    private static class Native { [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, Input[] inputs, int size); }
}
