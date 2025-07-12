using Avalonia.Threading;
using BoomBx.Models;
using BoomBx.ViewModels;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BoomBx.Services
{
    public class AudioService : IDisposable
    {
        private bool _isRestarting;
        private readonly object _restartLock = new();

        private readonly AppSettings _settings;
        private readonly MainWindowViewModel _viewModel;
        private readonly DeviceManager _deviceManager;

        private WasapiCapture? _micCapture;
        private IWavePlayer? _persistentOutput;
        private MixingSampleProvider? _persistentMixer;
                
        public bool IsUsingVirtualOutput => 
        _deviceManager.SelectedPlaybackDevice?.FriendlyName?.Contains("CABLE Input") == true;

        public AudioService(MainWindowViewModel viewModel, AppSettings settings, DeviceManager deviceManager)
        {
            if (viewModel == null) throw new ArgumentNullException(nameof(viewModel));
            _viewModel = viewModel;
            _settings = settings;
            _deviceManager = deviceManager;
        }

        public void InitializeDevices()
        {
            StartPersistentAudioRouting();
        }

        public void HandlePlaybackDeviceChanged(MMDevice? device)
        {
            _deviceManager.SetPlaybackDevice(device);
            RestartPersistentAudioRouting();
        }

        private void HandleCaptureError(object? sender, StoppedEventArgs e)
        {
            if (e.Exception == null) return;
            
            Console.WriteLine($"Capture error: {e.Exception.Message}");
            ScheduleAudioRestart();
        }

        private void HandleOutputError(object? sender, StoppedEventArgs e)
        {
            if (e.Exception == null) return;
            
            Console.WriteLine($"Output error: {e.Exception.Message}");
            ScheduleAudioRestart();
        }

        private void ScheduleAudioRestart()
        {
            lock (_restartLock)
            {
                if (_isRestarting) return;
                _isRestarting = true;
            }

            // Restart on UI thread after delay
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    await Task.Delay(1000);
                    Console.WriteLine("Attempting audio restart...");
                    RestartPersistentAudioRouting();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Restart failed: {ex.Message}");
                }
                finally
                {
                    lock (_restartLock) _isRestarting = false;
                }
            });
        }


        public void HandleCaptureDeviceChanged(MMDevice? device)
        {
            _deviceManager.SetCaptureDevice(device);
            RestartPersistentAudioRouting();
        }

        public void RestartPersistentAudioRouting()
        {
            StopPersistentAudioRouting();
            StartPersistentAudioRouting();
        }

        private void StartPersistentAudioRouting()
        {
            if (_deviceManager.SelectedPlaybackDevice == null || _deviceManager.SelectedCaptureDevice == null)
                return;

            try
            {
                StopPersistentAudioRouting();

                var targetFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
                _persistentMixer = new MixingSampleProvider(targetFormat)
                {
                    ReadFully = true
                };
                _deviceManager.SetPersistentMixer(_persistentMixer);

                _micCapture = new WasapiCapture(_deviceManager.SelectedCaptureDevice);
                _micCapture.RecordingStopped += HandleCaptureError;
                var micProvider = new WaveInProvider(_micCapture);
                var micSampleProvider = ConvertFormat(micProvider.ToSampleProvider(), targetFormat);
                _persistentMixer.AddMixerInput(micSampleProvider);

                _persistentOutput = new WasapiOut(
                    _deviceManager.SelectedPlaybackDevice,
                    AudioClientShareMode.Shared,
                    false,
                    100
                );
                _persistentOutput.PlaybackStopped += HandleOutputError;

                _persistentOutput.Init(_persistentMixer);
                _micCapture.StartRecording();
                _persistentOutput.Play();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting persistent routing: {ex}");
            }
        }

        private void StopPersistentAudioRouting()
        {
            if (_micCapture != null)
            {
                _micCapture.RecordingStopped -= HandleCaptureError;
                _micCapture.StopRecording();
                _micCapture.Dispose();
                _micCapture = null;
            }

            if (_persistentOutput != null)
            {
                _persistentOutput.PlaybackStopped -= HandleOutputError;
                _persistentOutput.Stop();
                _persistentOutput.Dispose();
                _persistentOutput = null;
            }

            _persistentMixer?.RemoveAllMixerInputs();
        }


        public static ISampleProvider ConvertFormat(ISampleProvider input, WaveFormat targetFormat)
        {
            if (input.WaveFormat.Channels != targetFormat.Channels)
            {
                if (targetFormat.Channels == 2)
                {
                    input = input.WaveFormat.Channels switch
                    {
                        1 => new MonoToStereoSampleProvider(input),
                        > 2 => new DownmixToStereoSampleProvider(input),
                        _ => input
                    };
                }
                else if (targetFormat.Channels == 1)
                {
                    input = input.WaveFormat.Channels switch
                    {
                        2 => new StereoToMonoSampleProvider(input),
                        > 2 => throw new NotSupportedException(
                            "Downmixing to mono from multi-channel not implemented"),
                        _ => input
                    };
                }
            }

            if (input.WaveFormat.SampleRate != targetFormat.SampleRate)
            {
                input = new WdlResamplingSampleProvider(input, targetFormat.SampleRate);
            }

            return input;
        }

        public void Dispose()
        {
            StopPersistentAudioRouting();
            _persistentOutput?.Dispose();
            _micCapture?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}