using System.Diagnostics;
using AirPlaySender.Core.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AirPlaySender.Core.Services;

/// <summary>
/// Captures a Windows endpoint and produces interleaved stereo s16 at 44.1 kHz.
/// Render endpoints use WASAPI loopback; capture endpoints use normal capture.
/// </summary>
public sealed class AudioCaptureService : IDisposable
{
    private WasapiCapture? _capture;
    private BufferedWaveProvider? _buffer;
    private SampleToWaveProvider16? _pcm16;
    private Thread? _pump;
    private volatile bool _running;
    private volatile bool _stopping;
    private readonly int _targetRate;

    public AudioCaptureService(int targetRate = 44100)
    {
        _targetRate = targetRate;
    }

    public event EventHandler<PcmChunkEventArgs>? PcmAvailable;
    public event EventHandler<string>? CaptureError;

    public string? CurrentFormat { get; private set; }

    public void Start(AudioDevice device)
    {
        Stop();
        _stopping = false;

        _capture = device.Kind == AudioDeviceKind.Render
            ? new WasapiLoopbackCapture(device.Device)
            : new WasapiCapture(device.Device);

        CurrentFormat = _capture.WaveFormat.ToString();
        _buffer = new BufferedWaveProvider(_capture.WaveFormat)
        {
            BufferDuration = TimeSpan.FromSeconds(2),
            DiscardOnBufferOverflow = true
        };

        ISampleProvider samples = _buffer.ToSampleProvider();
        if (samples.WaveFormat.Channels == 1)
        {
            samples = new MonoToStereoSampleProvider(samples);
        }
        else if (samples.WaveFormat.Channels != 2)
        {
            throw new NotSupportedException(
                $"The selected endpoint has {samples.WaveFormat.Channels} channels. " +
                "Only mono and stereo endpoints are supported in this build.");
        }

        if (samples.WaveFormat.SampleRate != _targetRate)
        {
            samples = new WdlResamplingSampleProvider(samples, _targetRate);
        }

        _pcm16 = new SampleToWaveProvider16(samples);
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
        _capture.StartRecording();

        _running = true;
        _pump = new Thread(Pump)
        {
            IsBackground = true,
            Name = "AirPlay audio pump",
            Priority = ThreadPriority.AboveNormal
        };
        _pump.Start();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0) return;
        try
        {
            _buffer?.AddSamples(e.Buffer, 0, e.BytesRecorded);
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("Audio buffer write failed", ex);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (_stopping) return;
        if (e.Exception is not null)
        {
            Logger.Instance.Error("Audio capture stopped", e.Exception);
            CaptureError?.Invoke(this, e.Exception.Message);
        }
    }

    private void Pump()
    {
        var bytes = new byte[_targetRate / 100 * 2 * sizeof(short)]; // 10 ms stereo s16
        var period = Stopwatch.Frequency / 100; // 10 ms
        var next = Stopwatch.GetTimestamp();

        while (_running)
        {
            var pcm = _pcm16;
            if (pcm is null) break;
            var read = pcm.Read(bytes, 0, bytes.Length);
            if (read > 0)
            {
                if ((read & 1) != 0) read--;
                var samples = new short[read / sizeof(short)];
                Buffer.BlockCopy(bytes, 0, samples, 0, read);
                try
                {
                    PcmAvailable?.Invoke(this, new PcmChunkEventArgs(samples, samples.Length / 2));
                }
                catch (Exception ex)
                {
                    Logger.Instance.Error("PCM consumer failed", ex);
                }
            }

            // BufferedWaveProvider returns silence when starved. Without this
            // pacing the pump would generate samples as fast as the CPU can
            // read, overflowing the native ring. Keep the producer at
            // real-time; catch up gently if we fall behind.
            next += period;
            var now = Stopwatch.GetTimestamp();
            if (next > now)
            {
                var waitMs = (int)((next - now) * 1000 / Stopwatch.Frequency);
                if (waitMs > 0) Thread.Sleep(waitMs);
            }
            else if (now - next > period * 5)
            {
                next = now;
            }
        }
    }

    public void Stop()
    {
        _running = false;
        _stopping = true;
        try { _capture?.StopRecording(); } catch { }
        try { _pump?.Join(1500); } catch { }
        _pump = null;

        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            try { _capture.Dispose(); } catch { }
            _capture = null;
        }

        _buffer = null;
        _pcm16 = null;
        CurrentFormat = null;
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
