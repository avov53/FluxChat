using System.Runtime.InteropServices;

namespace FluxChat.Client;

internal sealed class AudioCallSession : IDisposable
{
    private const int WaveMapper = -1;
    private const int CallbackFunction = 0x00030000;
    private const int WimData = 0x3C0;
    private const int WomDone = 0x3BD;
    private const int SampleRate = 8000;
    private const short Channels = 1;
    private const short BitsPerSample = 16;
    private const int BufferMilliseconds = 40;
    private const int InputBufferCount = 4;
    private const int InputBufferBytes = SampleRate * Channels * (BitsPerSample / 8) * BufferMilliseconds / 1000;

    private readonly object _gate = new();
    private readonly WaveInProc _waveInProc;
    private readonly WaveOutProc _waveOutProc;
    private readonly List<InputBuffer> _inputBuffers = [];
    private IntPtr _waveIn;
    private IntPtr _waveOut;
    private bool _isRunning;
    private bool _isDisposed;

    public AudioCallSession()
    {
        _waveInProc = OnWaveIn;
        _waveOutProc = OnWaveOut;
    }

    public event Action<byte[]>? AudioCaptured;

    public void Start()
    {
        var format = CreateFormat();
        ThrowIfFailed(waveInOpen(out _waveIn, WaveMapper, ref format, _waveInProc, IntPtr.Zero, CallbackFunction), "Microphone is not available.");
        ThrowIfFailed(waveOutOpen(out _waveOut, WaveMapper, ref format, _waveOutProc, IntPtr.Zero, CallbackFunction), "Speakers are not available.");

        for (var i = 0; i < InputBufferCount; i++)
        {
            var buffer = CreateInputBuffer();
            _inputBuffers.Add(buffer);
            ThrowIfFailed(waveInPrepareHeader(_waveIn, buffer.Header, Marshal.SizeOf<WaveHeader>()), "Could not prepare microphone buffer.");
            ThrowIfFailed(waveInAddBuffer(_waveIn, buffer.Header, Marshal.SizeOf<WaveHeader>()), "Could not queue microphone buffer.");
        }

        _isRunning = true;
        ThrowIfFailed(waveInStart(_waveIn), "Could not start microphone capture.");
    }

    public void Play(byte[] pcm)
    {
        if (pcm.Length == 0 || pcm.Length > InputBufferBytes * 4)
        {
            return;
        }

        lock (_gate)
        {
            if (_isDisposed || _waveOut == IntPtr.Zero)
            {
                return;
            }
        }

        var data = Marshal.AllocHGlobal(pcm.Length);
        var header = Marshal.AllocHGlobal(Marshal.SizeOf<WaveHeader>());
        Marshal.Copy(pcm, 0, data, pcm.Length);
        Marshal.StructureToPtr(new WaveHeader
        {
            Data = data,
            BufferLength = pcm.Length
        }, header, false);

        if (waveOutPrepareHeader(_waveOut, header, Marshal.SizeOf<WaveHeader>()) != 0 ||
            waveOutWrite(_waveOut, header, Marshal.SizeOf<WaveHeader>()) != 0)
        {
            FreeOutputBuffer(header);
        }
    }

    private InputBuffer CreateInputBuffer()
    {
        var data = Marshal.AllocHGlobal(InputBufferBytes);
        var header = Marshal.AllocHGlobal(Marshal.SizeOf<WaveHeader>());
        Marshal.StructureToPtr(new WaveHeader
        {
            Data = data,
            BufferLength = InputBufferBytes
        }, header, false);
        return new InputBuffer(header, data);
    }

    private void OnWaveIn(IntPtr handle, int message, IntPtr instance, IntPtr headerPointer, IntPtr reserved)
    {
        if (message != WimData || headerPointer == IntPtr.Zero)
        {
            return;
        }

        var header = Marshal.PtrToStructure<WaveHeader>(headerPointer);
        if (_isRunning && header.BytesRecorded > 0)
        {
            var bytes = new byte[header.BytesRecorded];
            Marshal.Copy(header.Data, bytes, 0, bytes.Length);
            AudioCaptured?.Invoke(bytes);
        }

        lock (_gate)
        {
            if (_isDisposed || !_isRunning || _waveIn == IntPtr.Zero)
            {
                return;
            }

            header.BytesRecorded = 0;
            Marshal.StructureToPtr(header, headerPointer, false);
            _ = waveInAddBuffer(_waveIn, headerPointer, Marshal.SizeOf<WaveHeader>());
        }
    }

    private void OnWaveOut(IntPtr handle, int message, IntPtr instance, IntPtr headerPointer, IntPtr reserved)
    {
        if (message == WomDone && headerPointer != IntPtr.Zero)
        {
            FreeOutputBuffer(headerPointer);
        }
    }

    private void FreeOutputBuffer(IntPtr headerPointer)
    {
        var header = Marshal.PtrToStructure<WaveHeader>(headerPointer);
        if (_waveOut != IntPtr.Zero)
        {
            _ = waveOutUnprepareHeader(_waveOut, headerPointer, Marshal.SizeOf<WaveHeader>());
        }

        if (header.Data != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(header.Data);
        }

        Marshal.FreeHGlobal(headerPointer);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _isRunning = false;
        }

        if (_waveIn != IntPtr.Zero)
        {
            _ = waveInStop(_waveIn);
            _ = waveInReset(_waveIn);
            foreach (var buffer in _inputBuffers)
            {
                _ = waveInUnprepareHeader(_waveIn, buffer.Header, Marshal.SizeOf<WaveHeader>());
                Marshal.FreeHGlobal(buffer.Data);
                Marshal.FreeHGlobal(buffer.Header);
            }

            _inputBuffers.Clear();
            _ = waveInClose(_waveIn);
            _waveIn = IntPtr.Zero;
        }

        if (_waveOut != IntPtr.Zero)
        {
            _ = waveOutReset(_waveOut);
            _ = waveOutClose(_waveOut);
            _waveOut = IntPtr.Zero;
        }
    }

    private static WaveFormat CreateFormat()
        => new()
        {
            FormatTag = 1,
            Channels = Channels,
            SamplesPerSec = SampleRate,
            AvgBytesPerSec = SampleRate * Channels * (BitsPerSample / 8),
            BlockAlign = Channels * (BitsPerSample / 8),
            BitsPerSample = BitsPerSample
        };

    private static void ThrowIfFailed(int result, string message)
    {
        if (result != 0)
        {
            throw new InvalidOperationException($"{message} winmm={result}");
        }
    }

    private sealed record InputBuffer(IntPtr Header, IntPtr Data);

    private delegate void WaveInProc(IntPtr handle, int message, IntPtr instance, IntPtr parameter1, IntPtr parameter2);
    private delegate void WaveOutProc(IntPtr handle, int message, IntPtr instance, IntPtr parameter1, IntPtr parameter2);

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormat
    {
        public short FormatTag;
        public short Channels;
        public int SamplesPerSec;
        public int AvgBytesPerSec;
        public short BlockAlign;
        public short BitsPerSample;
        public short Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public IntPtr Data;
        public int BufferLength;
        public int BytesRecorded;
        public IntPtr User;
        public int Flags;
        public int Loops;
        public IntPtr Next;
        public IntPtr Reserved;
    }

    [DllImport("winmm.dll")]
    private static extern int waveInOpen(out IntPtr waveIn, int deviceId, ref WaveFormat format, WaveInProc callback, IntPtr instance, int flags);

    [DllImport("winmm.dll")]
    private static extern int waveInPrepareHeader(IntPtr waveIn, IntPtr header, int headerSize);

    [DllImport("winmm.dll")]
    private static extern int waveInAddBuffer(IntPtr waveIn, IntPtr header, int headerSize);

    [DllImport("winmm.dll")]
    private static extern int waveInStart(IntPtr waveIn);

    [DllImport("winmm.dll")]
    private static extern int waveInStop(IntPtr waveIn);

    [DllImport("winmm.dll")]
    private static extern int waveInReset(IntPtr waveIn);

    [DllImport("winmm.dll")]
    private static extern int waveInUnprepareHeader(IntPtr waveIn, IntPtr header, int headerSize);

    [DllImport("winmm.dll")]
    private static extern int waveInClose(IntPtr waveIn);

    [DllImport("winmm.dll")]
    private static extern int waveOutOpen(out IntPtr waveOut, int deviceId, ref WaveFormat format, WaveOutProc callback, IntPtr instance, int flags);

    [DllImport("winmm.dll")]
    private static extern int waveOutPrepareHeader(IntPtr waveOut, IntPtr header, int headerSize);

    [DllImport("winmm.dll")]
    private static extern int waveOutWrite(IntPtr waveOut, IntPtr header, int headerSize);

    [DllImport("winmm.dll")]
    private static extern int waveOutUnprepareHeader(IntPtr waveOut, IntPtr header, int headerSize);

    [DllImport("winmm.dll")]
    private static extern int waveOutReset(IntPtr waveOut);

    [DllImport("winmm.dll")]
    private static extern int waveOutClose(IntPtr waveOut);
}
