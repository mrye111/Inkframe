using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenRecorder.Infrastructure.Configuration;

/// <summary>
/// JSON 配置读写（§59）：加载时按版本迁移链（§60）逐级升格，保存总是当前版本。
/// </summary>
public sealed class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly IReadOnlyList<IConfigMigration> _migrations;
    private AppConfig? _current;

    public ConfigService(string? filePath = null, IEnumerable<IConfigMigration>? migrations = null)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Inkframe");
        Directory.CreateDirectory(dir);
        _filePath = filePath ?? Path.Combine(dir, "config.json");
        _migrations = (migrations ?? Enumerable.Empty<IConfigMigration>()).OrderBy(m => m.FromVersion).ToList();
    }

    public AppConfig Current => _current ??= Load();

    public AppConfig Load()
    {
        if (!File.Exists(_filePath))
        {
            _current = new AppConfig();
            Save(_current);
            return _current;
        }

        var json = JsonNode.Parse(File.ReadAllText(_filePath))!.AsObject();
        var version = json["Version"]?.GetValue<int>() ?? 0;

        // §60：沿迁移链逐级升格到当前版本
        while (version < AppConfig.CurrentVersion)
        {
            var step = _migrations.FirstOrDefault(m => m.FromVersion == version);
            if (step is null) break; // 无迁移路径：保留可读字段，缺失字段用默认
            step.Migrate(json);
            version = step.ToVersion;
            json["Version"] = version;
        }

        _current = json.Deserialize<AppConfig>() ?? new AppConfig();
        if (version != AppConfig.CurrentVersion)
        {
            _current.Version = AppConfig.CurrentVersion;
            Save(_current);
        }
        return _current;
    }

    public void Save(AppConfig? config = null)
    {
        _current = config ?? _current ?? new AppConfig();
        _current.Version = AppConfig.CurrentVersion;
        File.WriteAllText(_filePath, JsonSerializer.Serialize(_current, JsonOptions));
    }
}
