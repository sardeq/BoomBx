using Avalonia.Controls;
using Avalonia.Interactivity;
using BoomBx.Services;
using BoomBx.ViewModels;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Threading.Tasks;

namespace BoomBx.Views
{
    public partial class MainWindow : Window
    {
        private DeviceManager _deviceManager;

        private void InitializeVirtualOutput()
        {
            try
            {
                if (_volumeProviderVirtual != null)
                {
                    _deviceManager.AddMixerInput(_volumeProviderVirtual);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"🔇 Virtual output error: {ex.Message}");
            }
        }
    }
}