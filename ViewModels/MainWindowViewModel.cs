using BoomBx.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace BoomBx.ViewModels
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<SoundItem> Sounds { get; } = new();

        private SoundItem? _selectedSound;
        public SoundItem? SelectedSound
        {
            get => _selectedSound;
            set
            {
                if (_selectedSound != value)
                {
                    _selectedSound = value;
                    SelectedFilePath = _selectedSound?.Path ?? string.Empty;
                    OnPropertyChanged(nameof(SelectedSound));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private string? _selectedFilePath;
        public string? SelectedFilePath
        {
            get => _selectedFilePath;
            private set
            {
                _selectedFilePath = value;
                OnPropertyChanged(nameof(SelectedFilePath));
            }
        }
    }
}