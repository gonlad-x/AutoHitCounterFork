//

using System;

namespace AutoHitCounter.ViewModels;

// A single boss sub-row: either a fightable Phase (numbered) or a Transition segment
// (the downtime between forms -- Genichiro's transform, the Ape's death->revive). IsLive
// marks the in-progress segment (ticks its Hits/Time, excluded from persistence).
public class PhaseResultViewModel : BaseViewModel
{
    private int _phaseNumber;

    public int PhaseNumber
    {
        get => _phaseNumber;
        set
        {
            if (SetProperty(ref _phaseNumber, value))
                OnPropertyChanged(nameof(Label));
        }
    }

    private bool _isTransition;

    public bool IsTransition
    {
        get => _isTransition;
        set
        {
            if (SetProperty(ref _isTransition, value))
                OnPropertyChanged(nameof(Label));
        }
    }

    private int _hits;

    public int Hits
    {
        get => _hits;
        set => SetProperty(ref _hits, value);
    }

    private long _timeMs;

    public long TimeMs
    {
        get => _timeMs;
        set
        {
            if (SetProperty(ref _timeMs, value))
                OnPropertyChanged(nameof(TimeDisplay));
        }
    }

    private bool _isLive;

    public bool IsLive
    {
        get => _isLive;
        set => SetProperty(ref _isLive, value);
    }

    public string Label => IsTransition ? "transition" : $"Phase {PhaseNumber}";

    public string TimeDisplay => TimeSpan.FromMilliseconds(TimeMs).ToString(@"m\:ss");
}
