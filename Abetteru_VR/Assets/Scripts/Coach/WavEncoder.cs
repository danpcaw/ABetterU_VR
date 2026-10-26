using System;

namespace ABetterU.Coach
{
    /// Encodes float samples (-1..1) as a RIFF/WAVE file: mono, 16-bit integer PCM.
    public static class WavEncoder
    {
        public static byte[] FromSamples(float[] samples, int sampleRate)
        {
            int dataBytes = samples.Length * 2;
            var wav = new byte[44 + dataBytes];

            void Ascii(int offset, string s) { for (int i = 0; i < s.Length; i++) wav[offset + i] = (byte)s[i]; }
            void U32(int offset, int v) => BitConverter.GetBytes(v).CopyTo(wav, offset);
            void U16(int offset, short v) => BitConverter.GetBytes(v).CopyTo(wav, offset);

            Ascii(0, "RIFF");  U32(4, 36 + dataBytes);  Ascii(8, "WAVE");
            Ascii(12, "fmt "); U32(16, 16);  U16(20, 1);  U16(22, 1);   // PCM, mono
            U32(24, sampleRate);  U32(28, sampleRate * 2);  U16(32, 2);  U16(34, 16);
            Ascii(36, "data"); U32(40, dataBytes);

            for (int i = 0; i < samples.Length; i++)
            {
                short s = (short)(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
                wav[44 + i * 2] = (byte)(s & 0xFF);
                wav[45 + i * 2] = (byte)((s >> 8) & 0xFF);
            }
            return wav;
        }
    }
}
