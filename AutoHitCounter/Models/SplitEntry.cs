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

    private bool _isBossTimerEnabled;

    public bool IsBossTimerEnabled
    {
        get => _isBossTimerEnabled;
        set
        {
            _isBossTimerEnabled = value;
            OnPropertyChanged();
        }
    }

    public long? BossKillTimeBestMs { get; set; }

    public string Label => DisplayName ?? Name;
    public bool IsAuto => EventId.HasValue;

    public event PropertyChangedEventHandler PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}