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
        private bool _espeakChecked = false;
        private bool _espeakAvailable = false;

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
            
            if (!_espeakChecked)
            {
                _espeakChecked = true;
                Dispatcher.UIThread.Post(async () => 
                {
                    try
                    {
                        // Only check installation if eSpeak is not already confirmed available
                        if (!_espeakAvailable)
                        {
                            _espeakAvailable = await IsESpeakAvailable();
                            
                            // Only show installer if eSpeak is NOT available
                            if (!_espeakAvailable)
                            {
                                await CheckESpeakInstallation();
                            }
                            else
                            {
                                UpdateStatus("✓ eSpeak is ready for text-to-speech");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        UpdateStatus($"Installation check failed: {ex.Message}");
                    }
                });
            }
        }

        private async void PreviewTts_Click(object? sender, RoutedEventArgs e)
{
    if (_ttsService == null) return;

    try
    {
        // Check if eSpeak is available before trying to generate
        if (!_espeakAvailable)
        {
            _espeakAvailable = await IsESpeakAvailable();
            if (!_espeakAvailable)
            {
                UpdateStatus("eSpeak not available. Please install it first.");
                await CheckESpeakInstallation();
                return;
            }
        }

        await _ttsService.GenerateTtsAudioAsync();
        StartTtsPlayback();
    }
    catch (Exception ex)
    {
        UpdateStatus($"TTS Error: {ex.Message}");
        
        if (ex is FileNotFoundException || ex.Message.Contains("espeak"))
        {
            _espeakAvailable = false; // Reset the flag
            await CheckESpeakInstallation();
        }
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


        protected override async void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            // Pre-extract eSpeak on Windows to avoid issues later
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                ExtractEmbeddedESpeak();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                    RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // Check installation on Linux/macOS
                _espeakAvailable = await IsESpeakAvailable();
                if (!_espeakAvailable)
                {
                    await CheckESpeakInstallation();
                }
            }
        }

        private void ExtractEmbeddedESpeak()
        {
            string targetDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "espeak");
            string targetPath = Path.Combine(targetDir, "espeak.exe");

            try
            {
                // Create directory if it doesn't exist
                Directory.CreateDirectory(targetDir);

                // Check if file already exists and is valid
                if (File.Exists(targetPath))
                {
                    var fileInfo = new FileInfo(targetPath);
                    if (fileInfo.Length > 0) // Check if file is not empty
                    {
                        Console.WriteLine($"Embedded eSpeak already extracted: {targetPath} (Size: {fileInfo.Length} bytes)");
                        return;
                    }
                    else
                    {
                        // File exists but is empty, delete and re-extract
                        Console.WriteLine("Found empty espeak.exe, re-extracting...");
                        File.Delete(targetPath);
                    }
                }

                // Extract embedded resource
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = "BoomBx.espeak.espeak.exe";

                // List all resources for debugging
                var resources = assembly.GetManifestResourceNames();
                Console.WriteLine("Available embedded resources:");
                foreach (var res in resources)
                {
                    Console.WriteLine($"  - {res}");
                }

                using var resource = assembly.GetManifestResourceStream(resourceName);

                if (resource == null)
                {
                    Console.WriteLine($"ERROR: Embedded resource '{resourceName}' not found!");
                    UpdateStatus("⚠️ Embedded eSpeak not found in resources");
                    return;
                }

                Console.WriteLine($"Extracting eSpeak (Resource size: {resource.Length} bytes)...");

                using var file = File.Create(targetPath);
                resource.CopyTo(file);
                file.Flush(); // Ensure all data is written

                // Verify extraction
                var extractedInfo = new FileInfo(targetPath);
                Console.WriteLine($"Successfully extracted eSpeak to: {targetPath} (Size: {extractedInfo.Length} bytes)");
                UpdateStatus("✓ Extracted embedded eSpeak");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to extract eSpeak: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                UpdateStatus($"⚠️ Failed to extract eSpeak: {ex.Message}");
            }
        }



        private async Task CheckESpeakInstallation()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Always try to extract embedded version first (non-blocking)
                ExtractEmbeddedESpeak();

                // Re-check after extraction attempt
                _espeakAvailable = await IsESpeakAvailable();

                if (_espeakAvailable)
                {
                    return; // eSpeak is now available, no need to show installation prompt
                }

                // If we reach here, eSpeak is not available anywhere
                await ShowInstallPrompt(
                    "eSpeak not found. The application includes an embedded version, but extraction may have failed.\n\n" +
                    "Options:\n" +
                    "1. Restart the application (may fix extraction issues)\n" +
                    "2. Install eSpeak manually from: https://sourceforge.net/projects/espeak/\n" +
                    "3. Check if antivirus is blocking the embedded espeak.exe\n\n" +
                    "Without eSpeak, text-to-speech will not function.");
            }
            else
            {
                // Linux/macOS check remains the same
                try
                {
                    using var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "which",
                            Arguments = "espeak",
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    process.Start();
                    string output = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    if (string.IsNullOrWhiteSpace(output))
                    {
                        await ShowInstallPrompt("eSpeak not installed. Please install with:\n\n" +
                                            "• Linux (Debian/Ubuntu):\n   sudo apt-get install espeak\n\n" +
                                            "• macOS:\n   brew install espeak");
                    }
                    else
                    {
                        _espeakAvailable = true;
                    }
                }
                catch
                {
                    await ShowInstallPrompt("Failed to check eSpeak installation");
                }
            }
        }



        private async Task<bool> IsESpeakAvailable()
        {
            try
            {
                // First check embedded/extracted version
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string embeddedPath = Path.Combine(appDir, "espeak", "espeak.exe");
                
                if (File.Exists(embeddedPath))
                {
                    var fileInfo = new FileInfo(embeddedPath);
                    if (fileInfo.Length > 0) // Make sure file is not empty
                    {
                        Console.WriteLine($"Found embedded eSpeak at: {embeddedPath}");
                        UpdateStatus("✓ Using embedded eSpeak");
                        return true;
                    }
                }

                // Check common installation paths
                string[] commonPaths = {
                    @"C:\Program Files (x86)\eSpeak\command_line\espeak.exe",
                    @"C:\Program Files\eSpeak\command_line\espeak.exe",
                    @"C:\eSpeak\command_line\espeak.exe"
                };

                foreach (string path in commonPaths)
                {
                    if (File.Exists(path))
                    {
                        Console.WriteLine($"Found eSpeak at: {path}");
                        UpdateStatus($"✓ Using system eSpeak from: {Path.GetDirectoryName(path)}");
                        return true;
                    }
                }

                // Try to run espeak command to see if it's in PATH
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/c espeak --version",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                
                // Don't wait forever
                var completed = process.WaitForExit(3000);
                
                if (completed && process.ExitCode == 0)
                {
                    string output = await process.StandardOutput.ReadToEndAsync();
                    Console.WriteLine($"eSpeak in PATH: {output}");
                    UpdateStatus("✓ Using system eSpeak (PATH)");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking eSpeak availability: {ex.Message}");
                return false;
            }
        }

        private async Task ShowInstallPrompt(string message)
        {
            var box = MessageBoxManager.GetMessageBoxStandard(
                title: "eSpeak Installation Required",
                text: message + "\n\nWithout eSpeak, text-to-speech will not function.",
                @enum: ButtonEnum.Ok);

            await box.ShowAsync();
        }

        //# Debian/Ubuntu
        //sudo apt-get install espeak

        //# macOS
        //brew install espeak

    }
}