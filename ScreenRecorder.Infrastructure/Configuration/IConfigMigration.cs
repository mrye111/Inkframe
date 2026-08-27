namespace ScreenRecorder.Infrastructure.Configuration;

/// <summary>配置版本迁移钩子（§60）：把旧版本 JSON 数据升格到下一版本。</summary>
public interface IConfigMigration
{
    int FromVersion { get; }
    int ToVersion { get; }
    void Migrate(System.Text.Json.Nodes.JsonObject configJson);
}
