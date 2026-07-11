using System.Runtime.InteropServices;

namespace FluxChat.Client;

internal sealed class AudioCallSession : IDisposable
{
    private const int WaveMapper = -1;
    private const int CallbackFunction = 0x00030000;
    private const int WimData = 0x3C0;
    private const int WomDone = 0x3BD;
    private const int SampleRate = 16000;
    private const short Channels = 1;
    private const short BitsPerSample = 16;
    private const int BufferMilliseconds = 40;
    private const int InputBufferCount = 4;
    private const int OutputBufferCount = 4;
    private const int InputBufferBytes = SampleRate * Channels * (BitsPerSample / 8) * BufferMilliseconds / 1000;
    private const int MaxQueuedOutputBytes = InputBufferBytes * 8;

    private readonly object _gate = new();
    private readonly WaveInProc _waveInProc;
    private readonly WaveOutProc _waveOutProc;
    private readonly List<InputBuffer> _inputBuffers = [];
    private readonly List<OutputBuffer> _outputBuffers = [];
    private readonly Queue<byte[]> _playbackQueue = new();
    private readonly int _inputDeviceId;
    private readonly int _outputDeviceId;
    private IntPtr _waveIn;
    private IntPtr _waveOut;
    private byte[]? _playbackRemainder;
    private int _playbackRemainderOffset;
    private int _queuedPlaybackBytes;
    private long _failedInputRequeues;
    private long _failedOutputRequeues;
    private bool _isRunning;
    private bool _isDisposed;

    public AudioCallSession(int inputDeviceId = WaveMapper, int outputDeviceId = WaveMapper)
    {
        _inputDeviceId = inputDeviceId;
        _outputDeviceId = outputDeviceId;
        _waveInProc = OnWaveIn;
        _waveOutProc = OnWaveOut;
    }

    public event Action<byte[]>? AudioCaptured;

    public void Start()
    {
        var format = CreateFormat();
        ThrowIfFailed(waveInOpen(out _waveIn, _inputDeviceId, ref format, _waveInProc, IntPtr.Zero, CallbackFunction), "Microphone is not available.");
        ThrowIfFailed(waveOutOpen(out _waveOut, _outputDeviceId, ref format, _waveOutProc, IntPtr.Zero, CallbackFunction), "Speakers are not available.");

        for (var i = 0; i < InputBufferCount; i++)
        {
            var buffer = CreateInputBuffer();
            _inputBuffers.Add(buffer);
            ThrowIfFailed(waveInPrepareHeader(_waveIn, buffer.Header, Marshal.SizeOf<WaveHeader>()), "Could not prepare microphone buffer.");
            ThrowIfFailed(waveInAddBuffer(_waveIn, buffer.Header, Marshal.SizeOf<WaveHeader>()), "Could not queue microphone buffer.");
        }

        for (var i = 0; i < OutputBufferCount; i++)
        {
            var buffer = CreateOutputBuffer();
            _outputBuffers.Add(buffer);
            ThrowIfFailed(waveOutPrepareHeader(_waveOut, buffer.Header, Marshal.SizeOf<WaveHeader>()), "Could not prepare speaker buffer.");
        }

        _isRunning = true;
        foreach (var buffer in _outputBuffers)
        {
            ThrowIfFailed(QueueOutputBuffer(buffer.Header), "Could not queue speaker buffer.");
        }

        ThrowIfFailed(waveInStart(_waveIn), "Could not start microphone capture.");
    }

    public static IReadOnlyList<AudioDeviceInfo> GetInputDevices()
    {
        var devices = new List<AudioDeviceInfo> { AudioDeviceInfo.SystemDefault(WaveMapper) };
        var count = waveInGetNumDevs();
        for (var i = 0; i < count; i++)
        {
            if (waveInGetDevCaps(i, out var caps, Marshal.SizeOf<WaveInCaps>()) == 0)
            {
                devices.Add(new AudioDeviceInfo(i, caps.ProductName));
            }
        }

        return devices;
    }

    public static IReadOnlyList<AudioDeviceInfo> GetOutputDevices()
    {
        var devices = new List<AudioDeviceInfo> { AudioDeviceInfo.SystemDefault(WaveMapper) };
        var count = waveOutGetNumDevs();
        for (var i = 0; i < count; i++)
        {
            if (waveOutGetDevCaps(i, out var caps, Marshal.SizeOf<WaveOutCaps>()) == 0)
            {
                devices.Add(new AudioDeviceInfo(i, caps.ProductName));
            }
        }

        return devices;
    }

    public bool Play(byte[] pcm, out string? error)
    {
        error = null;
        if (pcm.Length == 0 || pcm.Length > InputBufferBytes * 4)
        {
            error = $"Invalid PCM length: {pcm.Length}.";
            return false;
        }

        lock (_gate)
        {
            if (_isDisposed || _waveOut == IntPtr.Zero)
            {
                error = "Audio output is closed.";
                return false;
            }

            while (_queuedPlaybackBytes + pcm.Length > MaxQueuedOutputBytes &&
                   _playbackQueue.TryDequeue(out var dropped))
            {
                _queuedPlaybackBytes -= dropped.Length;
            }

            var copy = new byte[pcm.Length];
            Buffer.BlockCopy(pcm, 0, copy, 0, pcm.Length);
            _playbackQueue.Enqueue(copy);
            _queuedPlaybackBytes += copy.Length;
        }

        return true;
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

    private OutputBuffer CreateOutputBuffer()
    {
        var data = Marshal.AllocHGlobal(InputBufferBytes);
        var header = Marshal.AllocHGlobal(Marshal.SizeOf<WaveHeader>());
        Marshal.StructureToPtr(new WaveHeader
        {
            Data = data,
            BufferLength = InputBufferBytes
        }, header, false);
        return new OutputBuffer(header, data);
    }

    private void OnWaveIn(IntPtr handle, int message, IntPtr instance, IntPtr headerPointer, IntPtr reserved)
    {
        if (message != WimData || headerPointer == IntPtr.Zero)
        {
            return;
        }

        var header = Marshal.PtrToStructure<WaveHeader>(headerPointer);
        byte[]? bytes = null;
        var bytesRecorded = Math.Min(header.BytesRecorded, header.BufferLength);
        if (_isRunning && bytesRecorded > 0)
        {
            bytes = new byte[bytesRecorded];
            Marshal.Copy(header.Data, bytes, 0, bytes.Length);
        }

        ThreadPool.QueueUserWorkItem(static state =>
        {
            var (session, pointer, pcm) = ((AudioCallSession, IntPtr, byte[]?))state!;
            session.RequeueInputBuffer(pointer, pcm);
        }, (this, headerPointer, bytes));
    }

    private void RequeueInputBuffer(IntPtr headerPointer, byte[]? bytes)
    {
        lock (_gate)
        {
            if (_isDisposed || !_isRunning || _waveIn == IntPtr.Zero)
            {
                return;
            }

            var header = Marshal.PtrToStructure<WaveHeader>(headerPointer);
            header.BytesRecorded = 0;
            Marshal.StructureToPtr(header, headerPointer, false);
            var result = waveInAddBuffer(_waveIn, headerPointer, Marshal.SizeOf<WaveHeader>());
            if (result != 0)
            {
                var failures = Interlocked.Increment(ref _failedInputRequeues);
                if (failures == 1 || failures % 25 == 0)
                {
                    AppLog.Write($"Call audio input requeue failed: failures={failures}, winmm={result}");
                }

                return;
            }
        }

        var handler = AudioCaptured;
        if (bytes is not null && handler is not null)
        {
            handler(bytes);
        }
    }

    private void OnWaveOut(IntPtr handle, int message, IntPtr instance, IntPtr headerPointer, IntPtr reserved)
    {
        if (message == WomDone && headerPointer != IntPtr.Zero)
        {
            ThreadPool.QueueUserWorkItem(static state =>
            {
                var (session, pointer) = ((AudioCallSession, IntPtr))state!;
                session.RequeueOutputBuffer(pointer);
            }, (this, headerPointer));
        }
    }

    private void RequeueOutputBuffer(IntPtr headerPointer)
    {
        int result;
        lock (_gate)
        {
            if (_isDisposed || !_isRunning || _waveOut == IntPtr.Zero)
            {
                return;
            }

            result = QueueOutputBuffer(headerPointer);
        }

        if (result != 0)
        {
            var failures = Interlocked.Increment(ref _failedOutputRequeues);
            if (failures == 1 || failures % 25 == 0)
            {
                AppLog.Write($"Call audio output requeue failed: failures={failures}, winmm={result}");
            }
        }
    }

    private int QueueOutputBuffer(IntPtr headerPointer)
    {
        var header = Marshal.PtrToStructure<WaveHeader>(headerPointer);
        var output = new byte[header.BufferLength];
        var offset = 0;
        while (offset < output.Length)
        {
            if (_playbackRemainder is null || _playbackRemainderOffset >= _playbackRemainder.Length)
            {
                if (!_playbackQueue.TryDequeue(out _playbackRemainder))
                {
                    _playbackRemainder = null;
                    _playbackRemainderOffset = 0;
                    break;
                }

                _queuedPlaybackBytes -= _playbackRemainder.Length;
                _playbackRemainderOffset = 0;
            }

            var available = _playbackRemainder.Length - _playbackRemainderOffset;
            var toCopy = Math.Min(available, output.Length - offset);
            Buffer.BlockCopy(_playbackRemainder, _playbackRemainderOffset, output, offset, toCopy);
            _playbackRemainderOffset += toCopy;
            offset += toCopy;

            if (_playbackRemainderOffset >= _playbackRemainder.Length)
            {
                _playbackRemainder = null;
                _playbackRemainderOffset = 0;
            }
        }

        Marshal.Copy(output, 0, header.Data, output.Length);
        header.BytesRecorded = 0;
        header.Loops = 0;
        Marshal.StructureToPtr(header, headerPointer, false);
        return waveOutWrite(_waveOut, headerPointer, Marshal.SizeOf<WaveHeader>());
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
            foreach (var buffer in _outputBuffers)
            {
                _ = waveOutUnprepareHeader(_waveOut, buffer.Header, Marshal.SizeOf<WaveHeader>());
                Marshal.FreeHGlobal(buffer.Data);
                Marshal.FreeHGlobal(buffer.Header);
            }

            _outputBuffers.Clear();
            _ = waveOutClose(_waveOut);
            _waveOut = IntPtr.Zero;
        }

        _playbackQueue.Clear();
        _playbackRemainder = null;
        _playbackRemainderOffset = 0;
        _queuedPlaybackBytes = 0;
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
    private sealed record OutputBuffer(IntPtr Header, IntPtr Data);

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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WaveInCaps
    {
        public ushort ManufacturerId;
        public ushort ProductId;
        public uint DriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ProductName;
        public uint Formats;
        public ushort Channels;
        public ushort Reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WaveOutCaps
    {
        public ushort ManufacturerId;
        public ushort ProductId;
        public uint DriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ProductName;
        public uint Formats;
        public ushort Channels;
        public ushort Reserved;
        public uint Support;
    }

    [DllImport("winmm.dll")]
    private static extern int waveInOpen(out IntPtr waveIn, int deviceId, ref WaveFormat format, WaveInProc callback, IntPtr instance, int flags);

    [DllImport("winmm.dll")]
    private static extern int waveInGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int waveInGetDevCaps(int deviceId, out WaveInCaps caps, int size);

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
    private static extern int waveOutGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int waveOutGetDevCaps(int deviceId, out WaveOutCaps caps, int size);

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

internal sealed record AudioDeviceInfo(int Id, string Name)
{
    public static AudioDeviceInfo SystemDefault(int id)
        => new(id, "Default Windows device");

    public override string ToString()
        => Name;
}
