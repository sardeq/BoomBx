using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

public class ESpeakGenerator
{
    private string? _extractedPath;
    private bool _isInitialized = false;

    public void Initialize()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            ExtractEmbeddedESpeak();
        }

        // Verify eSpeak is actually available
        string executable = GetESpeakExecutable();
        if (!string.IsNullOrEmpty(executable) && (File.Exists(executable) || executable == "espeak"))
        {
            _isInitialized = true;
            Console.WriteLine($"eSpeak initialized with: {executable}");
        }
        else
        {
            throw new FileNotFoundException("eSpeak executable not found. Please install eSpeak.");
        }
    }

    public List<string> GetAvailableVoices()
    {
        return new List<string> { 
            "en", "en-gb", "en-us", "es", "fr", "de", 
            "it", "ru", "zh", "ja", "hi", "ar"
        };
    }

    public async Task<MemoryStream> GenerateSpeechAsync(string text, string voice, float speed, float pitch)
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("eSpeak not initialized");
        }

        int speedValue = (int)(175 * speed);
        int pitchValue = (int)(pitch * 50);

        string tempPath = Path.GetTempFileName() + ".wav";
        string executable = GetESpeakExecutable();

        // Escape quotes in text to prevent command injection
        text = text.Replace("\"", "\\\"");
        
        string args = $"-v {voice} -p {pitchValue} -s {speedValue} -w \"{tempPath}\" \"{text}\"";

        Console.WriteLine($"Running eSpeak: {executable} {args}");

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        try
        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            process.Start();
            
            await process.WaitForExitAsync(cts.Token);

            if (process.ExitCode != 0)
            {
                string error = await process.StandardError.ReadToEndAsync();
                throw new Exception($"eSpeak error (code {process.ExitCode}): {error}");
            }

            // Wait for file to be written
            for (int i = 0; i < 10; i++)
            {
                if (File.Exists(tempPath) && new FileInfo(tempPath).Length > 0)
                {
                    break;
                }
                await Task.Delay(100);
            }

            // Try to read the file
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    var bytes = await File.ReadAllBytesAsync(tempPath);
                    var stream = new MemoryStream(bytes);
                    File.Delete(tempPath);
                    stream.Position = 0;
                    return stream;
                }
                catch (IOException) when (i < 4)
                {
                    await Task.Delay(200);
                }
            }
            throw new Exception("Failed to read TTS output file");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"eSpeak generation error: {ex.Message}");
            // Clean up temp file if it exists
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            throw;
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(); } catch { /* Ignore */ }
            }
            process.Dispose();
        }
    }

    private void ExtractEmbeddedESpeak()
    {
        string targetDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "espeak");
        string targetPath = Path.Combine(targetDir, "espeak.exe");

        try
        {
            Directory.CreateDirectory(targetDir);
            
            if (File.Exists(targetPath))
            {
                var fileInfo = new FileInfo(targetPath);
                if (fileInfo.Length > 0)
                {
                    _extractedPath = targetPath;
                    Console.WriteLine($"Embedded eSpeak already exists: {targetPath} ({fileInfo.Length} bytes)");
                    return;
                }
                else
                {
                    Console.WriteLine("Found empty espeak.exe, deleting and re-extracting...");
                    File.Delete(targetPath);
                }
            }
            
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "BoomBx.espeak.espeak.exe";
            
            using var resource = assembly.GetManifestResourceStream(resourceName);
            if (resource == null)
            {
                Console.WriteLine($"WARNING: Embedded resource '{resourceName}' not found!");
                return;
            }
            
            Console.WriteLine($"Extracting embedded eSpeak ({resource.Length} bytes)...");
            
            using var file = File.Create(targetPath);
            resource.CopyTo(file);
            file.Flush();
            
            _extractedPath = targetPath;
            Console.WriteLine($"Successfully extracted eSpeak to: {targetPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to extract eSpeak: {ex.Message}");
        }
    }

    private string GetESpeakExecutable()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Priority order for Windows:
            // 1. Extracted embedded version
            // 2. Embedded version in app directory
            // 3. System installation
            // 4. PATH

            // Check extracted path
            if (!string.IsNullOrEmpty(_extractedPath) && File.Exists(_extractedPath))
            {
                return _extractedPath;
            }

            // Check embedded path directly
            string embeddedPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "espeak", "espeak.exe");
            if (File.Exists(embeddedPath))
            {
                var fileInfo = new FileInfo(embeddedPath);
                if (fileInfo.Length > 0)
                {
                    Console.WriteLine($"Using embedded eSpeak: {embeddedPath}");
                    return embeddedPath;
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
                    Console.WriteLine($"Using system eSpeak: {path}");
                    return path;
                }
            }

            // Finally, try just "espeak" in case it's in PATH
            Console.WriteLine("Falling back to 'espeak' command (PATH)");
            return "espeak";
        }
        else
        {
            // For Linux/macOS, just use the command
            return "espeak"; 
        }
    }
}