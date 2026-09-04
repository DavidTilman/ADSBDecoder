using System;
using System.Collections.Generic;
using System.Text;

namespace ADSBDecoder;

internal static class ChunkConverter
{
    public static ushort[] Magnitudes(byte[] raw)
    {
        ushort[] magnitudes = new ushort[raw.Length / 2];

        for (int i = 0; i < magnitudes.Length; i++)
        {
            // 127 is 0 from the radio
            int i_sample = raw[2*i] - 127;
            int q_sample = raw[(2*i)+1] - 127;
            magnitudes[i] = (ushort) Math.Sqrt((i_sample * i_sample) + (q_sample * q_sample));
        }

        return magnitudes;
    }
}
