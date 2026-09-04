# ADSBDecoder

A from-scratch ADS-B receiver for .NET. It drives an RTL-SDR dongle directly over
`librtlsdr`, turns the raw IQ stream into magnitudes, finds Mode S preambles,
demodulates and CRC-checks 112-bit DF17 frames, decodes the payload, and paints a
live aircraft table in the terminal.

No `dump1090`, no decoding libraries — everything from the USB read down to the
CRC-24 polynomial is implemented in this repo.

```
ICAO    CALLSIGN     ALT   SPD   HDG     V/S   AGE
--------------------------------------------------
4CA2D3  RYR7GX     37000   441   112°    -64    2s
40631C  EZY83NM    28950   398    87°   1216    0s
A1B2C3  —           2175   163     —       —    9s

3 aircraft tracked
```

## Requirements

**Hardware**

- An RTL-SDR dongle (RTL2832U based). Anything that tunes 1090 MHz will do.
- An antenna. A quarter-wave whip for 1090 MHz is ~69 mm — the stock telescopic
  antenna collapsed down to roughly thumb length works surprisingly well.

**Software**

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later.
- `librtlsdr` native binaries on the library search path (see below).
- Windows: the dongle must be bound to the WinUSB driver via
  [Zadig](https://zadig.akeo.ie/), otherwise `rtlsdr_open` will fail.

### Native library setup

`RtlSdr.cs` P/Invokes into a native library named `rtlsdr`, so the runtime looks
for `rtlsdr.dll` (Windows), `librtlsdr.so` (Linux) or `librtlsdr.dylib` (macOS).

On Windows, grab a prebuilt release from
[librtlsdr](https://github.com/librtlsdr/librtlsdr/releases) or
[osmocom rtl-sdr](https://ftp.osmocom.org/binaries/windows/rtl-sdr/) and drop
these next to the built executable (`ADSBDecoder/bin/Debug/net10.0/`):

- `rtlsdr.dll`
- `pthreadVC2.dll`
- `msvcr100.dll`

Use the 64-bit build — mixing a 32-bit DLL into a 64-bit process gives a
`BadImageFormatException`.

On Linux, installing `librtlsdr` from your package manager (plus a udev rule so
the device opens without root) is enough.

## Build and run

```sh
dotnet build ADSBDecoder.slnx
dotnet run --project ADSBDecoder
```

The program opens device index 0, tunes it, and loops forever. `Ctrl+C` to quit.
If it prints `No RTL-SDR found`, the dongle is not enumerating — check the driver
binding before anything else.

## How it works

### 1. Capture — `Radio.cs`, `RtlSdr.cs`

`RtlSdr` is a thin `DllImport` surface over `librtlsdr`. `Radio` configures the
dongle once in its constructor:

| Setting | Value | Why |
|---|---|---|
| Sample rate | 2 MS/s | ADS-B is 1 Mbit/s PPM, so 2 MS/s gives exactly 2 samples per symbol |
| Centre frequency | 1090 MHz | The ADS-B downlink |
| Gain mode | manual | AGC would ride over the short bursts |
| Gain | 49.6 dB | Near max; these are weak, bursty signals |
| AGC | off | Same reason |

`rtlsdr_reset_buffer` is called before the first read — without it the driver
hands back stale samples. `ReadChunk` then does a blocking `rtlsdr_read_sync`
into a 262144-byte buffer (131072 IQ pairs, ~65 ms of air time per chunk).

### 2. IQ to magnitude — `ChunkConverter.cs`

The dongle returns unsigned 8-bit offset-binary interleaved I/Q, where 127 is
zero. Each pair becomes one magnitude sample:

```
mag[i] = sqrt((I - 127)² + (Q - 127)²)
```

ADS-B is pulse position modulated on an unmodulated carrier, so phase carries no
information — the envelope is all the decoder needs.

### 3. Frame sync and demodulation — `FrameDecoder.cs`

A Mode S extended squitter is an 8 µs preamble followed by 112 bits at 1 Mbit/s.
At 2 samples per microsecond that is a **240 sample** window: 16 preamble samples
plus 224 data samples.

`ScanChunk` slides a 240-sample window over the whole chunk one sample at a time.
For each position:

- **`IsPreamble`** checks the fixed pulse pattern. Samples 0, 2, 7 and 9 should
  be high; their mean must clear a floor of 20 to reject noise. Samples 1, 3, 4,
  5, 6 and 8 must all sit below half that mean. This shape test is cheap and
  discards the overwhelming majority of window positions immediately.
- **`DemodulateByte`** reads the 224 data samples as 112 PPM bits. Each bit is a
  sample pair: energy in the first half is a `1`, energy in the second half is a
  `0`.
- On a successful decode the scan skips forward a full frame length instead of
  re-triggering inside the frame it just read.

### 4. CRC and frame parsing — `FrameDecoder.TryDecodeFrame`

Only **DF 17** (ADS-B extended squitter from a Mode S transponder) is accepted;
everything else is dropped at the first check.

`ModeSCrc` runs the Mode S CRC-24 with generator polynomial `0x1FFF409` over the
first 88 bits and compares against the trailing 24-bit parity. A non-zero
remainder rejects the frame — there is **no error correction**, so a single bad
bit costs the whole message. That strictness is what makes the loose preamble
threshold safe: noise that survives the shape test almost never survives the CRC.

A surviving frame is unpacked into `Frame`:

| Field | Bits | Meaning |
|---|---|---|
| `DownlinkFormat` | 1–5 | Always 17 here |
| `Capability` | 6–8 | Transponder capability |
| `IcaoAddress` | 9–32 | The aircraft's unique 24-bit address |
| `TypeCode` | 33–37 | What kind of message the payload is |
| `Me` | 33–88 | The 56-bit payload, kept raw |

### 5. Payload decoding — `MessageDecoder.cs`, `Message.cs`

`MessageDecoder` is a 32-entry lookup table from type code to an
`IMessageDecoder`. Each decoder declares the type codes it owns, so adding a
message type means writing one class and registering it — there is no switch to
extend.

| Decoder | Type codes | Produces |
|---|---|---|
| `IdentificationDecoder` | 1–4 | Callsign (eight 6-bit characters from the ADS-B charset) and emitter category |
| `AirbornePositionDecoder` | 9–18, 20–22 | Altitude, CPR odd/even flag, raw 17-bit CPR latitude and longitude |
| `AirbourneVelocityDecoder` | 19 | Ground speed, heading and vertical rate from the E/W and N/S velocity components |

Altitude follows the spec's two cases: type codes 9–18 are barometric, where the
Q bit selects 25 ft encoding, and 20–22 are GNSS height in metres, converted to
feet.

Velocity subtypes 1 and 2 (ground speed) are decoded, with subtype 2 scaling by
4 kt per count for supersonic. A raw component of 0 means "not available" and
yields `null` rather than a fabricated zero.

Each decoder returns a `record` — `Identification`, `AirbornePosition`,
`AirborneVelocity` — all deriving from `AdsbMessage`.

### 6. Aircraft tracking — `Aircraft.cs`

`AircraftRegistry` keys aircraft by ICAO address, creating an `Aircraft` on first
sight and folding each subsequent message into it. Anything not heard from for
60 seconds is dropped.

`ToString` renders the whole table with ANSI erase sequences (`\x1b[K`,
`\x1b[0J`), so `Program` can park the cursor at 0,0 and overwrite in place — no
flicker, no full clear.

## Project layout

```
ADSBDecoder/
├── Program.cs           Main loop: read chunk → magnitudes → scan → render
├── Radio.cs             RTL-SDR lifecycle and tuning
├── RtlSdr.cs            P/Invoke declarations for librtlsdr
├── ChunkConverter.cs    Interleaved IQ bytes → magnitude samples
├── FrameDecoder.cs      Preamble detection, PPM demodulation, Mode S CRC-24
├── Frame.cs             Parsed DF17 frame header + raw 56-bit payload
├── MessageDecoder.cs    Type-code dispatch table and the payload decoders
├── Message.cs           Decoded message record types
└── Aircraft.cs          Per-aircraft state and the terminal table
```

## Current limitations

- **No position fix.** CPR latitude/longitude are decoded and stored raw, but the
  odd/even CPR pair is never solved into real coordinates, so no lat/lon is
  displayed yet.
- **DF17 only.** No DF11 acquisition squitters, no DF20/21 Comm-B, no TIS-B.
- **No error correction.** Frames with a single-bit error are discarded rather
  than repaired, which costs some range compared to `dump1090`.
- **Velocity subtypes 3–4** (airspeed and heading instead of ground velocity)
  decode to nulls.
- The scan is a naive single-sample slide over every window in the chunk. It
  keeps up at 2 MS/s on a modern CPU, but it is the obvious place to optimise.

## Legal note

ADS-B is an unencrypted broadcast intended for public reception, and receiving it
is legal in most jurisdictions. This project only ever receives — it never
transmits.
