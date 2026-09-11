using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.UI.Xaml;

namespace AircraftRadar;

/// <summary>
/// Drives the radar display. Everything here runs on the UI thread: the radio thread updates
/// the registry at full rate, and <see cref="Refresh"/> samples it on a timer.
/// </summary>
public sealed partial class RadarViewModel : ObservableObject
{
    private readonly RadioService radio;
    private readonly DispatcherTimer timer;

    /// <summary>Lets us find an existing view model by ICAO without scanning the collection.</summary>
    private readonly Dictionary<uint, AircraftViewModel> byIcao = [];

    /// <summary>Scratch buffer for the sweep that finds pruned aircraft; reused to avoid churn.</summary>
    private readonly List<uint> stale = [];

    public RadarViewModel(RadioService radio)
    {
        this.radio = radio;

        this.timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        this.timer.Tick += (_, _) => this.Refresh();

        this.StatusText = this.DescribeRadio();
    }

    public Projection Projection { get; } = new();

    public ObservableCollection<AircraftViewModel> Aircraft { get; } = [];

    [ObservableProperty] public partial int TrackedCount { get; set; }
    [ObservableProperty] public partial int VisibleCount { get; set; }
    [ObservableProperty] public partial string StatusText { get; set; }

    public void Start() => this.timer.Start();

    public void Stop() => this.timer.Stop();

    /// <summary>
    /// Syncs the observable collection against the registry and updates every view model in
    /// place. The collection is added to and removed from, never cleared and rebuilt, so the
    /// containers and their visuals survive from frame to frame.
    /// </summary>
    public void Refresh()
    {
        List<AircraftState> states = this.radio.Snapshot();
        int visible = 0;

        foreach (AircraftState state in states)
        {
            if (!this.byIcao.TryGetValue(state.Icao, out AircraftViewModel? vm))
            {
                vm = new AircraftViewModel(state.Icao);
                this.byIcao.Add(state.Icao, vm);
                this.Aircraft.Add(vm);
            }

            vm.Update(state, this.Projection);

            if (vm.HasPosition)
                visible++;
        }

        this.RemovePruned(states);

        this.TrackedCount = this.byIcao.Count;
        this.VisibleCount = visible;
        this.StatusText = this.DescribeRadio();

    }

    private void RemovePruned(List<AircraftState> states)
    {
        if (states.Count == this.byIcao.Count)
            return;     // nothing was pruned, so there is nothing to sweep for

        this.stale.Clear();

        foreach (uint icao in this.byIcao.Keys)
        {
            bool present = false;
            foreach (AircraftState state in states)
            {
                if (state.Icao == icao)
                {
                    present = true;
                    break;
                }
            }

            if (!present)
                this.stale.Add(icao);
        }

        foreach (uint icao in this.stale)
        {
            if (this.byIcao.Remove(icao, out AircraftViewModel? vm))
                this.Aircraft.Remove(vm);
        }
    }

    private string DescribeRadio() => this.radio.Status switch
    {
        RadioStatus.Stopped => "STOPPED",
        RadioStatus.NoDevice => "NO RTL-SDR DEVICE",
        RadioStatus.Starting => "STARTING",
        RadioStatus.Receiving => $"RECEIVING  ·  {this.radio.MessageCount} msgs",
        RadioStatus.Failed => $"RADIO FAILED: {this.radio.FailureMessage}",
        _ => "UNKNOWN"
    };
}
