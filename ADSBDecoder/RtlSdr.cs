using System;
using System.Collections.Generic;
using System.Text;

using System;
using System.Runtime.InteropServices;

internal static class RtlSdr
{
    private const string Lib = "rtlsdr";

    [DllImport(Lib)] public static extern uint rtlsdr_get_device_count();
    [DllImport(Lib)] public static extern int rtlsdr_open(out IntPtr dev, uint index);
    [DllImport(Lib)] public static extern int rtlsdr_close(IntPtr dev);
    [DllImport(Lib)] public static extern int rtlsdr_set_center_freq(IntPtr dev, uint freq);
    [DllImport(Lib)] public static extern int rtlsdr_set_sample_rate(IntPtr dev, uint rate);
    [DllImport(Lib)] public static extern int rtlsdr_set_tuner_gain_mode(IntPtr dev, int manual);
    [DllImport(Lib)] public static extern int rtlsdr_set_tuner_gain(IntPtr dev, int gainTenthsDb);
    [DllImport(Lib)] public static extern int rtlsdr_set_agc_mode(IntPtr dev, int on);
    [DllImport(Lib)] public static extern int rtlsdr_reset_buffer(IntPtr dev);

    [DllImport(Lib)]
    public static extern int rtlsdr_read_sync(IntPtr dev, byte[] buf, int len, out int nRead);
}