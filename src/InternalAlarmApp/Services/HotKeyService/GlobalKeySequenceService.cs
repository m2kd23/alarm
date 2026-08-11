using System.Diagnostics;
using System.Runtime.InteropServices;

namespace InternalAlarmApp.Services.HotKeyService;

public sealed class GlobalKeySequenceService : IDisposable
{
    private readonly Native.LowLevelKeyboardProc _callback;
    private IntPtr _hook;
    private int _virtualKey;
    private int _interval;
    private long _lastPress;
    private bool _isDown;

    public event EventHandler? Triggered;

    public GlobalKeySequenceService(int virtualKey, int intervalMilliseconds)
    {
        _virtualKey = virtualKey;
        _interval = intervalMilliseconds;
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

    public void Update(int virtualKey, int intervalMilliseconds)
    {
        _virtualKey = virtualKey;
        _interval = Math.Clamp(intervalMilliseconds, 200, 2000);
        _lastPress = 0;
    }

    private IntPtr HookCallback(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            var vk = Marshal.ReadInt32(data);
            var msg = message.ToInt32();
            if ((msg == 0x100 || msg == 0x104) && vk == _virtualKey && !_isDown)
            {
                _isDown = true;
                var now = Environment.TickCount64;
                if (_lastPress > 0 && now - _lastPress <= _interval)
                {
                    _lastPress = 0;
                    Triggered?.Invoke(this, EventArgs.Empty);
                }
                else _lastPress = now;
            }
            else if ((msg == 0x101 || msg == 0x105) && vk == _virtualKey) _isDown = false;
        }
        return Native.CallNextHookEx(_hook, code, message, data);
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
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)] internal static extern IntPtr GetModuleHandle(string? moduleName);
    }
}
