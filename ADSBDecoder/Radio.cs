using System;
using System.Collections.Generic;
using System.Text;

namespace ADSBDecoder;

internal class Radio
{
    private IntPtr Device;
    private readonly uint ChunkLength;

    public byte[]? Chunk { get; private set; } = null;
    public Radio(uint index, uint ChunkLength)
    {
        this.ChunkLength = ChunkLength;

        this.Open(index);

        RtlSdr.rtlsdr_set_sample_rate(Device, 2_000_000);
        RtlSdr.rtlsdr_set_center_freq(Device, 1_090_000_000);
        RtlSdr.rtlsdr_set_tuner_gain_mode(Device, 1);   // 1 = manual
        RtlSdr.rtlsdr_set_tuner_gain(Device, 496);      // tenths of a dB, so 49.6
        RtlSdr.rtlsdr_set_agc_mode(Device, 0);
        RtlSdr.rtlsdr_reset_buffer(Device);             // required before the first read
    }
    public static bool DeviceExists => RtlSdr.rtlsdr_get_device_count() > 0;
    public bool Open(uint index) => RtlSdr.rtlsdr_open(out Device, index) != 0;

    public bool ReadChunk()
    {
        this.Chunk = new byte[ChunkLength];

        RtlSdr.rtlsdr_read_sync(Device, this.Chunk, this.Chunk.Length, out int bytesRead);

        return bytesRead == ChunkLength;
    }
}
