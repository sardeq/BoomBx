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
        private DateTime _startTime = DateTime.UtcNow;

        public SplashScreen()
        {
            InitializeComponent();
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.CanResize = false;
            this.SystemDecorations = SystemDecorations.None;

            this.Opened += async (s, e) =>
            {
                await Task.Delay(50);
                PlayStartupSound();
            };
        }

        private void PlayStartupSound()
        {
            try
            {
                Console.WriteLine("Attempting to play startup sound...");
                var assembly = Assembly.GetExecutingAssembly();
                var stream = assembly.GetManifestResourceStream("BoomBx.Assets.Sounds.vineboom.mp3");

                if (stream == null)
                {
                    Console.WriteLine("Sound resource not found.");
                    _playbackCompleted.TrySetResult(true);
                    return;
                }

                Console.WriteLine("Sound stream found. Initializing audio...");
                _audioFile = new Mp3FileReader(stream);
                _soundPlayer.Init(_audioFile);

                _soundPlayer.PlaybackStopped += (sender, e) =>
                {
                    Console.WriteLine($"Playback stopped. Exception: {e.Exception?.Message ?? "None"}");
                    _playbackCompleted.TrySetResult(true);
                };

                Console.WriteLine("Starting playback...");
                _soundPlayer.Play();
                Console.WriteLine("Playback started successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error playing sound: {ex}");
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
                
                var timeout = Task.Delay(TimeSpan.FromSeconds(5));
                await Task.WhenAny(_playbackCompleted.Task, timeout);

                var elapsed = DateTime.UtcNow - _startTime;
                if (elapsed < TimeSpan.FromSeconds(3))
                {
                    await Task.Delay(TimeSpan.FromSeconds(3) - elapsed);
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!IsClosed)
                    {
                        Close();
                        IsClosed = true;
                    }
                });
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