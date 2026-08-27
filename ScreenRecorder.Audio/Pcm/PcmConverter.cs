namespace ScreenRecorder.Audio.Pcm;

/// <summary>PCM 格式换算：WASAPI 原生 float32 → s16le（ffmpeg 管道输入格式）。</summary>
public static class PcmConverter
{
    /// <summary>float32 交错样本 → s16le 字节流，削波保护。</summary>
    public static byte[] Float32ToS16(byte[] floatBytes)
    {
        var sampleCount = floatBytes.Length / 4;
        var output = new byte[sampleCount * 2];
        for (var i = 0; i < sampleCount; i++)
        {
            var f = BitConverter.ToSingle(floatBytes, i * 4);
            var clamped = Math.Clamp(f, -1f, 1f);
            var s = (short)(clamped * short.MaxValue);
            output[i * 2] = (byte)(s & 0xFF);
            output[i * 2 + 1] = (byte)(s >> 8);
        }
        return output;
    }
}
