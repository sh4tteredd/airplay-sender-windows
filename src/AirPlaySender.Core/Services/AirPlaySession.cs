using System.Runtime.InteropServices;
using AirPlaySender.Core.Models;

namespace AirPlaySender.Core.Services;

/// <summary>
/// Managed wrapper for the native AirPlayCore.dll (AP1 / RAOP).
/// </summary>
public sealed class AirPlaySession : IDisposable
{
    private const string Dll = "AirPlayCore";

    private IntPtr _handle;
    private bool _disposed;
    private readonly StateCallback _stateCallback;
    private readonly LogCallback _logCallback;

    public AirPlaySession(string senderName, string senderDeviceId)
    {
        SenderName = senderName;
        SenderDeviceId = senderDeviceId;
        _stateCallback = OnNativeState;
        _logCallback = OnNativeLog;
    }

    public string SenderName { get; }
    public string SenderDeviceId { get; }

    public SessionState State { get; private set; } = SessionState.Idle;
    public string? LastError { get; private set; }

    public event EventHandler<SessionStateChangedEventArgs>? StateChanged;
    public event EventHandler<string>? LogReceived;

    public bool IsActive => _handle != IntPtr.Zero && State is not (SessionState.Stopped or SessionState.Error or SessionState.Idle);

    public void Start(ReceiverDevice receiver, string? password)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Stop();
        DestroyHandle();

        var config = new ApConfig
        {
            DeviceName = SenderName,
            DeviceId = SenderDeviceId,
            Model = "Windows",
            SampleRate = 44100,
            Channels = 2
        };

        _handle = ap_create(ref config, _stateCallback, _logCallback, IntPtr.Zero);
        if (_handle == IntPtr.Zero)
            throw new InvalidOperationException("Could not create the AirPlay core.");

        var result = ap_start(_handle, receiver.Host, (ushort)receiver.Port,
            string.IsNullOrWhiteSpace(password) ? null : password);
        if (result != 0)
        {
            var error = GetLastError();
            DestroyHandle();
            throw new InvalidOperationException(error ?? "The AirPlay receiver refused the session.");
        }
    }

    public bool PushPcm(short[] samples, int frames)
    {
        if (_handle == IntPtr.Zero || _disposed || frames <= 0) return false;
        return ap_push_pcm(_handle, samples, (uint)frames) == 1;
    }

    public void SetVolume(double percent)
    {
        if (_handle == IntPtr.Zero || _disposed) return;
        ap_set_volume(_handle, Math.Clamp(percent, 0, 100));
    }

    public void Stop()
    {
        if (_handle == IntPtr.Zero) return;
        ap_stop(_handle);
        State = SessionState.Stopped;
    }

    public uint BufferedFrames => _handle == IntPtr.Zero ? 0 : ap_get_buffer_frames(_handle);
    public ulong DroppedFrames => _handle == IntPtr.Zero ? 0 : ap_get_dropped_frames(_handle);

    private string? GetLastError()
    {
        if (_handle == IntPtr.Zero) return LastError;
        var ptr = ap_last_error(_handle);
        return ptr == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(ptr);
    }

    private void OnNativeState(int state, IntPtr detail, IntPtr user)
    {
        if (_disposed) return;
        var text = detail == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(detail) ?? string.Empty;
        State = Enum.IsDefined(typeof(SessionState), state) ? (SessionState)state : SessionState.Error;
        if (State == SessionState.Error) LastError = text;
        StateChanged?.Invoke(this, new SessionStateChangedEventArgs(State, text));
    }

    private void OnNativeLog(int level, IntPtr message, IntPtr user)
    {
        if (_disposed) return;
        var text = message == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(message) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(text))
            LogReceived?.Invoke(this, text);
    }

    private void DestroyHandle()
    {
        if (_handle == IntPtr.Zero) return;
        ap_destroy(_handle);
        _handle = IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { Stop(); } catch { }
        DestroyHandle();
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void StateCallback(int state, IntPtr detail, IntPtr user);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void LogCallback(int level, IntPtr message, IntPtr user);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct ApConfig
    {
        [MarshalAs(UnmanagedType.LPStr)] public string DeviceName;
        [MarshalAs(UnmanagedType.LPStr)] public string DeviceId;
        [MarshalAs(UnmanagedType.LPStr)] public string Model;
        public int SampleRate;
        public int Channels;
    }

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern IntPtr ap_create(ref ApConfig config, StateCallback stateCallback,
        LogCallback logCallback, IntPtr user);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int ap_start(IntPtr handle,
        [MarshalAs(UnmanagedType.LPStr)] string host, ushort port,
        [MarshalAs(UnmanagedType.LPStr)] string? password);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ap_push_pcm(IntPtr handle, [In] short[] samples, uint frames);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ap_set_volume(IntPtr handle, double percent);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ap_stop(IntPtr handle);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void ap_destroy(IntPtr handle);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ap_get_state(IntPtr handle);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern uint ap_get_buffer_frames(IntPtr handle);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong ap_get_dropped_frames(IntPtr handle);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr ap_last_error(IntPtr handle);
}

public sealed class SessionStateChangedEventArgs : EventArgs
{
    public SessionStateChangedEventArgs(SessionState state, string detail)
    {
        State = state;
        Detail = detail;
    }

    public SessionState State { get; }
    public string Detail { get; }
}
