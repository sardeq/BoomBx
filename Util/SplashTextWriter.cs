using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Threading;
using BoomBx;
using BoomBx.Views;

public class SplashTextWriter : TextWriter
{
    private readonly App? _app;
    
    public SplashTextWriter()
    {
        _app = Application.Current as App;
    }

    public override void WriteLine(string? value)
    {
        if (_app?._splash != null && !_app._splash.IsClosed)
        {
            Dispatcher.UIThread.Post(() => 
                _app._splash.UpdateStatus(value ?? string.Empty));
        }
    }

    public override Encoding Encoding => Encoding.UTF8;
}