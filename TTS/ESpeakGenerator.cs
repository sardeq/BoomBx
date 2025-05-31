using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

public class ESpeakGenerator
{
    private bool _isInitialized = false;
    private  string _espeakPath;
    private  string _espeakDataPath;
    private string? _extractionDir;

    public ESpeakGenerator()
    {
        _espeakPath = "";
        _espeakDataPath = "";
    }


    public void Initialize()
    {
        if (_isInitialized) return;

        Logger.Log("Initializing Espeak");

        try
        {
            _extractionDir = Path.Combine(Path.GetTempPath(), "BoomBx_espeak");
            
            // Clean up any existing extraction directory
            if (Directory.Exists(_extractionDir))
            {
                try
                {
                    Directory.Delete(_extractionDir, true);
                }
                catch
                {
                    // If we can't delete, try a different temp directory
                    _extractionDir = Path.Combine(Path.GetTempPath(), $"BoomBx_espeak_{Guid.NewGuid():N}");
                }
            }
            
            Directory.CreateDirectory(_extractionDir);
            
            ExtractEmbeddedResources();
            
            _espeakPath = Path.Combine(_extractionDir, "command_line", "espeak.exe");
            _espeakDataPath = _extractionDir;

            if (!File.Exists(_espeakPath))
            {
                Logger.Log($"eSpeak executable not found at: {_espeakPath}");
                
                // List what we actually extracted for debugging
                Logger.Log("Files extracted:");
                if (Directory.Exists(_extractionDir))
                {
                    foreach (var file in Directory.GetFiles(_extractionDir, "*", SearchOption.AllDirectories))
                    {
                        Logger.Log($"  {Path.GetRelativePath(_extractionDir, file)}");
                    }
                }
                
                throw new FileNotFoundException($"eSpeak executable not found at: {_espeakPath}");
            }
            
            if (!Directory.Exists(_espeakDataPath))
            {
                Logger.Log($"eSpeak data folder not found at: {_espeakDataPath}");
                throw new DirectoryNotFoundException($"eSpeak data folder not found at: {_espeakDataPath}");
            }
            
            _isInitialized = true;
            Console.WriteLine($"eSpeak initialized successfully:");
            Console.WriteLine($"  Executable: {_espeakPath}");
            Console.WriteLine($"  Data path: {_espeakDataPath}");
        }
        catch (Exception ex)
        {
            Logger.Log($"eSpeak initialization failed: {ex}");
            throw;
        }
    }

    private void ExtractEmbeddedResources()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames();
        
        Logger.Log($"Found {resourceNames.Length} embedded resources");
        
        // Look for resources that start with "espeak." prefix
        var espeakResources = resourceNames.Where(n => n.StartsWith("espeak.", StringComparison.OrdinalIgnoreCase)).ToList();
        
        if (!espeakResources.Any())
        {
            // If no resources with "espeak." prefix, look for any espeak-related resources
            espeakResources = resourceNames.Where(n => n.Contains("espeak", StringComparison.OrdinalIgnoreCase)).ToList();
        }
        
        Logger.Log($"Found {espeakResources.Count} eSpeak resources to extract");
        
        foreach (var resourceName in espeakResources)
        {
            try
            {
                Logger.Log($"Extracting: {resourceName}");
                
                string relativePath;
                if (resourceName.StartsWith("espeak."))
                {
                    // Remove the "espeak." prefix
                    relativePath = resourceName.Substring("espeak.".Length);
                    
                    // Normalize path separators - replace forward slashes with backslashes if needed
                    relativePath = relativePath.Replace('/', '\\');
                }
                else
                {
                    // Fallback: use the resource name as-is and try to extract a reasonable path
                    relativePath = resourceName.Replace("BoomBx.", "").Replace("espeak.", "");
                }

                if (_extractionDir != null)
                {
                    // Use the relativePath directly without additional Path.Combine manipulation
                    var filePath = Path.Combine(_extractionDir, relativePath);
                    var directoryPath = Path.GetDirectoryName(filePath);
                    
                    if (!string.IsNullOrEmpty(directoryPath))
                    {
                        Directory.CreateDirectory(directoryPath);
                    }

                    using var resource = assembly.GetManifestResourceStream(resourceName);
                    if (resource != null)
                    {
                        using var file = File.Create(filePath);
                        resource.CopyTo(file);
                        Logger.Log($"Extracted to: {filePath}");
                    }
                    else
                    {
                        Logger.Log($"Resource stream was null for: {resourceName}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error extracting {resourceName}: {ex.Message}");
                Console.WriteLine($"Error extracting {resourceName}: {ex.Message}");
            }
        }
    }

    public void Cleanup()
    {
        try
        {
            if (!string.IsNullOrEmpty(_extractionDir) && Directory.Exists(_extractionDir))
            {
                Directory.Delete(_extractionDir, true);
                Logger.Log("Cleaned up extraction directory");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Cleanup error: {ex.Message}");
        }
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

        string args = $"--path=\"{_extractionDir}\" -v {voice} -p {pitchValue} -s {speedValue} -w \"{tempPath}\" \"{text}\"";

        Logger.Log($"Running eSpeak with args: {args}");

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
                WorkingDirectory = Path.GetDirectoryName(_espeakPath) // Set working directory to eSpeak location
            }
        };

        try
        {
            process.Start();
            
            string errorOutput = await process.StandardError.ReadToEndAsync();
            string standardOutput = await process.StandardOutput.ReadToEndAsync();
            
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                Logger.Log($"eSpeak error output: {errorOutput}");
                Logger.Log($"eSpeak standard output: {standardOutput}");
                throw new Exception($"eSpeak error (code {process.ExitCode}): {errorOutput}");
            }

            // Wait for file to be created with a longer timeout
            for (int i = 0; i < 50; i++) // Increased from 10 to 50 iterations
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
            Logger.Log($"eSpeak generation error: {ex.Message}");
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