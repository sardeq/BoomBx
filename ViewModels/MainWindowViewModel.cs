using BoomBx.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace BoomBx.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject 
    {
        [ObservableProperty]
        private string? _appVersion;

        public MainWindowViewModel()
        {
            _appVersion = $"Version {AppVersionHelper.GetInformationalVersion()}";
        }

        public ObservableCollection<SoundItem> Sounds { get; } = new();

        private SoundItem? _selectedSound;
        public SoundItem? SelectedSound
        {
            get => _selectedSound;
            set
            {
                if (SetProperty(ref _selectedSound, value))
                {
                    SelectedFilePath = _selectedSound?.Path ?? string.Empty;
                }
            }
        }

        private string? _selectedFilePath;
        public string? SelectedFilePath
        {
            get => _selectedFilePath;
            private set => SetProperty(ref _selectedFilePath, value);
        }
    }
}