using System;
using NAudio.Wave;

public class SilentSampleProvider : ISampleProvider
{
    private readonly WaveFormat waveFormat;
    
    public SilentSampleProvider(int sampleRate = 44100, int channels = 2)
    {
        waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        Array.Clear(buffer, offset, count);
        return count;
    }

    public WaveFormat WaveFormat => waveFormat;
}