//

using System;
using System.Windows;
using System.Windows.Media;
using AutoHitCounter.Enums;
using AutoHitCounter.Services;

namespace AutoHitCounter.ViewModels;

public class SplitViewModel : BaseViewModel
{
    public SplitViewModel()
    {
        ThemeService.ThemeChanged += OnThemeChanged;
    }

    private string _name;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    private int _numOfHits;

    public int NumOfHits
    {
        get => _numOfHits;
        set
        {
            if (SetProperty(ref _numOfHits, value))
            {
                OnPropertyChanged(nameof(Diff));
                OnPropertyChanged(nameof(HitsBrush));
                OnPropertyChanged(nameof(DiffBrush));
            }
        }
    }

    private int _personalBest;

    public int PersonalBest
    {
        get => _personalBest;
        set
        {
            if (SetProperty(ref _personalBest, value))
                OnPropertyChanged(nameof(Diff));
            OnPropertyChanged(nameof(DiffBrush));
        }
    }

    private bool _isCurrent;

    public bool IsCurrent
    {
        get => _isCurrent;
        set => SetProperty(ref _isCurrent, value);
    }

    private bool _isDistancePb;

    public bool IsDistancePb
    {
        get => _isDistancePb;
        set => SetProperty(ref _isDistancePb, value);
    }

    private bool _isAuto;

    public bool IsAuto
    {
        get => _isAuto;
        set => SetProperty(ref _isAuto, value);
    }

    private long? _bossKillTimeMs;

    public long? BossKillTimeMs
    {
        get => _bossKillTimeMs;
        set
        {
            if (SetProperty(ref _bossKillTimeMs, value))
            {
                OnPropertyChanged(nameof(BossKillTimeDisplay));
                OnPropertyChanged(nameof(BossKillTimeBrush));
            }
        }
    }

    private long? _bossKillTimeBestMs;

    public long? BossKillTimeBestMs
    {
        get => _bossKillTimeBestMs;
        set
        {
            if (SetProperty(ref _bossKillTimeBestMs, value))
            {
                OnPropertyChanged(nameof(BossKillTimeBestDisplay));
                OnPropertyChanged(nameof(BossKillTimeBrush));
            }
        }
    }

    // Green when the split is actually done (IsPast -- the same "completed" signal
    // used elsewhere for this split) and its kill time beats its own PB. Gated on
    // IsPast specifically so a still-live-ticking timer never flashes green just
    // because it's transiently under the PB mid-fight -- only a finished kill counts.
    // Same better-than-PB-is-green convention as DiffBrush, just no "worse" case
    // since there's no time-diff column to attach that to.
    public Brush BossKillTimeBrush => IsPast && BossKillTimeMs is { } ms && BossKillTimeBestMs is { } best && ms < best
        ? GetBrush("DiffNegativeBrush")
        : GetBrush("DiffNeutralBrush");

    // Session-only (not persisted to SplitEntry) snapshot of whatever BossKillTimeMs
    // last held right before a non-kill clear (ResetBossTimer, HandleGameUnloaded, the
    // IGT-rewind guard) wiped it -- lets "Restore Timer" recover a run that got reset
    // by something other than the player deliberately choosing to reset it (e.g. a
    // quitout or warp mid-attempt).
    public long? BossKillTimeRestoreMs { get; set; }

    private bool _isPast;

    // Set by MainViewModel whenever the current-split cursor moves -- true for any split
    // already behind the cursor (or all splits once the run is complete), regardless of
    // whether it's boss-timer-eligible. Drives the "-" for a passed split with no time.
    public bool IsPast
    {
        get => _isPast;
        set
        {
            if (SetProperty(ref _isPast, value))
            {
                OnPropertyChanged(nameof(BossKillTimeDisplay));
                OnPropertyChanged(nameof(BossKillTimeBestDisplay));
                OnPropertyChanged(nameof(BossKillTimeBrush));
            }
        }
    }

    public string BossKillTimeDisplay => BossKillTimeMs is { } ms
        ? TimeSpan.FromMilliseconds(ms).ToString(@"m\:ss")
        : IsPast ? "-" : "";

    public string BossKillTimeBestDisplay => BossKillTimeBestMs is { } ms
        ? TimeSpan.FromMilliseconds(ms).ToString(@"m\:ss")
        : IsPast ? "-" : "";

    private SplitType _type = SplitType.Child;

    public SplitType Type
    {
        get => _type;
        set
        {
            if (SetProperty(ref _type, value))
                OnPropertyChanged(nameof(IsParent));
        }
    }

    private string _groupId;

    public string GroupId
    {
        get => _groupId;
        set => SetProperty(ref _groupId, value);
    }

    private bool _isExpanded;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    private string _notes;

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public bool IsParent => Type == SplitType.Parent;
    public int Diff => NumOfHits - PersonalBest;

    public Brush HitsBrush => NumOfHits > 0
        ? GetBrush("HitsActiveBrush")
        : GetBrush("HitsInactiveBrush");

    public Brush DiffBrush
    {
        get
        {
            if (Diff > 0) return GetBrush("DiffPositiveBrush");
            if (Diff < 0) return GetBrush("DiffNegativeBrush");
            return GetBrush("DiffNeutralBrush");
        }
    }

    private static Brush GetBrush(string key)
    {
        if (Application.Current.Resources[key] is SolidColorBrush brush)
            return brush;
        return new SolidColorBrush(Colors.White);
    }

    private bool _isEditing;

    public bool IsEditing
    {
        get => _isEditing;
        set => SetProperty(ref _isEditing, value);
    }

    public bool IsEditingPb
    {
        get => _isEditingPb;
        set
        {
            _isEditingPb = value;
            OnPropertyChanged();
        }
    }

    private bool _isEditingPb;

    public bool IsEditingBossKillTimePb
    {
        get => _isEditingBossKillTimePb;
        set
        {
            _isEditingBossKillTimePb = value;
            OnPropertyChanged();
        }
    }

    private bool _isEditingBossKillTimePb;

    public void RefreshLayout()
    {
        OnPropertyChanged(nameof(IsEditingPb));
        OnPropertyChanged(nameof(PersonalBest));
        OnPropertyChanged(nameof(IsEditingBossKillTimePb));
        OnPropertyChanged(nameof(BossKillTimeBestDisplay));
    }

    private void OnThemeChanged()
    {
        OnPropertyChanged(nameof(HitsBrush));
        OnPropertyChanged(nameof(DiffBrush));
    }

    public override void Dispose()
    {
        ThemeService.ThemeChanged -= OnThemeChanged;
    }
}