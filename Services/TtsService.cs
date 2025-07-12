using BoomBx.Models;
using BoomBx.ViewModels;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VoiceInfo = BoomBx.Models.VoiceInfo;

namespace BoomBx.Services
{
    public class TtsService
    {
        private readonly MainWindowViewModel _viewModel;
        private MemoryStream? _ttsAudioStream;
        private ESpeakGenerator? _espeakGenerator;
        
        public TtsService(MainWindowViewModel viewModel)
        {

            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            InitializeESpeak();
        }

        private void InitializeESpeak()
        {
            try
            {
                _espeakGenerator = new ESpeakGenerator();
                _espeakGenerator.Initialize();
                RefreshVoices();
                Console.WriteLine("TTS Service initialized successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"eSpeak initialization error: {ex.Message}");
                throw new Exception($"Failed to initialize text-to-speech: {ex.Message}", ex);
            }
        }
        
        public void Cleanup()
        {
            _espeakGenerator?.Cleanup();
        }

        public void RefreshVoices()
        {
            _viewModel.AvailableVoices.Clear();

            if (_espeakGenerator != null)
            {
                foreach (var voice in _espeakGenerator.GetAvailableVoices())
                {
                    _viewModel.AvailableVoices.Add(new VoiceInfo { Name = voice });
                }
                _viewModel.SelectedVoice = _viewModel.AvailableVoices.FirstOrDefault();
            }
        }

        public async Task GenerateTtsAudioAsync()
        {
            if (string.IsNullOrWhiteSpace(_viewModel.TtsText)) 
                throw new Exception("Please enter text to speak");
            
            if (_espeakGenerator == null)
                throw new Exception("Text-to-speech engine not initialized");

            try
            {
                _ttsAudioStream?.Dispose();
                
                _ttsAudioStream = await _espeakGenerator.GenerateSpeechAsync(
                    text: _viewModel.TtsText,
                    voice: _viewModel.SelectedVoice?.Name ?? "en",
                    speed: (float)_viewModel.TtsSpeed,
                    pitch: (float)_viewModel.TtsPitch
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"TTS Generation Error: {ex.Message}");
                throw new Exception($"Failed to generate speech: {ex.Message}", ex);
            }
        }

        public MemoryStream? GetAudioStream()
        {
            if (_ttsAudioStream != null)
            {
                _ttsAudioStream.Position = 0;
            }
            return _ttsAudioStream;
        }

        public void SaveToSoundboard(string filePath)
        {
            if (_ttsAudioStream == null) return;

            using var fileStream = File.Create(filePath);
            _ttsAudioStream.Position = 0;
            _ttsAudioStream.CopyTo(fileStream);
        }
    }
}