using Avalonia.Threading;
using BoomBx.Models;
using BoomBx.ViewModels;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace BoomBx.Services
{
    public class DeviceManager
    {
        private readonly MainWindowViewModel _viewModel;
        private readonly AppSettings _settings;
        private readonly Dispatcher _dispatcher;
        
        private MMDevice? _selectedPlaybackDevice;
        private MMDevice? _selectedCaptureDevice;
        private MixingSampleProvider? _persistentMixer;

        public bool IsUsingVirtualOutput =>
            _selectedPlaybackDevice?.FriendlyName?.Contains("CABLE Input") == true;

        public MMDevice? SelectedPlaybackDevice => _selectedPlaybackDevice;
        public MMDevice? SelectedCaptureDevice => _selectedCaptureDevice;

        public DeviceManager(
            MainWindowViewModel viewModel,
            AppSettings settings,
            Dispatcher dispatcher)
        {
            _viewModel = viewModel;
            _settings = settings;
            _dispatcher = dispatcher;
        }

        
        public void SetPersistentMixer(MixingSampleProvider? mixer)
        {
            _persistentMixer = mixer;
        }

        public async Task InitializeAsync()
        {
            await EnumerateDevices();
            LoadDeviceSettings();

            await _dispatcher.InvokeAsync(() =>
            {
                _viewModel.SelectedPlaybackDevice = _selectedPlaybackDevice;
                _viewModel.SelectedCaptureDevice = _selectedCaptureDevice;
            });
        }

        public async Task EnumerateDevices()
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();

                var playback = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                    .OrderBy(d => d.FriendlyName)
                    .ToList();

                var capture = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                    .OrderBy(d => d.FriendlyName)
                    .ToList();

                await _dispatcher.InvokeAsync(() =>
                {
                    _viewModel.PlaybackDevices.Clear();
                    foreach (var device in playback)
                    {
                        _viewModel.PlaybackDevices.Add(device);
                    }

                    _viewModel.CaptureDevices.Clear();
                    foreach (var device in capture)
                    {
                        _viewModel.CaptureDevices.Add(device);
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Device enumeration error: {ex}");
            }
        }

        private void LoadDeviceSettings()
        {
            if (!string.IsNullOrEmpty(_settings.LastPlaybackDevice))
            {
                _selectedPlaybackDevice = _viewModel.PlaybackDevices
                    .FirstOrDefault(d => d.FriendlyName == _settings.LastPlaybackDevice);
            }

            if (!string.IsNullOrEmpty(_settings.LastCaptureDevice))
            {
                _selectedCaptureDevice = _viewModel.CaptureDevices
                    .FirstOrDefault(d => d.FriendlyName == _settings.LastCaptureDevice);
            }

            SelectDefaultDevices();
        }

        public void SaveDeviceSettings()
        {
            _settings.LastPlaybackDevice = _selectedPlaybackDevice?.FriendlyName;
            _settings.LastCaptureDevice = _selectedCaptureDevice?.FriendlyName;
            SettingsManager.SaveSettings(_settings);
        }

        public void SetPlaybackDevice(MMDevice? device)
        {
            _selectedPlaybackDevice = device;
            _viewModel.SelectedPlaybackDevice = device;
            _settings.LastPlaybackDevice = device?.FriendlyName;
            SaveDeviceSettings();
        }

        public void SetCaptureDevice(MMDevice? device)
        {
            _selectedCaptureDevice = device;
            _viewModel.SelectedCaptureDevice = device;
            _settings.LastCaptureDevice = device?.FriendlyName;
            SaveDeviceSettings();
        }
        
        public void AddMixerInput(ISampleProvider input)
        {
            _dispatcher.InvokeAsync(() => 
            {
                _persistentMixer?.AddMixerInput(input);
            });
        }

        public void RemoveMixerInput(ISampleProvider input)
        {
            _dispatcher.InvokeAsync(() =>
            {
                _persistentMixer?.RemoveMixerInput(input);
            });
        }

        private void SelectDefaultDevices()
        {
            _selectedPlaybackDevice ??= _viewModel.PlaybackDevices
                .FirstOrDefault(d => d.FriendlyName.Contains("CABLE Input"))
                ?? _viewModel.PlaybackDevices.FirstOrDefault();

            _selectedCaptureDevice ??= _viewModel.CaptureDevices
                .FirstOrDefault(d => !d.FriendlyName.Contains("CABLE"))
                ?? _viewModel.CaptureDevices.FirstOrDefault();
        }
    }
}