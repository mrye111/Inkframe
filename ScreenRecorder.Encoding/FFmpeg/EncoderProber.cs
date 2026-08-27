using System.Diagnostics;
using System.Text.Json;

namespace ScreenRecorder.Encoding.FFmpeg;

/// <summary>
/// 编码器运行时探测（#3 Spike 实证：-h 探测是假阳性，必须真实试编码）+ 机器指纹缓存（#6 决策）。
/// 回退链：NVENC → QSV → AMF → libopenh264（LGPL 分发版的软编兜底）。
/// </summary>
public sealed class EncoderProber
{
    // 回退链：硬编三家 → openh264（LGPL 分发版的软编兜底）→ libx264（仅开发机 full 构建存在，发布版探不到自然跳过）
    public static readonly IReadOnlyList<string> FallbackChain = ["h264_nvenc", "h264_qsv", "h264_amf", "libopenh264", "libx264"];

    private readonly string _ffmpegPath;
    private readonly string _cacheFile;
    private readonly Func<string, string, int> _probeRunner;   // (exe, args) => exitCode，测试可注入
    private readonly Func<string> _fingerprintProvider;

    public EncoderProber(
        string ffmpegPath,
        string? cacheFile = null,
        Func<string, string, int>? probeRunner = null,
        Func<string>? fingerprintProvider = null)
    {
        _ffmpegPath = ffmpegPath;
        _cacheFile = cacheFile ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Inkframe", "encoder-cache.json");
        _probeRunner = probeRunner ?? DefaultProbeRunner;
        _fingerprintProvider = fingerprintProvider ?? (() => MachineFingerprint.Current);
    }

    /// <summary>按回退链选出第一个运行时真正可用的编码器，结果按机器指纹缓存。</summary>
    public string Probe()
    {
        var fingerprint = _fingerprintProvider();
        var cached = ReadCache(fingerprint);
        if (cached is not null) return cached;

        foreach (var encoder in FallbackChain)
        {
            // 0.1s 真实试编码：帮助存在 ≠ 运行可用（NVENC 驱动版本 / QSV 运行时都可能缺席）
            var exit = _probeRunner(_ffmpegPath,
                "-hide_banner -loglevel error -f lavfi -i testsrc2=size=64x64:duration=0.1:rate=10 -c:v " + encoder + " -f null -");
            if (exit == 0)
            {
                WriteCache(fingerprint, encoder);
                return encoder;
            }
        }
        throw new InvalidOperationException("回退链全部失败：无可用 H.264 编码器（含 libopenh264 软编兜底）");
    }

    private static int DefaultProbeRunner(string exe, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = false,
            RedirectStandardError = false   // 不重定向：重定向而不消费会管道死锁（Spike 实证）
        });
        if (p is null) return -1;
        if (!p.WaitForExit(15000)) { try { p.Kill(); } catch { } return -1; }
        return p.ExitCode;
    }

    private string? ReadCache(string fingerprint)
    {
        try
        {
            if (!File.Exists(_cacheFile)) return null;
            var doc = JsonDocument.Parse(File.ReadAllText(_cacheFile));
            if (doc.RootElement.TryGetProperty(fingerprint, out var node))
                return node.GetString();
        }
        catch { /* 缓存损坏视为未命中 */ }
        return null;
    }

    private void WriteCache(string fingerprint, string encoder)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cacheFile)!);
            var dict = new Dictionary<string, string>();
            if (File.Exists(_cacheFile))
            {
                var existing = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_cacheFile));
                if (existing is not null) dict = existing;
            }
            dict[fingerprint] = encoder;
            File.WriteAllText(_cacheFile, JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* 缓存写失败不阻塞录制 */ }
    }
}
