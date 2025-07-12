using System.Reflection;

public class AppSettings
{
    public string? LastPlaybackDevice { get; set; }
    public string? LastCaptureDevice { get; set; }

    public string PlayPauseHotkey { get; set; } = "F9";
    public string StopHotkey { get; set; } = "F10";
    public string PauseHotkey { get; set; } = "F11";
}