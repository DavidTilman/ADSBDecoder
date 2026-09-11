using System;
using System.Collections.Generic;
using System.Threading;

using ADSBDecoder;

namespace AircraftRadar;

/// <summary>An immutable copy of one aircraft's state, taken under the registry lock.</summary>
public readonly record struct AircraftState(
    uint Icao,
    string? Callsign,
    int? Altitude,
    int? GroundSpeed,
    double? Heading,
    int? VerticalRate,
    double? Lat,
    double? Lon,
    DateTime LastSeen);

public enum RadioStatus
{
    Stopped,
    NoDevice,
    Starting,
    Receiving,
    Failed
}

/// <summary>
/// Owns the RTL-SDR capture thread and the <see cref="AircraftRegistry"/> it feeds.
///
/// The registry is a plain <c>List&lt;Aircraft&gt;</c> that ConsumeMessage both adds to and
/// removes from, so it is not safe to read while the radio thread is writing. Rather than
/// change the decoder library, this service is made the sole owner of the registry: every
/// mutation and every read goes through <see cref="gate"/>. The UI never sees an Aircraft
/// instance at all, only the value snapshots produced by <see cref="Snapshot"/>.
/// </summary>
public sealed class RadioService
{
    /// <summary>256 KiB of interleaved I/Q at 2 MS/s is about 65 ms of signal per read.</summary>
    private const uint ChunkBytes = 262_144;

    private readonly AircraftRegistry registry = new();
    private readonly object gate = new();

    private Thread? captureThread;
    private volatile bool running;
    private volatile RadioStatus status = RadioStatus.Stopped;
    private volatile string? failureMessage;
    private long messageCount;

    public RadioStatus Status => this.status;

    /// <summary>Set when the capture thread fails, so the overlay can say why.</summary>
    public string? FailureMessage => this.failureMessage;

    public long MessageCount => Interlocked.Read(ref this.messageCount);

    public void Start()
    {
        if (this.captureThread is not null)
            return;

        this.running = true;
        this.status = RadioStatus.Starting;

        this.captureThread = new Thread(this.CaptureLoop)
        {
            IsBackground = true,
            Name = "ADS-B capture",
            Priority = ThreadPriority.AboveNormal
        };
        this.captureThread.Start();
    }

    public void Stop() => this.running = false;

    /// <summary>
    /// Copies the current state of every tracked aircraft. Called on the UI thread; holds the
    /// lock only for the copy, so the radio thread is never blocked for longer than a few
    /// dozen field reads.
    /// </summary>
    public List<AircraftState> Snapshot()
    {
        lock (this.gate)
        {
            List<Aircraft> tracked = this.registry.Aircraft;
            List<AircraftState> states = new(tracked.Count);

            foreach (Aircraft a in tracked)
            {
                (double? lat, double? lon) = a.PositionVector;

                states.Add(new AircraftState(
                    a.Icao,
                    a.Callsign,
                    a.Altitude,
                    a.GroundSpeed,
                    a.Heading,
                    a.VerticalRate,
                    lat,
                    lon,
                    a.LastSeen));
            }

            return states;
        }
    }

    private void CaptureLoop()
    {
        // Nothing may escape this method: an unhandled exception on a background thread takes
        // the whole process down, and the P/Invokes below fail hard when the native rtlsdr
        // library or a device is missing.
        try
        {
            this.Capture();
        }
        catch (Exception ex)
        {
            this.failureMessage = ex.Message;
            this.status = RadioStatus.Failed;
        }
    }

    private void Capture()
    {
        if (!Radio.DeviceExists)
        {
            this.status = RadioStatus.NoDevice;
            return;
        }

        Radio radio = new(0, ChunkBytes);

        this.status = RadioStatus.Receiving;

        while (this.running)
        {
            if (!radio.ReadChunk() || radio.Chunk is null)
                continue;

            ushort[] magnitudes = ChunkConverter.Magnitudes(radio.Chunk);
            List<AdsbMessage> messages = FrameDecoder.ScanChunk(magnitudes);

            if (messages.Count == 0)
                continue;

            lock (this.gate)
            {
                foreach (AdsbMessage message in messages)
                    this.registry.ConsumeMessage(message);
            }

            Interlocked.Add(ref this.messageCount, messages.Count);
        }

        this.status = RadioStatus.Stopped;
    }
}
