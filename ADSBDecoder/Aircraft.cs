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

        sb.Append($"{"ICAO",-6}  {"CALLSIGN",-8}  {"ALT",6}  {"SPD",4}  {"HDG",4}  {"V/S",6}  {"AGE",4}")
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
    public uint? LatCpr { get; private set; }
    public uint? LonCpr { get; private set;  }
    public int? GroundSpeed { get; private set;  }
    public double? Heading { get; private set;  }
    public int? VerticalRate { get; private set; }

    public DateTime LastSeen { get; private set;  }
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
                this.LatCpr = pos.LatCpr;
                this.LonCpr = pos.LonCpr;
                break;
            case AirborneVelocity vel:
                this.GroundSpeed = vel.GroundSpeed;
                this.Heading = vel.Heading;
                this.VerticalRate = vel.VerticalRate;
                break;
        }
    }
}
