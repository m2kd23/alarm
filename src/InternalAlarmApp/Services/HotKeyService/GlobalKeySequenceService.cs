using System.Diagnostics;
using System.Runtime.InteropServices;

namespace InternalAlarmApp.Services.HotKeyService;

public sealed class GlobalKeySequenceService : IDisposable
{
    private readonly Native.LowLevelKeyboardProc _callback;
    private IntPtr _hook;
    private int _virtualKey;
    private int _interval;
    private int _modifiers;
    private int _pressCount;
    private long _lastPress;
    private bool _isDown;
    private bool _suppressCurrentKey;

    public event EventHandler? Triggered;

    public GlobalKeySequenceService(int virtualKey, int intervalMilliseconds, int modifiers = 0, int pressCount = 2)
    {
        _virtualKey = virtualKey;
        _interval = intervalMilliseconds;
        _modifiers = modifiers;
        _pressCount = Math.Clamp(pressCount, 1, 2);
        _callback = HookCallback;
    }

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        _hook = Native.SetWindowsHookEx(13, _callback, Native.GetModuleHandle(module?.ModuleName), 0);
        if (_hook == IntPtr.Zero) throw new InvalidOperationException("전역 단축키를 등록할 수 없습니다. 다른 프로그램과의 충돌을 확인해 주세요.");
    }

    public void Update(int virtualKey, int intervalMilliseconds, int modifiers = 0, int pressCount = 2)
    {
        _virtualKey = virtualKey;
        _interval = Math.Clamp(intervalMilliseconds, 200, 2000);
        _modifiers = modifiers;
        _pressCount = Math.Clamp(pressCount, 1, 2);
        _lastPress = 0;
    }

    private IntPtr HookCallback(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            var vk = Marshal.ReadInt32(data);
            var msg = message.ToInt32();
            if ((msg == 0x100 || msg == 0x104) && vk == _virtualKey && !_isDown && ModifiersMatch())
            {
                _isDown = true;
                _suppressCurrentKey = _modifiers != 0;
                if (_pressCount == 1)
                {
                    _lastPress = 0;
                    Triggered?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    var now = Environment.TickCount64;
                    if (_lastPress > 0 && now - _lastPress <= _interval)
                    {
                        _lastPress = 0;
                        Triggered?.Invoke(this, EventArgs.Empty);
                    }
                    else _lastPress = now;
                }
                if (_suppressCurrentKey) return new IntPtr(1);
            }
            else if ((msg == 0x101 || msg == 0x105) && vk == _virtualKey)
            {
                _isDown = false;
                if (_suppressCurrentKey) { _suppressCurrentKey = false; return new IntPtr(1); }
            }
        }
        return Native.CallNextHookEx(_hook, code, message, data);
    }

    private bool ModifiersMatch()
    {
        var ctrl = (Native.GetAsyncKeyState(0x11) & 0x8000) != 0;
        var alt = (Native.GetAsyncKeyState(0x12) & 0x8000) != 0;
        var shift = (Native.GetAsyncKeyState(0x10) & 0x8000) != 0;
        return ((_modifiers & 1) == 0 || ctrl)
            && ((_modifiers & 2) == 0 || alt)
            && ((_modifiers & 4) == 0 || shift);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        GC.SuppressFinalize(this);
    }

    private static class Native
    {
        internal delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc callback, IntPtr module, uint threadId);
        [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int virtualKey);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)] internal static extern IntPtr GetModuleHandle(string? moduleName);
    }
}
