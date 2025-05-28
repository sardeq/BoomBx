using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using BoomBx.Models;
using BoomBx.Services;
using BoomBx.ViewModels;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.ComponentModel;
using System.IO;

namespace BoomBx.Views
{
    public partial class MainWindow : Window
    {
        private AudioService? _audioService;

        private IWavePlayer? _audioWaveOutSpeaker;
        private VolumeSampleProvider? _volumeProviderVirtual;
        private VolumeSampleProvider? _volumeProviderSpeaker;
        private SoundItem? _currentSoundSubscription;
        private LoopStream? _virtualLoopStream;
        private LoopStream? _speakerLoopStream;
        private EqualizerSampleProvider? _equalizerVirtual;
        private EqualizerSampleProvider? _equalizerSpeaker;
        private PitchShifter? _pitchShifterVirtual;
        private PitchShifter? _pitchShifterSpeaker;


        private enum PlaybackState { Stopped, Playing, Paused }
        private PlaybackState _currentPlaybackState = PlaybackState.Stopped;

        private void InitializeAudioService()
        {
            try
            {
                if (DataContext == null) 
                    throw new InvalidOperationException("DataContext is not initialized");
                
                _audioService = new AudioService(
                    (MainWindowViewModel)DataContext,
                    _settings,
                    _deviceManager
                );
            }
            catch (Exception ex)
            {
                UpdateStatus($"Audio service init failed: {ex.Message}");
            }
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (ViewModel?.SelectedSound == null) return;
            
            if (e.PropertyName == nameof(MainWindowViewModel.SelectedSound))
            {
                if (_currentPlaybackState != PlaybackState.Stopped)
                {
                    StopAudioProcessing();
                }

                if (_currentSoundSubscription != null)
                {
                    _currentSoundSubscription.PropertyChanged -= SoundItem_VolumeChanged;
                }

                _currentSoundSubscription = ViewModel.SelectedSound;

                if (_currentSoundSubscription != null)
                {
                    _currentSoundSubscription.PropertyChanged += SoundItem_VolumeChanged;
                }
            }
            else if (e.PropertyName == nameof(MainWindowViewModel.IsLoopingEnabled))
            {
                if (_virtualLoopStream != null)
                    _virtualLoopStream.Loop = ViewModel.IsLoopingEnabled;
                if (_speakerLoopStream != null)
                    _speakerLoopStream.Loop = ViewModel.IsLoopingEnabled;
            }
        }

        private void SoundItem_VolumeChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not SoundItem soundItem ||
                _currentPlaybackState != PlaybackState.Playing ||
                _volumeProviderVirtual == null ||
                _volumeProviderSpeaker == null)
            {
                return;
            }

            try
            {
                switch (e.PropertyName)
                {
                    case nameof(SoundItem.Volume):
                        _volumeProviderVirtual.Volume = (float)soundItem.Volume / 100;
                        _volumeProviderSpeaker.Volume = (float)soundItem.Volume / 100;
                        break;

                    case nameof(SoundItem.Bass):
                    case nameof(SoundItem.Treble):
                        _equalizerVirtual?.UpdateFilters(soundItem);
                        _equalizerSpeaker?.UpdateFilters(soundItem);
                        break;

                    case nameof(SoundItem.Pitch):
                        _pitchShifterVirtual?.SetPitch((float)soundItem.Pitch);
                        _pitchShifterSpeaker?.SetPitch((float)soundItem.Pitch);
                        break;
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"⚠️ Update failed: {ex.Message}");
            }
        }

        private void PlayPauseHandler(object? sender, RoutedEventArgs e)
        {
            switch (_currentPlaybackState)
            {
                case PlaybackState.Stopped:
                    StartAudioProcessing();
                    break;
                case PlaybackState.Playing:
                    PauseAudioProcessing();
                    break;
                case PlaybackState.Paused:
                    ResumeAudioProcessing();
                    break;
            }
        }

        private void StopHandler(object? sender, RoutedEventArgs e)
        {
            StopAudioProcessing();
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

        private void StartAudioProcessing()
        {
            try
            {
                StopAudioProcessing(updateStatus: false);

                if (ViewModel.SelectedSound == null)
                {
                    UpdateStatus("🔇 No sound selected");
                    return;
                }

                if (string.IsNullOrWhiteSpace(ViewModel.SelectedSound.Path) || !File.Exists(ViewModel.SelectedSound.Path))
                {
                    UpdateStatus("❌ Invalid audio file path");
                    return;
                }

                var tempVirtualStream = CreateAudioStream(ViewModel.SelectedSound.Path);
                var tempSpeakerStream = CreateAudioStream(ViewModel.SelectedSound.Path);

                if (tempVirtualStream == null || tempSpeakerStream == null)
                {
                    UpdateStatus("⚠️ Failed to initialize audio streams");
                    tempVirtualStream?.Dispose();
                    tempSpeakerStream?.Dispose();
                    return;
                }

                if (tempVirtualStream.WaveFormat == null || tempSpeakerStream.WaveFormat == null)
                {
                    UpdateStatus("⚠️ Audio stream has invalid format");
                    tempVirtualStream.Dispose();
                    tempSpeakerStream.Dispose();
                    return;
                }

                _virtualLoopStream = tempVirtualStream;
                _speakerLoopStream = tempSpeakerStream;
                _virtualLoopStream.Loop = ViewModel.IsLoopingEnabled;
                _speakerLoopStream.Loop = ViewModel.IsLoopingEnabled;

                var targetFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
                float initialVolume = (float)(ViewModel.SelectedSound.Volume / 100.0);

                var virtualChain = CreateProcessingChain(_virtualLoopStream, targetFormat, ViewModel.SelectedSound, initialVolume);
                var speakerChain = CreateProcessingChain(_speakerLoopStream, targetFormat, ViewModel.SelectedSound, initialVolume);

                if (virtualChain?.Volume == null || speakerChain?.Volume == null)
                {
                    UpdateStatus("⚠️ Failed to create processing chain");
                    StopAudioProcessing(updateStatus: false);
                    return;
                }

                if (virtualChain is { } vChain)
                {
                    _equalizerVirtual = vChain.Eq;
                    _pitchShifterVirtual = vChain.Pitch;
                    _volumeProviderVirtual = vChain.Volume;
                }

                if (speakerChain is { } sChain)
                {
                    _equalizerSpeaker = sChain.Eq;
                    _pitchShifterSpeaker = sChain.Pitch;
                    _volumeProviderSpeaker = sChain.Volume;
                }

                InitializeVirtualOutput();
                InitializeSpeakerOutput();

                _currentPlaybackState = PlaybackState.Playing;
                UpdateStatus($"🎵 Playing {Path.GetFileName(ViewModel.SelectedSound.Path)}");
            }
            catch (Exception ex)
            {
                UpdateStatus($"⛔ Error: {ex.Message}");
                StopAudioProcessing(updateStatus: false);
            }
            finally
            {
                UpdatePlayPauseButtonState();
            }
        }

        private (EqualizerSampleProvider Eq, PitchShifter Pitch, VolumeSampleProvider Volume)?
            CreateProcessingChain(LoopStream stream, WaveFormat format, SoundItem sound, float volume)
        {
            try
            {
                if (stream == null) throw new ArgumentNullException(nameof(stream));
                if (sound == null) throw new ArgumentNullException(nameof(sound));
                if (stream.WaveFormat == null) throw new InvalidOperationException("Stream has no WaveFormat");

                var sampleProvider = stream.ToSampleProvider();
                if (sampleProvider == null) throw new InvalidOperationException("ToSampleProvider returned null");
                if (sampleProvider.WaveFormat == null) throw new InvalidOperationException("Sample provider has no WaveFormat");

                var provider = AudioService.ConvertFormat(sampleProvider, format);
                if (provider == null) throw new InvalidOperationException("Format conversion returned null");
                if (provider.WaveFormat == null) throw new InvalidOperationException("Converted provider has no WaveFormat");

                var eq = new EqualizerSampleProvider(provider, sound);
                var pitch = new PitchShifter(eq);
                pitch.SetPitch((float)sound.Pitch);
                var vol = new VolumeSampleProvider(pitch) { Volume = volume };
                return (eq, pitch, vol);
            }
            catch (Exception ex)
            {
                UpdateStatus($"⚠️ Processing chain error: {ex.Message} (Type: {ex.GetType().Name})");
                return null;
            }
        }

        private void HandlePlaybackStopped(object? sender, StoppedEventArgs args)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_currentPlaybackState == PlaybackState.Playing && args.Exception == null)
                {
                    StopAudioProcessing();
                    UpdatePlayPauseButtonState();
                    UpdateStatus("Playback completed");
                }
            });
        }

        private void PauseAudioProcessing()
        {
            if (_currentPlaybackState != PlaybackState.Playing) return;

            _audioWaveOutSpeaker?.Pause();
            if (_volumeProviderVirtual != null)
            {
                _deviceManager.RemoveMixerInput(_volumeProviderVirtual);
            }
            _currentPlaybackState = PlaybackState.Paused;
            UpdatePlayPauseButtonState();
        }

        private void ResumeAudioProcessing()
        {
            if (_currentPlaybackState != PlaybackState.Paused) return;

            if (_volumeProviderVirtual != null)
            {
                _deviceManager.AddMixerInput(_volumeProviderVirtual);
            }
            _audioWaveOutSpeaker?.Play();
            _currentPlaybackState = PlaybackState.Playing;
            UpdatePlayPauseButtonState();
        }

        private void StopAudioProcessing(bool updateStatus = true)
        {
            try
            {
                _currentPlaybackState = PlaybackState.Stopped;

                _audioWaveOutSpeaker?.Stop();
                _audioWaveOutSpeaker?.Dispose();

                if (_volumeProviderVirtual != null)
                {
                    _deviceManager.RemoveMixerInput(_volumeProviderVirtual);
                }

                _virtualLoopStream?.Dispose();
                _speakerLoopStream?.Dispose();

                _equalizerVirtual = null;
                _equalizerSpeaker = null;
                _pitchShifterVirtual = null;
                _pitchShifterSpeaker = null;
                _volumeProviderVirtual = null;
                _volumeProviderSpeaker = null;
                _virtualLoopStream = null;
                _speakerLoopStream = null;

                if (updateStatus) UpdateStatus("⏹ Playback stopped");
            }
            catch (Exception ex)
            {
                UpdateStatus($"⚠️ Stop error: {ex.Message}");
            }
            finally
            {
                UpdatePlayPauseButtonState();
            }
        }

        private void UpdatePlayPauseButtonState()
        {
            Dispatcher.UIThread.Post(() =>
            {
                PlayPauseButton.Content = _currentPlaybackState switch
                {
                    PlaybackState.Playing => "⏸ Pause",
                    PlaybackState.Paused => "▶ Resume",
                    _ => "▶ Play"
                };

                StopButton.IsVisible = _currentPlaybackState != PlaybackState.Stopped;
            });
        }

        private LoopStream? CreateAudioStream(string filePath)
        {
            try
            {
                WaveStream reader = Path.GetExtension(filePath).ToLower() switch
                {
                    ".mp3" => new Mp3FileReader(filePath),
                    ".wav" => new WaveFileReader(filePath),
                    _ => throw new InvalidOperationException("Unsupported file format")
                };
                if (reader.WaveFormat == null ||
                    reader.WaveFormat.Channels <= 0 ||
                    reader.WaveFormat.SampleRate <= 0)
                {
                    throw new InvalidOperationException("Audio file has an invalid format");
                }
                return new LoopStream(reader);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error loading audio: {ex.Message}");
                return null;
            }
        }
    }
}