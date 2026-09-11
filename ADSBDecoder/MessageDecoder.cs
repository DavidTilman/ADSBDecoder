using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Runtime.ConstrainedExecution;
using System.Text;

namespace ADSBDecoder;

public interface IMessageDecoder
{
    IEnumerable<byte> TypeCodes { get; }
    AdsbMessage Decode(in Frame frame);
}

public sealed class MessageDecoder
{
    private readonly IMessageDecoder?[] byTypeCode = new IMessageDecoder?[32];

    public MessageDecoder(IEnumerable<IMessageDecoder> decoders)
    {
        foreach (IMessageDecoder d in decoders)
            foreach (byte tc in d.TypeCodes)
                byTypeCode[tc] = d;
    }

    public AdsbMessage? Decode(in Frame frame)
        => byTypeCode[frame.TypeCode]?.Decode(frame);
}

public sealed class IdentificationDecoder : IMessageDecoder
{
    private const string Table =
        "#ABCDEFGHIJKLMNOPQRSTUVWXYZ#####_###############0123456789######";

    public IEnumerable<byte> TypeCodes => new byte[] { 1, 2, 3, 4 };

    public AdsbMessage Decode(in Frame f)
    {
        Span<char> chars = stackalloc char[8];
        for (int i = 0; i < 8; i++)
            chars[i] = Table[(int) ((f.Me >> (42 - (6 * i))) & 0x3F)];

        return new Identification(
            f.IcaoAddress,
            (byte) ((f.Me >> 48) & 0x7),
            new string(chars).TrimEnd('_'));
    }
}

public sealed class AirbornePositionDecoder : IMessageDecoder
{
    public IEnumerable<byte> TypeCodes => new byte[] { 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 20, 21, 22 };

    public AdsbMessage Decode(in Frame f)
    {
        int encoded_altitude = (int)((f.Me >> 36) & 0xFFF);
        bool odd = ((f.Me >> 34) & 0x1) == 1;
        uint lat_cpr = (uint)((f.Me >> 17) & 0x1FFFF);
        uint lon_cpr = (uint) (f.Me & 0x1FFFF);

        int? altitudeFeet = null;
        if (f.TypeCode is >= 9 and <= 18)
        {
            // barometric
            bool q = (encoded_altitude & 0x10) != 0;
            if (q)
            {
                int n = ((encoded_altitude >> 5) << 4) | (encoded_altitude & 0xF);
                altitudeFeet = q ? (n * 25) - 1000 : null;
            }
        } 
        else
        {
            altitudeFeet = f.TypeCode is >= 20 and <= 22
                ? (int) Math.Round(encoded_altitude * 3.28084)
                : throw new ArgumentException("Invalid TypeCode");
        }

        return new AirbornePosition(
            f.IcaoAddress, altitudeFeet, odd, lat_cpr / Math.Pow(2,17), lon_cpr / Math.Pow(2,17));
    }
}

public sealed class AirbourneVelocityDecoder : IMessageDecoder
{
    public IEnumerable<byte> TypeCodes => new byte[] { 19 };

    public AdsbMessage Decode(in Frame f)
    {
        byte subtype = (byte) ((f.Me >> 48) & 0x7);

        // Subtypes 3-4 carry airspeed and heading instead; not handled.
        if (subtype is not (1 or 2))
            return new AirborneVelocity(f.IcaoAddress, null, null, null);

        bool westward = ((f.Me >> 42) & 0x1) != 0;
        int rawEw = (int) ((f.Me >> 32) & 0x3FF);
        bool southward = ((f.Me >> 31) & 0x1) != 0;
        int rawNs = (int) ((f.Me >> 21) & 0x3FF);

        bool descending = ((f.Me >> 19) & 0x1) != 0;
        int rawVr = (int) ((f.Me >> 10) & 0x1FF);

        // A raw value of 0 means "not available"; otherwise subtract 1.
        if (rawEw == 0 || rawNs == 0)
            return new AirborneVelocity(f.IcaoAddress, null, null, null);

        int ew = (rawEw - 1) * (westward ? -1 : 1);
        int ns = (rawNs - 1) * (southward ? -1 : 1);

        // Supersonic subtype encodes in 4 kt units.
        if (subtype == 2)
        { ew *= 4; ns *= 4; }

        int groundSpeed = (int) Math.Round(Math.Sqrt((ew * ew) + (ns * ns)));
        // atan2(east, north) gives the bearing clockwise from north in (-180, 180]; wrap to [0, 360).
        double heading = ((Math.Atan2(ew, ns) * 180.0 / Math.PI) + 360.0) % 360.0;
        int? verticalRate = rawVr == 0 ? null : (rawVr - 1) * 64 * (descending ? -1 : 1);

        return new AirborneVelocity(f.IcaoAddress, groundSpeed, heading, verticalRate);
    }
}