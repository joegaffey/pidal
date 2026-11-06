using System.Runtime.InteropServices;

namespace Pidal.DirectInput;

/// <summary>
/// A real, hidden top-level window used for DirectInput's cooperative level.
/// A console/ConPTY window handle is not reliable for this under Windows
/// Terminal, so PIDAL creates its own message window.
/// </summary>
internal sealed class HiddenWindow : IDisposable
{
    private readonly WndProc _wndProc;
    private readonly IntPtr _instance;
    private readonly string _className;
    private IntPtr _handle;
    private bool _disposed;

    private HiddenWindow(IntPtr instance, string className, IntPtr handle, WndProc wndProc)
    {
        _instance = instance;
        _className = className;
        _handle = handle;
        _wndProc = wndProc;
    }

    public IntPtr Handle => _handle;

    public static HiddenWindow Create()
    {
        var instance = NativeMethods.GetModuleHandle(null);
        if (instance == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"GetModuleHandle failed: {Marshal.GetLastWin32Error()}.");
        }

        var className = "PIDAL.DirectInput." + Guid.NewGuid().ToString("N");
        var wndProc = new WndProc(WindowProc);
        var windowClass = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = wndProc,
            hInstance = instance,
            lpszClassName = className,
        };

        if (NativeMethods.RegisterClassEx(ref windowClass) == 0)
        {
            throw new InvalidOperationException(
                $"RegisterClassEx failed: {Marshal.GetLastWin32Error()}.");
        }

        var handle = NativeMethods.CreateWindowEx(
            exStyle: 0,
            className: className,
            windowName: "PIDAL",
            style: 0,
            x: 0,
            y: 0,
            width: 0,
            height: 0,
            parent: IntPtr.Zero,
            menu: IntPtr.Zero,
            instance: instance,
            param: IntPtr.Zero);

        if (handle == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            NativeMethods.UnregisterClass(className, instance);
            throw new InvalidOperationException($"CreateWindowEx failed: {error}.");
        }

        return new HiddenWindow(instance, className, handle, wndProc);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_handle != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_handle);
            _handle = IntPtr.Zero;
        }

        NativeMethods.UnregisterClass(_className, _instance);
    }

    private static IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr GetModuleHandle(string? moduleName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern ushort RegisterClassEx(ref WNDCLASSEX windowClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr CreateWindowEx(
            uint exStyle,
            string className,
            string windowName,
            uint style,
            int x,
            int y,
            int width,
            int height,
            IntPtr parent,
            IntPtr menu,
            IntPtr instance,
            IntPtr param);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterClass(string className, IntPtr instance);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    }
}
