using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace HakoPods.App.Popup;

/// <summary>
/// Keyboard and mouse observation while the popup is visible, so it can honour "Esc or
/// click-away dismisses" without ever taking focus.
///
/// A no-activate window never receives key or mouse messages, so a local handler cannot work;
/// low-level hooks are the only way to see them. They are installed only while the popup is
/// visible and removed in <see cref="Dispose"/> — a permanent hook would be a system-wide tax
/// for a card that is on screen for six seconds.
/// </summary>
public sealed class GlobalInputHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int WmLeftButtonDown = 0x0201;
    private const int WmRightButtonDown = 0x0204;
    private const int VkEscape = 0x1B;

    private readonly Dispatcher _dispatcher;
    private readonly LowLevelProc _keyboardProc;
    private readonly LowLevelProc _mouseProc;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private bool _disposed;

    public GlobalInputHook(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _keyboardProc = KeyboardCallback;
        _mouseProc = MouseCallback;
    }

    public event Action? EscapePressed;

    /// <summary>Raised with a 1×1 screen rectangle at the click point.</summary>
    public event Action<Rect>? ClickedOutside;

    public void Install()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;

        var handle = module is null ? IntPtr.Zero : GetModuleHandle(module.ModuleName);
        _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardProc, handle, 0);
        _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseProc, handle, 0);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }

        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
    }

    private IntPtr KeyboardCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && (wParam == WmKeyDown || wParam == WmSysKeyDown) && Marshal.ReadInt32(lParam) == VkEscape)
        {
            _dispatcher.BeginInvoke(EscapePressed);
        }

        return CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    private IntPtr MouseCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && (wParam == WmLeftButtonDown || wParam == WmRightButtonDown))
        {
            var structure = Marshal.PtrToStructure<MsllHookStruct>(lParam);
            var point = new Rect(structure.Point.X, structure.Point.Y, 1, 1);
            _dispatcher.BeginInvoke(() => ClickedOutside?.Invoke(point));
        }

        return CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    private delegate IntPtr LowLevelProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int hookId, LowLevelProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point32
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsllHookStruct
    {
        public Point32 Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }
}
