using NAudio.Wave;
using NAudio.Wave.SampleProviders;

public class LoopingSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;

    public LoopingSampleProvider(ISampleProvider source)
    {
        _source = source;
        WaveFormat = source.WaveFormat;
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = _source.Read(buffer, offset + totalRead, count - totalRead);
            if (read == 0)
            {
                if (_source is AudioFileReader audioFileReader)
                {
                    audioFileReader.Position = 0;
                }
                else
                {
                    break; 
                }
            }
            totalRead += read;
        }
        return totalRead;
    }
}
