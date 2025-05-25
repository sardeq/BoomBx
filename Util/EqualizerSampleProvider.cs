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
        _source = source;
        UpdateFilters(soundItem);
        WaveFormat = source.WaveFormat;
    }

    public WaveFormat WaveFormat { get; }

    public void UpdateFilters(SoundItem soundItem)
    {
        _bassFilter = BiQuadFilter.LowShelf(
            WaveFormat.SampleRate,
            200f,
            1f,
            (float)soundItem.Bass
        );

        _trebleFilter = BiQuadFilter.HighShelf(
            WaveFormat.SampleRate,
            4000f,
            1f,
            (float)soundItem.Treble
        );
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