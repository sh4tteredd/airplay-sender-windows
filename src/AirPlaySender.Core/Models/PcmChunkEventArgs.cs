namespace AirPlaySender.Core.Models;

public sealed class PcmChunkEventArgs : EventArgs
{
    public PcmChunkEventArgs(short[] samples, int frames)
    {
        Samples = samples;
        Frames = frames;
    }

    /// <summary>Interleaved stereo signed 16-bit samples.</summary>
    public short[] Samples { get; }

    public int Frames { get; }
}
