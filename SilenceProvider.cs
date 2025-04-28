using System;
using NAudio.Wave;

public class SilenceProvider : ISampleProvider
{
    private readonly WaveFormat _waveFormat;

    public SilenceProvider(WaveFormat waveFormat)
    {
        _waveFormat = waveFormat;
    }

    public WaveFormat WaveFormat => _waveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        Array.Clear(buffer, offset, count);
        return count;
    }
}