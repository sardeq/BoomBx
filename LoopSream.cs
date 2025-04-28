using NAudio.Wave;

public class LoopStream : WaveStream
{
    private readonly WaveStream _sourceStream;
    private bool _loop = true;
    private long _position;

    public bool Loop
    {
        get => _loop;
        set => _loop = value;
    }

    public LoopStream(WaveStream sourceStream)
    {
        _sourceStream = sourceStream;
    }

    public override WaveFormat WaveFormat => _sourceStream.WaveFormat;
    
    public override long Length => _sourceStream.Length;

    public override long Position
    {
        get => _position;
        set => _sourceStream.Position = value % _sourceStream.Length;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = _sourceStream.Read(buffer, offset + totalRead, count - totalRead);
            if (read == 0)
            {
                if (!_loop) break;
                _sourceStream.Position = 0;
            }
            totalRead += read;
        }
        _position += totalRead;
        return totalRead;
    }

    protected override void Dispose(bool disposing)
    {
        _sourceStream.Dispose();
        base.Dispose(disposing);
    }
}