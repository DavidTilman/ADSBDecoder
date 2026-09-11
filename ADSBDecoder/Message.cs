using System;
using System.Collections.Generic;
using System.Data;
using System.Net.NetworkInformation;
using System.Text;

namespace ADSBDecoder;

public abstract record AdsbMessage(uint Icao, DateTime Received);

public sealed record Identification(uint Icao, byte Category, string Callsign)
    : AdsbMessage(Icao, DateTime.UtcNow)
{
    public override string ToString() => $"Identification {{ Icao: {this.Icao:X}, Catagory: {this.Category}, Callsign: {this.Callsign,-8}}}";
}

public sealed record AirbornePosition(uint Icao, int? Altitude, bool Odd, double LatCpr, double LonCpr)
    : AdsbMessage(Icao, DateTime.UtcNow)
{
    public override string ToString() => $"Position {{ Icao: {this.Icao:X}, Altitude: {this.Altitude}ft, Odd: {this.Odd,-5}, Lat: {this.LatCpr}, Lon: {this.LonCpr}}}";
}

public sealed record AirborneVelocity(uint Icao, int? GroundSpeed, double? Heading, int? VerticalRate)
    : AdsbMessage(Icao, DateTime.UtcNow);

