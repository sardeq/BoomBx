using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BoomBx.Models
{
    public class SoundItem
    {
        public string Path { get; set; } = string.Empty;
        private string _name = string.Empty;
        private double _volume = 100;
        private string _iconPath = "avares://BoomBx/Assets/bocchi.jpg";

        public string IconPath
        {
            get => _iconPath;
            set
            {
                if (_iconPath != value)
                {
                    _iconPath = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Name
        {
            get => _name ?? System.IO.Path.GetFileNameWithoutExtension(Path);
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                }
            }
        }

        public double Volume
        {
            get => _volume;
            set
            {
                if (Math.Abs(_volume - value) > 0.01)
                {
                    _volume = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}