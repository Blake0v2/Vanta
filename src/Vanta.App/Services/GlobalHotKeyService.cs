using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Interop;
using Vanta.Interop;

namespace Vanta.Services;

internal sealed class GlobalHotKeyService : IDisposable
{
    private const int HotKeyId = 0x5641;
    private HwndSource? _source;
    private IntPtr _windowHandle;
    private bool _registered;

    public event EventHandler? Pressed;

    public void Attach(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;
        _source = HwndSource.FromHwnd(windowHandle);
        _source?.AddHook(WindowProc);
    }

    public void Register(string modifier, string key)
    {
        Unregister();

        var modifiers = modifier switch
        {
            "Ctrl" => NativeMethods.ModControl,
            "Shift" => NativeMethods.ModShift,
            "Win" => NativeMethods.ModWin,
            _ => NativeMethods.ModAlt
        };

        var virtualKey = KeyInterop.VirtualKeyFromKey(ParseKey(key));
        if (!NativeMethods.RegisterHotKey(_windowHandle, HotKeyId, modifiers | NativeMethods.ModNoRepeat, (uint)virtualKey))
        {
            throw new Win32Exception("That hotkey is already in use by another application.");
        }

        _registered = true;
    }

    public static int GetVirtualKey(string key) => KeyInterop.VirtualKeyFromKey(ParseKey(key));

    private static Key ParseKey(string key)
    {
        return Enum.TryParse<Key>(key, true, out var parsed) ? parsed : Key.Q;
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotKey && wParam.ToInt32() == HotKeyId)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void Unregister()
    {
        if (_registered)
        {
            NativeMethods.UnregisterHotKey(_windowHandle, HotKeyId);
            _registered = false;
        }
    }

    public void Dispose()
    {
        Unregister();
        _source?.RemoveHook(WindowProc);
    }
}
