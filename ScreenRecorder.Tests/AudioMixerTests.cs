using ScreenRecorder.Audio.Mixer;
using ScreenRecorder.Audio.Pcm;

namespace ScreenRecorder.Tests;

public sealed class AudioMixerTests
{
    private static byte[] ConstantChunk(short value)
    {
        var buf = new byte[AudioMixer.ChunkBytes];
        for (var i = 0; i < buf.Length; i += 2)
        {
            buf[i] = (byte)(value & 0xFF);
            buf[i + 1] = (byte)(value >> 8);
        }
        return buf;
    }

    [Fact]
    public void Silence_When_No_Input()
    {
        using var mixer = new AudioMixer();
        byte[]? chunk = null;
        mixer.ChunkMixed += (_, c) => chunk = c;

        mixer.Start();
        Thread.Sleep(60);   // 等 2-3 拍
        mixer.Stop();

        Assert.NotNull(chunk);
        Assert.Equal(AudioMixer.ChunkBytes, chunk!.Length);
        Assert.All(chunk, b => Assert.Equal(0, b));   // 静音填充（§48 音频时钟连续）
    }

    [Fact]
    public void Clipping_On_Overflow()
    {
        using var mixer = new AudioMixer();
        mixer.PushSystemAudio(ConstantChunk(30000));
        mixer.PushMicrophone(ConstantChunk(30000));

        // 收集所有拍：含数据的第一拍在队列耗尽后会被后续静音拍覆盖
        var chunks = new List<byte[]>();
        mixer.ChunkMixed += (_, c) => { lock (chunks) chunks.Add(c); };
        mixer.Start();
        Thread.Sleep(50);
        mixer.Stop();

        Assert.NotEmpty(chunks);
        var loud = chunks.First(c => c.Any(b => b != 0));
        var first = (short)(loud[0] | loud[1] << 8);
        Assert.Equal(short.MaxValue, first);   // 30000+30000 削波到 32767
    }

    [Fact]
    public void Zero_Volume_Mutes_Source()
    {
        using var mixer = new AudioMixer { SystemVolume = 1.0f, MicVolume = 0.0f };
        mixer.PushSystemAudio(ConstantChunk(1000));
        mixer.PushMicrophone(ConstantChunk(9000));

        var chunks = new List<byte[]>();
        mixer.ChunkMixed += (_, c) => { lock (chunks) chunks.Add(c); };
        mixer.Start();
        Thread.Sleep(50);
        mixer.Stop();

        var loud = chunks.First(c => c.Any(b => b != 0));
        var first = (short)(loud[0] | loud[1] << 8);
        Assert.Equal(1000, first);   // 只剩系统声
    }

    [Fact]
    public void Undersized_Input_Fills_Silence_Until_Enough()
    {
        using var mixer = new AudioMixer();
        mixer.PushSystemAudio(ConstantChunk(500)[..(AudioMixer.ChunkBytes / 2)]);   // 不足一拍

        byte[]? chunk = null;
        mixer.ChunkMixed += (_, c) => chunk = c;
        mixer.Start();
        Thread.Sleep(50);
        mixer.Stop();

        Assert.All(chunk!, b => Assert.Equal(0, b));   // 不满一拍 → 静音，数据留存下拍
    }
}

public sealed class PcmConverterTests
{
    [Fact]
    public void Float_To_S16_Mapping_And_Clamp()
    {
        var floats = new byte[16];
        BitConverter.GetBytes(0.0f).CopyTo(floats, 0);
        BitConverter.GetBytes(1.0f).CopyTo(floats, 4);
        BitConverter.GetBytes(-1.0f).CopyTo(floats, 8);
        BitConverter.GetBytes(2.5f).CopyTo(floats, 12);   // 超界 → 削波

        var s16 = PcmConverter.Float32ToS16(floats);

        Assert.Equal(0, BitConverter.ToInt16(s16, 0));
        Assert.Equal(short.MaxValue, BitConverter.ToInt16(s16, 2));
        Assert.Equal(-32767, BitConverter.ToInt16(s16, 4));   // -1.0 * 32767 = -32767
        Assert.Equal(short.MaxValue, BitConverter.ToInt16(s16, 6));   // clamp
    }
}
