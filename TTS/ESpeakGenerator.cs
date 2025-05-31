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
    private bool _isInitialized = false;
    private readonly string _espeakPath;
    private readonly string _espeakDataPath;

    public ESpeakGenerator()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        
        _espeakPath = Path.Combine(
            baseDir,
            "espeak", "Windows", "command_line", "espeak.exe"
        );
        
        _espeakDataPath = Path.Combine(
            baseDir,
            "espeak", "Windows"
        );
    }


    public void Initialize()
    {
        // Verify eSpeak executable exists
        if (!File.Exists(_espeakPath))
        {
            throw new FileNotFoundException($"eSpeak executable not found at: {_espeakPath}");
        }
        
        // Verify espeak-data folder exists
        string dataFolder = Path.Combine(_espeakDataPath, "espeak-data");
        if (!Directory.Exists(dataFolder))
        {
            throw new DirectoryNotFoundException($"eSpeak data folder not found at: {dataFolder}");
        }
        
        _isInitialized = true;
        Console.WriteLine($"eSpeak initialized with:");
        Console.WriteLine($"  Executable: {_espeakPath}");
        Console.WriteLine($"  Data path: {_espeakDataPath}");
    }

    public List<string> GetAvailableVoices()
    {
        return new List<string> {
            "en", "en-gb", "en-us", "es", "fr", "de",
            "it", "ru", "zh", "hi"
        };
        // add arabic soon
    }

    public async Task<MemoryStream> GenerateSpeechAsync(string text, string voice, float speed, float pitch)
    {
        if (!_isInitialized)
        {
            Initialize();
        }

        if (!File.Exists(_espeakPath))
        {
            throw new FileNotFoundException($"eSpeak executable not found at: {_espeakPath}");
        }

        int speedValue = (int)(175 * speed);
        int pitchValue = (int)(pitch * 50);

        string tempPath = Path.GetTempFileName() + ".wav";
        
        text = text.Replace("\"", "\\\"");
        
        string args = $"--path=\"{_espeakDataPath}\" -v {voice} -p {pitchValue} -s {speedValue} -w \"{tempPath}\" \"{text}\"";

        Console.WriteLine($"Running eSpeak with args: {args}");

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _espeakPath,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = _espeakDataPath // Set working directory to help eSpeak find files
            }
        };

        try
        {
            process.Start();
            
            string errorOutput = await process.StandardError.ReadToEndAsync();
            
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                throw new Exception($"eSpeak error (code {process.ExitCode}): {errorOutput}");
            }

            // Wait for file to be created
            for (int i = 0; i < 10; i++)
            {
                if (File.Exists(tempPath) && new FileInfo(tempPath).Length > 0)
                {
                    break;
                }
                await Task.Delay(100);
            }

            if (!File.Exists(tempPath))
            {
                throw new Exception("eSpeak did not generate output file");
            }

            // Read and return the generated audio
            var bytes = await File.ReadAllBytesAsync(tempPath);
            var stream = new MemoryStream(bytes);
            stream.Position = 0;
            return stream;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"eSpeak generation error: {ex.Message}");
            throw;
        }
        finally
        {
            // Clean up temp file
            try 
            { 
                if (File.Exists(tempPath)) 
                    File.Delete(tempPath); 
            } 
            catch { /* Ignore cleanup errors */ }
            
            process.Dispose();
        }
    }
}