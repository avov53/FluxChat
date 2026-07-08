using System.Runtime.InteropServices;

namespace FluxChat.Client;

internal static class IdleDetector
{
    public static TimeSpan GetIdleTime()
    {
        var lastInput = new LastInputInfo
        {
            Size = (uint)Marshal.SizeOf<LastInputInfo>()
        };

        if (!GetLastInputInfo(ref lastInput))
        {
            return TimeSpan.Zero;
        }

        var idleMilliseconds = Environment.TickCount64 - lastInput.Time;
        return idleMilliseconds <= 0
            ? TimeSpan.Zero
            : TimeSpan.FromMilliseconds(idleMilliseconds);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo lastInputInfo);

    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }
}
