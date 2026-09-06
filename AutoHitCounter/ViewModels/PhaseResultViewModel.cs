//

using System;

namespace AutoHitCounter.ViewModels;

// A single boss-phase sub-row shown beneath its boss split. IsLive marks the
// in-progress phase (the one still being fought) -- it ticks its Hits/Time each
// frame and is excluded from persistence (only completed phases are saved).
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

    public string Label => $"Phase {PhaseNumber}";

    public string TimeDisplay => TimeSpan.FromMilliseconds(TimeMs).ToString(@"m\:ss");
}
