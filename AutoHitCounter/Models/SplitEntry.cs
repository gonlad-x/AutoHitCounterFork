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

    // Boss-timer start-trigger config (per split, persisted with the profile).
    // Default (both unset/false): the timer starts on the healthbar-spawn poll matching
    // this split's own EventId-derived boss entity, exactly as before this feature.
    //   - BossTimerStartEntityIds: start on one of *these* entities' healthbar spawn instead
    //     of the split's own flag-derived entities (entity-resolving games only; ignored on
    //     DS2, which has no per-boss entity id). A set, not a single value -- matching how the
    //     flag-derived table works: the timer starts on whichever listed entity's bar appears
    //     first, with no assumption about ordering. Null/empty = default (flag-derived).
    //   - StartTimerOnPreviousSplit: start the moment the previous split completes,
    //     independent of any healthbar detection. Mutually exclusive with the entity
    //     override in the editor UI (ticking it greys the entity field out) and takes
    //     precedence at runtime; works on every game including DS2.
    public uint[] BossTimerStartEntityIds { get; set; }
    public bool StartTimerOnPreviousSplit { get; set; }

    public string Label => DisplayName ?? Name;
    public bool IsAuto => EventId.HasValue;

    public event PropertyChangedEventHandler PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}