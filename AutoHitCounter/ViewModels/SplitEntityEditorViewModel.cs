//

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AutoHitCounter.Core;
using AutoHitCounter.Models;

namespace AutoHitCounter.ViewModels;

// Backs the "Edit entity id" dialog -- defines *when* a split's boss timer starts
// (distinct from "Edit Event", which defines the flag that completes the split and stops
// the timer). Two mutually-exclusive modes:
//   - a set of start entity ids (typed directly as a comma-separated list, or picked by
//     boss name from the list, which fills the field); the timer starts when *any* of
//     those entities' healthbars is detected -- a set, not a single value, matching the
//     flag-derived table it overrides, so no ordering/"which is first" assumption is made.
//   - "start timer on previous split completion", which starts the timer the instant the
//     preceding split completes and greys the entity controls out.
// On DS2 (no per-boss entity id) the entity controls are hidden entirely -- the only
// choice there is "start on healthbar detected" (unchecked) vs. previous-split completion.
public class SplitEntityEditorViewModel : BaseViewModel
{
    private static readonly char[] Separators = { ',', ' ', ';' };

    public SplitEntityEditorViewModel(SplitEntry split, IReadOnlyList<EntityOption> entityOptions,
        bool supportsEntityStart, uint[] initialEntityIds)
    {
        SplitName = split.Label;
        SupportsEntityStart = supportsEntityStart;

        ConfirmCommand = new DelegateCommand(Confirm, () => IsValidEntityIds);
        ClearCommand = new DelegateCommand(Clear);

        _entityIdText = FormatIds(initialEntityIds);
        _startTimerOnPreviousSplit = split.StartTimerOnPreviousSplit;

        foreach (var option in entityOptions)
            AllEntities.Add(option);

        FilteredEntities = new ObservableCollection<EntityOption>(AllEntities);
    }

    public string SplitName { get; }
    public bool SupportsEntityStart { get; }
    public ObservableCollection<EntityOption> AllEntities { get; } = new();
    public ObservableCollection<EntityOption> FilteredEntities { get; }

    public uint[] ResultEntityIds { get; private set; }
    public bool Confirmed { get; private set; }

    public DelegateCommand ConfirmCommand { get; }
    public DelegateCommand ClearCommand { get; }

    private string _entityIdText;

    public string EntityIdText
    {
        get => _entityIdText;
        set
        {
            if (SetProperty(ref _entityIdText, value))
            {
                OnPropertyChanged(nameof(IsValidEntityIds));
                ConfirmCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private bool _startTimerOnPreviousSplit;

    public bool StartTimerOnPreviousSplit
    {
        get => _startTimerOnPreviousSplit;
        set
        {
            if (SetProperty(ref _startTimerOnPreviousSplit, value))
                OnPropertyChanged(nameof(IsEntitySectionEnabled));
        }
    }

    // The entity id/picker/search controls stay editable only while the checkbox is off --
    // the two start modes are mutually exclusive. The typed ids are preserved (not wiped)
    // when the box is ticked, so unticking restores them.
    public bool IsEntitySectionEnabled => SupportsEntityStart && !StartTimerOnPreviousSplit;

    private EntityOption _selectedEntity;

    public EntityOption SelectedEntity
    {
        get => _selectedEntity;
        set
        {
            // Picking a boss fills the field with *all* of that flag's entities -- the timer
            // then starts on whichever one's healthbar shows first.
            if (SetProperty(ref _selectedEntity, value) && value != null)
                EntityIdText = FormatIds(value.EntityIds);
        }
    }

    private string _searchText;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                FilterEntities();
        }
    }

    // Valid when empty, or every whitespace/comma-separated token parses as a uint.
    public bool IsValidEntityIds => TryParseIds(EntityIdText, out _);

    private void Confirm()
    {
        TryParseIds(EntityIdText, out var ids);
        ResultEntityIds = ids;
        Confirmed = true;
        RequestClose?.Invoke();
    }

    private void Clear()
    {
        EntityIdText = string.Empty;
        SelectedEntity = null;
        ResultEntityIds = null;
        Confirmed = true;
        RequestClose?.Invoke();
    }

    private void FilterEntities()
    {
        FilteredEntities.Clear();
        foreach (var e in AllEntities)
        {
            if (string.IsNullOrEmpty(_searchText) ||
                e.Name.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                FilteredEntities.Add(e);
        }
    }

    private static string FormatIds(uint[] ids) =>
        ids is { Length: > 0 } ? string.Join(", ", ids) : string.Empty;

    // true if the text is empty or a clean list of uints; ids is null when empty, else the
    // parsed (distinct, order-preserving) set.
    private static bool TryParseIds(string text, out uint[] ids)
    {
        ids = null;
        if (string.IsNullOrWhiteSpace(text)) return true;

        var tokens = text.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        var parsed = new List<uint>();
        foreach (var token in tokens)
        {
            if (!uint.TryParse(token, out var id)) return false;
            if (!parsed.Contains(id)) parsed.Add(id);
        }

        ids = parsed.Count > 0 ? parsed.ToArray() : null;
        return true;
    }

    public Action RequestClose { get; set; }
}

// One selectable boss in the entity picker. Name comes from the game's event list, the
// entity ids from its boss-entity-id table (joined on the shared flag) -- the whole set,
// since a flag can drive several entities (duo/phase fights).
public class EntityOption(string name, uint[] entityIds)
{
    public string Name { get; } = name;
    public uint[] EntityIds { get; } = entityIds;
}
