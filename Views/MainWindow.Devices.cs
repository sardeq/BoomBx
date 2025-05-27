using Avalonia.Controls;
using Avalonia.Interactivity;
using BoomBx.Services;
using BoomBx.ViewModels;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Threading.Tasks;

namespace BoomBx.Views
{
    public partial class MainWindow : Window
    {
        private DeviceManager _deviceManager;

        private void InitializeVirtualOutput()
        {
            try
            {
                if (_volumeProviderVirtual != null && 
                    _deviceManager.IsUsingVirtualOutput)
                {
                    _deviceManager.AddMixerInput(_volumeProviderVirtual);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"🔇 Virtual output error: {ex.Message}");
            }
        }

                private void InitializeSpeakerOutput()
        {
            try
            {
                if (_volumeProviderSpeaker == null) return;

                using var enumerator = new MMDeviceEnumerator();
                var defaultSpeaker = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                    ?? throw new InvalidOperationException("No output device found");

                _audioWaveOutSpeaker = new WasapiOut(defaultSpeaker, AudioClientShareMode.Shared, true, 100);
                _audioWaveOutSpeaker.PlaybackStopped += HandlePlaybackStopped;
                _audioWaveOutSpeaker.Init(_volumeProviderSpeaker);
                _audioWaveOutSpeaker.Play();
            }
            catch (Exception ex)
            {
                UpdateStatus($"🔈 Speaker error: {ex.Message}");
                StopAudioProcessing(updateStatus: false);
            }
        }

        public void PlaybackDeviceChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is MMDevice device)
            {
                _deviceManager.HandlePlaybackDeviceChanged(device);
            }
        }

        public void CaptureDeviceChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is MMDevice device)
            {
                _deviceManager.HandleCaptureDeviceChanged(device);
            }
        }
    }
}