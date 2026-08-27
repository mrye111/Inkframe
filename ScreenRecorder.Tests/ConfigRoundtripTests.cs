using ScreenRecorder.Infrastructure.Configuration;

namespace ScreenRecorder.Tests;

public sealed class ConfigRoundtripTests : IDisposable
{
    private readonly string _tempFile = Path.Combine(Path.GetTempPath(), "inkframe-test-" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void Save_Then_Load_Preserves_Values()
    {
        var service = new ConfigService(_tempFile);
        var config = service.Current;
        config.Video.Fps = 60;
        config.Video.Quality = "超清";
        config.Audio.Microphone = true;
        config.Hotkeys.TogglePause = "Ctrl+Shift+P";
        service.Save(config);

        var reloaded = new ConfigService(_tempFile).Current;

        Assert.Equal(60, reloaded.Video.Fps);
        Assert.Equal("超清", reloaded.Video.Quality);
        Assert.True(reloaded.Audio.Microphone);
        Assert.Equal("Ctrl+Shift+P", reloaded.Hotkeys.TogglePause);
        Assert.Equal(AppConfig.CurrentVersion, reloaded.Version);
    }

    [Fact]
    public void Load_Missing_File_Creates_Defaults()
    {
        var service = new ConfigService(_tempFile);
        var config = service.Current;

        Assert.Equal(30, config.Video.Fps);
        Assert.True(File.Exists(_tempFile));   // 首次加载即落盘默认配置
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
    }
}
