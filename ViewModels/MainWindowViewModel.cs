using BoomBx.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using NAudio.CoreAudioApi;
using System.Collections.ObjectModel;
using System.Speech.Synthesis;
using VoiceInfo = BoomBx.Models.VoiceInfo;

namespace BoomBx.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {

        public MainWindowViewModel()
        {
            _appVersion = $"Version {AppVersionHelper.GetInformationalVersion()}";
            PlaybackDevices = new ObservableCollection<MMDevice>();
            CaptureDevices = new ObservableCollection<MMDevice>();
        }

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
        private double _ttsSpeed = 0.5;

        [ObservableProperty]
        private ObservableCollection<VoiceInfo> _availableVoices = [];

        [ObservableProperty]
        private VoiceInfo? _selectedVoice;
        
        [ObservableProperty]
        private ObservableCollection<MMDevice> _playbackDevices = new();

        [ObservableProperty]
        private ObservableCollection<MMDevice> _captureDevices = new();

        [ObservableProperty]
        private MMDevice? _selectedPlaybackDevice;

        [ObservableProperty]
        private MMDevice? _selectedCaptureDevice;
    }
}