using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FluxChat.Client;

internal static class TaskbarAttention
{
    private const uint FlashStop = 0;
    private const uint FlashTray = 2;
    private const uint FlashUntilForeground = 12;

    public static void Flash(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var info = CreateInfo(handle, FlashTray | FlashUntilForeground);
        _ = FlashWindowEx(ref info);
    }

    public static void Stop(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var info = CreateInfo(handle, FlashStop);
        _ = FlashWindowEx(ref info);
    }

    private static FlashInfo CreateInfo(IntPtr handle, uint flags)
        => new()
        {
            Size = (uint)Marshal.SizeOf<FlashInfo>(),
            WindowHandle = handle,
            Flags = flags,
            Count = uint.MaxValue,
            Timeout = 0
        };

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public IntPtr WindowHandle;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }
}
