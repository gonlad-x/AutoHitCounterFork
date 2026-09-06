//

using System.ComponentModel;
using System.Runtime.CompilerServices;
using AutoHitCounter.Enums;

namespace AutoHitCounter.Models;

public class SplitEntry : INotifyPropertyChanged
{
    private uint? _eventId;

    public uint? EventId
    {
        get => _eventId;
        set
        {
            _eventId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAuto));
        }
    }

    private string _name;

    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Label));
        }
    }

    private string _displayName;

    public string DisplayName
    {
        get => _displayName;
        set
        {
            _displayName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Label));
        }
    }
    public int PersonalBest { get; set; }
    public SplitType Type { get; set; } = SplitType.Child;
    public string GroupId { get; set; }
    public string Notes { get; set; }

    public long? BossKillTimeBestMs { get; set; }

    // Per-split override for the global "Sub-split on boss phases" setting.
    // Inherit follows the game's global flag; On/Off force it for this boss.
    public PhaseSubSplitMode PhaseSubSplitMode { get; set; } = PhaseSubSplitMode.Inherit;

    public string Label => DisplayName ?? Name;
    public bool IsAuto => EventId.HasValue;

    public event PropertyChangedEventHandler PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}