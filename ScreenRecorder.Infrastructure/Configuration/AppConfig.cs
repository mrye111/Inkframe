namespace ScreenRecorder.Infrastructure.Configuration;

/// <summary>应用配置模型（需求文档 §59），JSON 持久化于 %AppData%\Inkframe\config.json。</summary>
public sealed class AppConfig
{
    /// <summary>配置结构版本，用于 §60 版本迁移。</summary>
    public int Version { get; set; } = CurrentVersion;

    public const int CurrentVersion = 1;

    public string OutputDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Inkframe");

    public VideoConfig Video { get; set; } = new();
    public AudioConfig Audio { get; set; } = new();
    public HotkeyConfig Hotkeys { get; set; } = new();
    public AdvancedConfig Advanced { get; set; } = new();
}

public sealed class VideoConfig
{
    public int Fps { get; set; } = 30;                    // §13：24/30/60
    public string Quality { get; set; } = "标准";          // §14：低/标准/高清/超清
    public string Encoder { get; set; } = "auto";          // §15：auto 时硬编优先
}

public sealed class AudioConfig
{
    public bool SystemAudio { get; set; } = true;          // §16
    public bool Microphone { get; set; }                   // §17
    public double SystemVolume { get; set; } = 1.0;
    public double MicrophoneVolume { get; set; } = 1.0;
}

public sealed class HotkeyConfig                                 // §29 默认值
{
    public string ToggleRecording { get; set; } = "Ctrl+Alt+R";
    public string TogglePause { get; set; } = "Ctrl+Alt+P";
    public string ToggleMicrophone { get; set; } = "Ctrl+Alt+M";
}

public sealed class AdvancedConfig
{
    public bool CrashProtection { get; set; } = true;      // §56
}
