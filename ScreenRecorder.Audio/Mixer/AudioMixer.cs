namespace ScreenRecorder.Audio.Mixer;

/// <summary>
/// 双轨混音器（§18）：系统声 + 麦克风 → 单一 s16le/48kHz/立体声输出。
/// - 20ms 定时输出固定块（3840 字节），源缺席时填静音——音频时钟连续是音画同步（§48）的前提
/// - 独立音量（§16/§17）、相加混音 + 削波保护
/// - 源断开（§54）后自动按静音处理，录制不中断
/// </summary>
public sealed class AudioMixer : IDisposable
{
    public const int SampleRate = 48000;
    public const int Channels = 2;
    public const int BytesPerSample = 2;
    public const int TickMs = 20;
    public const int ChunkBytes = SampleRate * Channels * BytesPerSample * TickMs / 1000;   // 3840

    /// <summary>每源缓冲上限：2 秒音频。超出丢最旧（下游不消费时防内存膨胀——E2E 实证 Sum 溢出崩溃）。</summary>
    private const int MaxBufferedBytesPerSource = ChunkBytes * 100;

    private readonly Queue<byte[]> _systemQueue = new();
    private readonly Queue<byte[]> _micQueue = new();
    private int _systemPending, _micPending;   // 水位计数，替代每拍 O(n) Sum
    private readonly object _gate = new();
    private readonly Timer _timer;

    public float SystemVolume { get; set; } = 1.0f;
    public float MicVolume { get; set; } = 1.0f;

    /// <summary>当前输出电平 0..1（RMS，20ms 粒度平滑，§35 实时音量反馈用）。</summary>
    public float CurrentLevel { get; private set; }

    /// <summary>每 20ms 产出一块混合 PCM（s16le 48k stereo，3840B）。</summary>
    public event EventHandler<byte[]>? ChunkMixed;

    /// <summary>每拍电平通知（0..1 RMS）。</summary>
    public event EventHandler<float>? LevelChanged;

    public AudioMixer()
    {
        _timer = new Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start() => _timer.Change(0, TickMs);
    public void Stop() => _timer.Change(Timeout.Infinite, Timeout.Infinite);

    public void PushSystemAudio(byte[] s16Chunk)
    {
        lock (_gate)
        {
            _systemQueue.Enqueue(s16Chunk);
            _systemPending += s16Chunk.Length;
            while (_systemPending > MaxBufferedBytesPerSource)
                _systemPending -= _systemQueue.Dequeue().Length;   // 丢最旧保时钟连续
        }
    }

    public void PushMicrophone(byte[] s16Chunk)
    {
        lock (_gate)
        {
            _micQueue.Enqueue(s16Chunk);
            _micPending += s16Chunk.Length;
            while (_micPending > MaxBufferedBytesPerSource)
                _micPending -= _micQueue.Dequeue().Length;
        }
    }

    private void Tick()
    {
        byte[]? sys, mic;
        lock (_gate)
        {
            sys = DequeueAligned(_systemQueue, ref _systemPending);
            mic = DequeueAligned(_micQueue, ref _micPending);
        }

        var mixed = new byte[ChunkBytes];
        MixInto(mixed, sys, SystemVolume);
        MixInto(mixed, mic, MicVolume);
        CurrentLevel = ComputeRms(mixed);
        LevelChanged?.Invoke(this, CurrentLevel);
        ChunkMixed?.Invoke(this, mixed);
    }

    /// <summary>从队列取满 ChunkBytes 的数据；不足返回 null（本拍按静音），多余留存下拍。</summary>
    private static byte[]? DequeueAligned(Queue<byte[]> queue, ref int pending)
    {
        if (pending < ChunkBytes) return null;
        var result = new byte[ChunkBytes];
        var offset = 0;
        while (offset < ChunkBytes)
        {
            var head = queue.Dequeue();
            pending -= head.Length;
            var take = Math.Min(head.Length, ChunkBytes - offset);
            Buffer.BlockCopy(head, 0, result, offset, take);
            offset += take;
            if (take < head.Length)
            {
                var rest = head[take..];
                queue.Enqueue(rest);           // 余量回队尾（20ms 粒度下顺序影响可忽略）
                pending += rest.Length;
            }
        }
        return result;
    }

    private static void MixInto(byte[] target, byte[]? source, float volume)
    {
        if (source is null || volume <= 0f) return;
        for (var i = 0; i < ChunkBytes; i += 2)
        {
            var existing = (short)(target[i] | target[i + 1] << 8);
            var incoming = (short)(source[i] | source[i + 1] << 8);
            var sum = (int)(existing + incoming * volume);
            var clamped = Math.Clamp(sum, short.MinValue, short.MaxValue);
            target[i] = (byte)(clamped & 0xFF);
            target[i + 1] = (byte)(clamped >> 8);
        }
    }

    private static float ComputeRms(byte[] s16)
    {
        double sum = 0;
        for (var i = 0; i < s16.Length; i += 2)
        {
            var s = (short)(s16[i] | s16[i + 1] << 8) / 32768.0;
            sum += s * s;
        }
        var rms = Math.Sqrt(sum / (s16.Length / 2));
        // 感知映射：RMS 偏小，开方提升低电平可见度
        return (float)Math.Min(1.0, Math.Sqrt(rms) * 1.6);
    }

    public void Dispose() => _timer.Dispose();
}
