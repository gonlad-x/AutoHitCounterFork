//

using System;
using AutoHitCounter.Core;
using AutoHitCounter.Enums;
using AutoHitCounter.Models;
using AutoHitCounter.Utilities;

namespace AutoHitCounter.ViewModels;

// Per-split "Subsplit on boss phases" dialog. Lets a boss follow the global Sekiro
// setting (default) or force it On/Off just for that split.
public class PhaseSubSplitEditorViewModel : BaseViewModel
{
    private PhaseSubSplitMode _mode;

    public PhaseSubSplitEditorViewModel(SplitEntry split)
    {
        SplitName = split.Label;
        _mode = split.PhaseSubSplitMode;

        ConfirmCommand = new DelegateCommand(Confirm);
        CancelCommand = new DelegateCommand(() => RequestClose?.Invoke());
    }

    public string SplitName { get; }

    public string GlobalHint =>
        $"Follow global setting (currently {(SettingsManager.Default.SKPhaseSubSplitEnabled ? "On" : "Off")})";

    public bool IsInherit
    {
        get => _mode == PhaseSubSplitMode.Inherit;
        set { if (value) SetMode(PhaseSubSplitMode.Inherit); }
    }

    public bool IsOn
    {
        get => _mode == PhaseSubSplitMode.On;
        set { if (value) SetMode(PhaseSubSplitMode.On); }
    }

    public bool IsOff
    {
        get => _mode == PhaseSubSplitMode.Off;
        set { if (value) SetMode(PhaseSubSplitMode.Off); }
    }

    private void SetMode(PhaseSubSplitMode mode)
    {
        _mode = mode;
        OnPropertyChanged(nameof(IsInherit));
        OnPropertyChanged(nameof(IsOn));
        OnPropertyChanged(nameof(IsOff));
    }

    public PhaseSubSplitMode ResultMode { get; private set; }
    public bool Confirmed { get; private set; }

    public DelegateCommand ConfirmCommand { get; }
    public DelegateCommand CancelCommand { get; }

    private void Confirm()
    {
        ResultMode = _mode;
        Confirmed = true;
        RequestClose?.Invoke();
    }

    public Action RequestClose { get; set; }
}
