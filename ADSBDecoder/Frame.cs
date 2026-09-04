using System;
using System.Collections.Generic;
using System.Text;

namespace ADSBDecoder;

public readonly struct Frame
{
    public byte DownlinkFormat { get; init; }
    public byte Capability { get; init; }
    public uint IcaoAddress { get; init; }
    public byte TypeCode { get; init; }
    public ulong Me { get; init; }   // 56 bits, raw
}