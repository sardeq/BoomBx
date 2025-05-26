using NAudio.Wave;
using NAudio.Dsp;
using NAudio.Wave.SampleProviders;
using System;

public class PitchShifter : ISampleProvider
{
    private readonly SmbPitchShiftingSampleProvider _shifter;
    
    public PitchShifter(ISampleProvider source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (source.WaveFormat == null) throw new ArgumentException("Source has no WaveFormat", nameof(source));

        _shifter = new SmbPitchShiftingSampleProvider(source.ToStereo())
        {
            PitchFactor = 1.0f
        };
    }

    public WaveFormat WaveFormat => _shifter.WaveFormat;

    public float PitchFactor
    {
        get => _shifter.PitchFactor;
        set => _shifter.PitchFactor = value;
    }

    public void SetPitch(float factor) => PitchFactor = factor;

    public int Read(float[] buffer, int offset, int count) 
        => _shifter.Read(buffer, offset, count);
}