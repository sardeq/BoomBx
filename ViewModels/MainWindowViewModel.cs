using BoomBx.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

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
    }
}