using System;
using BoomBx.Models;
using NAudio.Dsp;
using NAudio.Wave;

public class EqualizerSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private BiQuadFilter? _bassFilter;
    private BiQuadFilter? _trebleFilter;
    

    public EqualizerSampleProvider(ISampleProvider source, SoundItem soundItem)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (soundItem == null) throw new ArgumentNullException(nameof(soundItem));
        if (source.WaveFormat == null) throw new ArgumentException("Source has no WaveFormat", nameof(source));

        _source = source;
        WaveFormat = source.WaveFormat;
        UpdateFilters(soundItem);
    }

    public WaveFormat WaveFormat { get; }

    public void UpdateFilters(SoundItem soundItem)
    {
        if (WaveFormat == null) throw new InvalidOperationException("WaveFormat is null");
        _bassFilter = BiQuadFilter.LowShelf(WaveFormat.SampleRate, 200f, 1f, (float)soundItem.Bass);
        _trebleFilter = BiQuadFilter.HighShelf(WaveFormat.SampleRate, 4000f, 1f, (float)soundItem.Treble);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int samplesRead = _source.Read(buffer, offset, count);
        
        for (int i = 0; i < samplesRead; i++)
        {
            float sample = buffer[offset + i];
            sample = _bassFilter?.Transform(sample) ?? sample;
            sample = _trebleFilter?.Transform(sample) ?? sample;
            buffer[offset + i] = sample;
        }
        return samplesRead;
    }
}