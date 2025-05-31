using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BoomBx.Models;
using BoomBx.Services;
using BoomBx.ViewModels;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using NAudio.Wave;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace BoomBx.Views
{
    public partial class MainWindow : Window
    {
        private TtsService? _ttsService;

        private void InitializeTts()
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                _ttsService = new TtsService(viewModel);
            }
        }

        private void TextToSpeechNav_Checked(object? sender, RoutedEventArgs e)
        {
            _ttsService?.RefreshVoices();
        }
    

        private async void PreviewTts_Click(object? sender, RoutedEventArgs e)
        {
            if (_ttsService == null) return;

            try
            {
                await _ttsService.GenerateTtsAudioAsync();
                StartTtsPlayback();
            }
            catch (Exception ex)
            {
                UpdateStatus($"TTS Error: {ex.Message}");
            }
        }




        private async void SaveTtsAudio_Click(object sender, RoutedEventArgs e)
        {
            if (_ttsService?.GetAudioStream() == null) return;

            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return;

                var file = await topLevel.StorageProvider.SaveFilePickerAsync(
                    new FilePickerSaveOptions
                    {
                        Title = "Save TTS Audio",
                        SuggestedFileName = $"TTS_{DateTime.Now:yyyyMMddHHmmss}.wav",
                        FileTypeChoices = new[]
                        {
                            new FilePickerFileType("WAV Audio")
                            {
                                Patterns = new[] { "*.wav" }
                            }
                        }
                    });

                if (DataContext is MainWindowViewModel vm)
                {
                    if (file != null)
                    {
                        var newItem = new SoundItem
                        {
                            Path = file.Path.AbsolutePath,
                            Name = $"TTS: {vm.TtsText?.TrimTo(20) ?? "Untitled"}",
                            Volume = vm.TtsVolume,
                            Pitch = vm.TtsPitch

                        };

                        vm.SelectedSoundboard?.Sounds.Add(newItem);
                        SaveSoundLibrary();
                        UpdateStatus("💾 TTS audio saved to soundboard!");

                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Save Error: {ex.Message}");
            }
            await Task.CompletedTask;
        }

        private void StartTtsPlayback()
        {
            var service = _ttsService;
            if (service == null) return;

            if (DataContext is not MainWindowViewModel viewModel) return;

            try
            {

                var audioStream = service.GetAudioStream();
                if (audioStream == null) return;

                StopAudioProcessing(updateStatus: false);
                var ttsReader = new WaveFileReader(audioStream);
                var loopStream = new LoopStream(ttsReader) { Loop = ((MainWindowViewModel)DataContext).IsLoopingEnabled };

                var targetFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
                float initialVolume = (float)(((MainWindowViewModel)DataContext).TtsVolume / 100.0);

                var soundParams = new SoundItem
                {
                    Volume = ((MainWindowViewModel)DataContext).TtsVolume,
                    Pitch = ((MainWindowViewModel)DataContext).TtsPitch,
                    Bass = 0,
                    Treble = 0
                };

                var virtualChain = CreateProcessingChain(loopStream, targetFormat, soundParams, initialVolume);
                var speakerChain = CreateProcessingChain(loopStream, targetFormat, soundParams, initialVolume);

                if (virtualChain?.Volume == null || speakerChain?.Volume == null) return;

                _equalizerVirtual = virtualChain.Value.Eq;
                _pitchShifterVirtual = virtualChain.Value.Pitch;
                _volumeProviderVirtual = virtualChain.Value.Volume;

                _equalizerSpeaker = speakerChain.Value.Eq;
                _pitchShifterSpeaker = speakerChain.Value.Pitch;
                _volumeProviderSpeaker = speakerChain.Value.Volume;

                InitializeVirtualOutput();
                InitializeSpeakerOutput();

                _currentPlaybackState = PlaybackState.Playing;
                UpdateStatus($"🗣️ Playing TTS: {((MainWindowViewModel)DataContext).TtsText.TrimTo(20)}");
            }
            catch (Exception ex)
            {
                UpdateStatus($"TTS Playback Error: {ex.Message}");
            }
            finally
            {
                UpdatePlayPauseButtonState();
            }
        }



        private string GetESpeakBinaryPath()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Path.Combine(appDir, "espeak", "espeak.exe");
            }
            else
            {
                return "espeak";
            }
        }


        /*
        protected override async void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            // run command line??
        }
        */

        //# Debian/Ubuntu
        //sudo apt-get install espeak

        //# macOS
        //brew install espeak

    }
}