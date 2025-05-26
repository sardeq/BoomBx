using BoomBx.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Speech.Synthesis;

namespace BoomBx.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        [ObservableProperty]
        private string? _appVersion;

        [ObservableProperty]
        private Soundboard? _selectedSoundboard;

        [ObservableProperty]
        private SoundItem? _selectedSound;

        [ObservableProperty]
        private bool _isLoopingEnabled;

        [ObservableProperty]
        private string? _selectedFilePath;

        public ObservableCollection<Soundboard> Soundboards { get; } = new();

        public MainWindowViewModel()
        {
            _appVersion = $"Version {AppVersionHelper.GetInformationalVersion()}";
        }

        partial void OnSelectedSoundChanged(SoundItem? value)
        {
            SelectedFilePath = value?.Path ?? string.Empty;
        }

        [ObservableProperty]
        private string _ttsText = "";
        
        [ObservableProperty]
        private double _ttsVolume = 100;
        
        [ObservableProperty]
        private double _ttsPitch = 1.0;
        
        [ObservableProperty]
        private double _ttsSpeed = 1.0;

        [ObservableProperty]
        private ObservableCollection<VoiceInfo> _availableVoices = new();

        [ObservableProperty]
        private VoiceInfo? _selectedVoice;
    }
}