using System;
using System.Collections.Generic;
using System.Text;

namespace ADSBDecoder;

internal class AircraftRegistry
{
    public List<Aircraft> Aircraft { get; private set; }

    public AircraftRegistry() { 
        this.Aircraft = []; 
    }

    public void ConsumeMessage(AdsbMessage message)
    {
        Aircraft? match = this.Aircraft.Find(x => x.Icao == message.Icao);

        if (match is null)
        {
            match = new Aircraft(message.Icao);
            this.Aircraft.Add(match);
        }

        match.ConsumeMessage(message);

        DateTime cutoff = DateTime.UtcNow - new TimeSpan(0,1,0);
        this.Aircraft.RemoveAll(a => a.LastSeen < cutoff);
    }

    public override string ToString()
    {
        const string ClearLine = "\x1b[K";    // erase to end of line
        const string ClearBelow = "\x1b[0J";   // erase everything below the cursor

        StringBuilder sb = new StringBuilder();

        sb.Append($"{"ICAO",-6}  {"CALLSIGN",-8}  {"ALT",6}  {"SPD",4}  {"HDG",4}  {"LAT",8}  {"LON",8}  {"V/S",6}  {"AGE",4}")
          .AppendLine(ClearLine);
        sb.Append(new string('-', 50)).AppendLine(ClearLine);

        DateTime now = DateTime.UtcNow;

        foreach (Aircraft a in this.Aircraft.OrderByDescending(x => x.Altitude ?? -1))
        {
            int age = (int) (now - a.LastSeen).TotalSeconds;

            sb.Append($"{a.Icao,-6:X6}  ")
              .Append($"{a.Callsign ?? "",-8}  ")
              .Append($"{Fmt(a.Altitude),6}  ")
              .Append($"{Fmt(a.GroundSpeed),4}  ")
              .Append($"{(a.Heading is null ? "—" : $"{a.Heading:0}°"),4}  ")
              .Append($"{(a.PositionVector.Lat is null ? "—" : $"{a.PositionVector.Lat:0.0000}"),8}  ")
              .Append($"{(a.PositionVector.Lon is null ? "—" : $"{a.PositionVector.Lon:0.0000}"),8}  ")
              .Append($"{Fmt(a.VerticalRate),6}  ")
              .Append($"{age + "s",4}")
              .AppendLine(ClearLine);
        }

        sb.AppendLine(ClearLine);
        sb.Append($"{this.Aircraft.Count} aircraft tracked").AppendLine(ClearLine);
        sb.Append(ClearBelow);

        return sb.ToString();

        static string Fmt(int? v)
        {
            return v?.ToString() ?? "—";
        }
    }
}
internal class Aircraft
{
    public uint Icao { get; private set; }
    public string? Callsign { get; private set; }
    public byte? Catagory { get; private set; }
    public int? Altitude { get; private set; }
    public int? GroundSpeed { get; private set; }
    public double? Heading { get; private set; }
    public int? VerticalRate { get; private set; }
    public AirbornePosition?[] PositionFrames = new AirbornePosition?[2] { null, null };
    public (double? Lat, double? Lon) PositionVector { get; private set; } = (null, null);
    public DateTime LastSeen { get; private set; }
    public Aircraft(uint icao)
    {
        this.Icao = icao;
    }

    public void ConsumeMessage(AdsbMessage adsbMessage)
    {
        this.LastSeen = DateTime.UtcNow;

        switch (adsbMessage)
        {
            case Identification id:
                this.Catagory = id.Category;
                this.Callsign = id.Callsign;
                break;
            case AirbornePosition pos:
                this.Altitude = pos.Altitude;
                this.PositionFrames[pos.Odd ? 1 : 0] = pos;
                this.PositionVector = this.DecodePosition();
                break;
            case AirborneVelocity vel:
                this.GroundSpeed = vel.GroundSpeed;
                this.Heading = vel.Heading;
                this.VerticalRate = vel.VerticalRate;
                break;
        }
    }

    private static double NL(double lat)
    {
        double a = Math.Abs(lat);

        if (a >= 90)
            return 1;

        if (a >= 87)
            return 2;

        return Math.Floor(2 * Math.PI /
            Math.Acos(1 - ((1 - Math.Cos(Math.PI / 30)) /
                          Math.Pow(Math.Cos(Math.PI / 180.0 * lat), 2))));
    }

    private static double Mod(double x, double y) => x - (y * Math.Floor(x / y));

    public (double? Lat, double? Lon) DecodePosition()
    {
        AirbornePosition? even = this.PositionFrames[0];
        AirbornePosition? odd = this.PositionFrames[1];

        if (even is null || odd is null)
            return (null, null);

        if ((even.Received - odd.Received).Duration() > TimeSpan.FromSeconds(10))
            return (null, null);

        bool oddIsRecent = odd.Received > even.Received;

        double latCprEven = even.LatCpr;
        double lonCprEven = even.LonCpr;
        double latCprOdd = odd.LatCpr;
        double lonCprOdd = odd.LonCpr;

        // latitude zone index
        double j = Math.Floor((59 * latCprEven) - (60 * latCprOdd) + 0.5);

        double latEven = 360.0 / 60.0 * (Mod(j, 60) + latCprEven);
        double latOdd = 360.0 / 59.0 * (Mod(j, 59) + latCprOdd);

        if (latEven >= 270)
            latEven -= 360;
        if (latOdd >= 270)
            latOdd -= 360;

        // if the frames fall in different longitude zones the pair is unusable
        if (NL(latEven) != NL(latOdd))
            return (null, null);

        double nl = NL(latEven);
        double m = Math.Floor((lonCprEven * (nl - 1)) - (lonCprOdd * nl) + 0.5);

        double lat, lon;
        if (oddIsRecent)
        {
            double ni = Math.Max(nl - 1, 1);
            lat = latOdd;
            lon = 360.0 / ni * (Mod(m, ni) + lonCprOdd);
        }
        else
        {
            double ni = Math.Max(nl, 1);
            lat = latEven;
            lon = 360.0 / ni * (Mod(m, ni) + lonCprEven);
        }

        if (lon >= 180)
            lon -= 360;

        return (lat, lon);
    }
}
