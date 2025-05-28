using Avalonia.Controls;
using Avalonia.Interactivity;
using BoomBx.Services;
using BoomBx.ViewModels;
using System;
using System.Threading.Tasks;

namespace BoomBx.Views
{
    public partial class MainWindow : Window
    {
       #if WINDOWS
        private TtsService? _ttsService;
        #endif

        private void InitializeTts()
        {
            #if WINDOWS
            _ttsService = new TtsService((MainWindowViewModel)DataContext);
            #endif
        }

        private async void PreviewTts_Click(object? sender, RoutedEventArgs e)
        {
        #if WINDOWS
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
        #else
            await Task.CompletedTask;
        #endif
        }

        private async void SaveTtsAudio_Click(object sender, RoutedEventArgs e)
        {
#if WINDOWS
            if (_ttsService.GetAudioStream() == null) return;

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

                if (file != null)
                {
                    _ttsService.SaveToSoundboard(file.Path.AbsolutePath);
                    var newItem = new SoundItem
                    {
                        Path = file.Path.AbsolutePath,
                        Name = $"TTS: {((MainWindowViewModel)DataContext).TtsText.TrimTo(20)}",
                        Volume = ((MainWindowViewModel)DataContext).TtsVolume,
                        Pitch = ((MainWindowViewModel)DataContext).TtsPitch
                    };

                    ((MainWindowViewModel)DataContext).SelectedSoundboard?.Sounds.Add(newItem);
                    SaveSoundLibrary();
                    UpdateStatus("💾 TTS audio saved to soundboard!");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Save Error: {ex.Message}");
            }
#else
            await Task.CompletedTask;
#endif
        }

        private void StartTtsPlayback()
        {
#if WINDOWS
            try
            {
                var audioStream = _ttsService.GetAudioStream();
                if (audioStream == null) return;

                StopAudioProcessing(updateStatus: false);
                var ttsReader = new WaveFileReader(audioStream);
                var loopStream = new LoopStream(ttsReader) { Loop = ((MainWindowViewModel)DataContext).IsLoopingEnabled };

                // Use existing audio processing chain
                var targetFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
                float initialVolume = (float)(((MainWindowViewModel)DataContext).TtsVolume / 100.0);

                var soundParams = new SoundItem
                {
                    Volume = ((MainWindowViewModel)DataContext).TtsVolume,
                    Pitch = ((MainWindowViewModel)DataContext).TtsPitch,
                    Bass = 0,
                    Treble = 0
                };

                // Reuse existing audio processing logic
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
#endif
        }
    }
}