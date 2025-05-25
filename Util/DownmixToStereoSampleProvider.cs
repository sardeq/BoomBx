using System;
using NAudio.Wave;

public class DownmixToStereoSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _sourceChannels;

    public DownmixToStereoSampleProvider(ISampleProvider source)
    {
        _source = source;
        _sourceChannels = source.WaveFormat.Channels;
        if (_sourceChannels < 2)
            throw new ArgumentException("Source must have at least 2 channels for downmixing");
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        int samplesRead = _source.Read(buffer, offset, count * _sourceChannels / 2);
        int numFrames = samplesRead / _sourceChannels;

        for (int i = 0; i < numFrames; i++)
        {
            float left = 0;
            float right = 0;
            for (int ch = 0; ch < _sourceChannels; ch++)
            {
                float sample = buffer[offset + i * _sourceChannels + ch];
                if (ch == 0) left = sample;        // First channel to left
                else if (ch == 1) right = sample;  // Second channel to right
                else
                {
                    // Additional channels split evenly between left and right
                    left += sample * 0.5f;
                    right += sample * 0.5f;
                }
            }
            buffer[offset + i * 2] = left;      // Left channel
            buffer[offset + i * 2 + 1] = right; // Right channel
        }
        return numFrames * 2;
    }
}