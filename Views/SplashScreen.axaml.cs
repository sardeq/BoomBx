using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NAudio.Wave;
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace BoomBx.Views
{
    public partial class SplashScreen : Window
    {
        private readonly WaveOutEvent _soundPlayer = new();
        private WaveStream? _audioFile;
        public bool IsClosed { get; private set; }

        private TaskCompletionSource<bool> _playbackCompleted = new();


        public SplashScreen()
        {
            InitializeComponent();
            PlayStartupSound();
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.CanResize = false;
            this.SystemDecorations = SystemDecorations.None;
        }

        private void PlayStartupSound()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var stream = assembly.GetManifestResourceStream("BoomBx.Assets.Sounds.vineboom.mp3");
                
                if (stream != null)
                {
                    _audioFile = new Mp3FileReader(stream);
                    _soundPlayer.Init(_audioFile);
                    _soundPlayer.PlaybackStopped += (sender, e) => 
                    {
                        _playbackCompleted.TrySetResult(true);
                    };
                    _soundPlayer.Play();
                }
                else
                {
                    _playbackCompleted.TrySetResult(true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error playing startup sound: {ex}");
                _playbackCompleted.TrySetResult(true);
            }
        }

        public void DisposeSound()
        {
            try
            {
                _soundPlayer?.Stop();
                _audioFile?.Dispose();
                _soundPlayer?.Dispose();
            }
            catch { /* ignore */ }
        }

        public async Task CloseSplashAsync()
        {
            try
            {
                if (IsClosed) return;
                
                await Task.WhenAny(
                    _playbackCompleted.Task,
                    Task.Delay(TimeSpan.FromSeconds(5)) 
                );
                
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!IsClosed)
                    {
                        Close();
                        IsClosed = true;
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error closing splash: {ex}");
            }
            finally
            {
                DisposeSound();
            }
        }

        public void UpdateStatus(string? message)
        {
            Dispatcher.UIThread.Post(() =>
            {
                StatusText.Text = message ?? string.Empty;
            });
        }

        public TimeSpan GetTotalTime()
        {
            try
            {
                return _audioFile?.TotalTime ?? TimeSpan.Zero;
            }
            catch
            {
                return TimeSpan.Zero;
            }
        }
    }
}