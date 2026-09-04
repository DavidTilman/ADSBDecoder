using System;
using System.Collections.Generic;
using System.Text;

namespace ADSBDecoder;

internal class FrameDecoder
{
    const int FRAME_LENGTH = 240;

    static readonly AircraftRegistry aircraftRegistry = new AircraftRegistry();

    static readonly MessageDecoder messageDecoder = new MessageDecoder(
    [
        new IdentificationDecoder(),
        new AirbornePositionDecoder(),
        new AirbourneVelocityDecoder()
    ]);

    public static List<AdsbMessage> ScanChunk(ReadOnlySpan<ushort> mags)
    {
        List<AdsbMessage> messages = [];
        for (int i = 0; i <= mags.Length - FRAME_LENGTH; i++)
        {
            ReadOnlySpan<ushort> window = mags.Slice(i, FRAME_LENGTH);
            AdsbMessage? message = DecodeWindow(window);
            if (message is not null)
            {
                messages.Add(message);
                i += FRAME_LENGTH - 1;
            }
        }

        return messages;
    }

    public static AdsbMessage? DecodeWindow(ReadOnlySpan<ushort> window)
    {
        if (!IsPreamble(window))
            return null;

        ReadOnlySpan<ushort> data = window[16..];
        byte[] msg = new byte[14];
        for (int i = 0; i < msg.Length; i++)
        {
            msg[i] = DemodulateByte(data, i*16);
        }

        return TryDecodeFrame(msg, out Frame frame) ?  messageDecoder.Decode(frame) : null;

    }

    public static bool TryDecodeFrame(ReadOnlySpan<byte> msg, out Frame frame)
    {
        frame = default;

        if ((msg[0] >> 3) != 17)
            return false;      // DF 17 only
        if (ModeSCrc(msg) != 0)
            return false;      // must validate

        ulong me = 0;
        for (int i = 4; i <= 10; i++)
            me = (me << 8) | msg[i];

        frame = new Frame
        {
            DownlinkFormat = 17,
            Capability = (byte) (msg[0] & 0x7),
            IcaoAddress = (uint) ((msg[1] << 16) | (msg[2] << 8) | msg[3]),
            TypeCode = (byte) (msg[4] >> 3),
            Me = me
        };
        return true;
    }

    static uint ModeSCrc(ReadOnlySpan<byte> msg)
    {
        const uint G = 0x1FFF409;          // 25-bit generator polynomial

        Span<byte> b = stackalloc byte[14];
        msg.CopyTo(b);

        int nbits = msg.Length * 8;
        for (int i = 0; i < nbits - 24; i++)
        {
            if ((b[i >> 3] & (0x80 >> (i & 7))) == 0)
                continue;

            for (int k = 0; k < 25; k++)
            {
                if ((G & (1u << (24 - k))) == 0)
                    continue;
                int p = i + k;
                if (p < nbits)
                    b[p >> 3] ^= (byte) (0x80 >> (p & 7));
            }
        }

        int n = msg.Length;
        return (uint) ((b[n - 3] << 16) | (b[n - 2] << 8) | b[n - 1]);
    }

    public static bool IsPreamble(ReadOnlySpan<ushort> window) {
        // avg the mags that should be high in preamble
        int high = (window[0] + window[2] + window[7] + window[9]) / 4; 
        if (high< 20) return false; // threshold to account for weak signal

        int low = high / 2; // determine a low value that is different enough to the high bit

        // we have checked the high bits. now check the shape of the low bits
        return window[1] < low && window[3] < low && window[4] < low &&
                window[5] < low && window[6] < low && window[8] < low;
    }

    public static byte DemodulateByte(ReadOnlySpan<ushort> mags, int start) {
        byte data = 0;
        for (int b = 0; b < 8; b++) {
            data <<= 1;
            if (mags[start + (2 * b)] > mags[start + (2 * b) + 1]) data |= 1;
        }

        return data;
    }
}
