# Per-Phase Boss Timer Breakdown (Sekiro) — implementation plan, not yet built

## Context

Multi-phase Sekiro bosses (Guardian Ape, Isshin Ashina/Emma, Genichiro Ashina Castle) currently
show one combined kill-timer value per split. Getting that combined value working correctly
required removing per-entity retry detection entirely — the timer now just runs continuously from
first spawn to kill, with no visibility into how long each individual phase took. The goal of this
feature is to add that visibility back as an **opt-in, per-profile** setting: the main split row
keeps showing the combined total (Hits/Diff/PB unchanged), and indented sub-rows underneath show
each phase's own elapsed time, in three places — the in-app splits list, the profile editor (a new
checkbox), and the HTML overlay. Phases are **named** (e.g. "Emma", "Guardian Ape", "Headless
Ape") rather than generically numbered, per user preference (confirmed worth the extra
hand-authored data).

Two design constraints carried over from the session that produced this plan, both
non-negotiable:
- Phase bookkeeping must be **fully decoupled** from the reset-vs-extend decision in
  `HandleBossHealthBarSpawn`. That logic was deliberately simplified to "any healthbar match on
  the already-tracked flag just extends, full stop" because Guardian Ape's decapitation reuses
  the *same* entity ID as phase 1 — there's no reliable way to tell a phase transition from a
  benign healthbar reshow using entity IDs alone, so retry detection was dropped entirely in
  favor of manual reset (Reset Boss Timer hotkey / split-reset / run-reset). New phase-tracking
  code must not reintroduce any entity-ID-based auto-reset logic.
- Any new per-split live/persisted field needs the same "5 call sites" treatment this codebase
  has been bitten by before: `RunState`, `RunSnapshot`, `RunStateService` (both save paths, both
  restore paths), and `MainViewModel.RefreshSplitValues()`'s manual capture/restore around
  `UpdateSplits()` (which rebuilds every `SplitViewModel` instance from scratch).

(The temporary `BossTimerDebugLog`/`MsgBox` diagnostic instrumentation used to chase the earlier
boss-timer bugs has since been removed — no need to work around it.)

## 1. Entity-ID → phase-name resource

New resx data node in `AutoHitCounter/Properties/Resources.resx`: `SKEntityNames`, CSV format
`EntityID,Name` (same node style as `SKBossEntityIds`). Seed content, verified against existing
`SKEvents`/`SKBossEntityIds` labels already in the file:

```
EntityID,Name
1700800,Guardian Ape
1700850,Headless Ape
1110900,Emma
1110920,Isshin Ashina
1110800,Genichiro Ashina
```

`1110801` (Genichiro Ashina Castle's phase-2 entity) is deliberately left unseeded — no confirmed
name exists anywhere in the codebase (a nearby-looking flag, "Genichiro, Way of Tomoe", turns out
to use a different entity ID entirely, so guessing would just be wrong). It falls back to a
generic "Phase 2" label.

- `AutoHitCounter/Utilities/EventLoader.cs`: add `GetEntityNames(string resourceName) ->
  Dictionary<uint, string>`, mirroring `GetEntityIds`'s resx-CSV-loop but regex `^(\d+)\s*,\s*(.+)$`
  (id, name), one name per ID (last-row-wins, no aggregation).
- `AutoHitCounter/Interfaces/IGameModuleFactory.cs`: add `Dictionary<uint, string>
  GetEntityNamesForGame(GameTitle title);`
- `AutoHitCounter/Services/GameModuleFactory.cs`: add `EntityNameResource` to the private
  `GameRegistration` class (new optional ctor param, default null), set to `"SKEntityNames"` only
  on Sekiro's registration, implement `GetEntityNamesForGame` exactly like
  `GetBossEntityIdsForGame` (empty dict for games without the resource).

**Phase-label resolution**: given the *n*-th distinct entity ID observed for the currently-tracked
flag (1-based, first-seen order), label = `names.TryGetValue(id, out var name) ? name : $"Phase
{n}"`. Positional fallback only — named entries substitute in wherever available regardless of
position, so Genichiro Ashina Castle gets "Genichiro Ashina" / "Phase 2".

## 2. `SplitViewModel` — live phase data

New file `AutoHitCounter/ViewModels/PhaseTimeViewModel.cs`:
```csharp
public class PhaseTimeViewModel : BaseViewModel
{
    public string Label { get; }
    private long? _elapsedMs;
    public long? ElapsedMs
    {
        get => _elapsedMs;
        set { if (SetProperty(ref _elapsedMs, value)) OnPropertyChanged(nameof(Display)); }
    }
    public string Display => ElapsedMs is { } ms ? TimeSpan.FromMilliseconds(ms).ToString(@"m\:ss") : "";

    public PhaseTimeViewModel(string label, long? elapsedMs = null)
    {
        Label = label;
        _elapsedMs = elapsedMs;
    }
}
```
`Label` is immutable once a phase starts; only `ElapsedMs` ticks live. No PB — matches the
mockups (sub-rows show only a time column).

On `SplitViewModel.cs`, add:
```csharp
public ObservableCollection<PhaseTimeViewModel> Phases { get; } = new();
public bool HasPhases => Phases.Count > 0;
```
Wire `Phases.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasPhases));` in the
constructor so `HasPhases` stays correct without every mutation site remembering to raise it.

Do **not** reuse the existing `IsExpanded` property — confirmed via grep it has zero consumers
anywhere in the codebase, likely dead code from a removed feature.

**No expand/collapse toggle in v1.** Sub-rows render whenever `HasPhases` is true, unconditionally.
Reasoning: it's not core to the ask, it would need its own slice of the 5-site persistence
treatment for a feature that only ever touches 3 specific splits, and it can be bolted on later
purely as a view-local `bool IsPhasesExpanded` + a `Visibility` binding without touching the
engine or persistence at all. (The mockup's per-row `>` glyphs most likely represent this app's
existing drag-handle icon, not a new phase-specific chevron — every row in the mockup has one,
not just the multi-phase ones.)

## 3. `MainViewModel` boss-timer engine

Core principle: phase bookkeeping is purely additive *recording*, layered onto the existing
reset-vs-extend branch — it must never influence that decision.

**Gate at the source**: only track phases at all when the profile flag is on
(`GetRule("show_individual_phase_timers")`, the existing helper around line 385). If the flag is
off, skip all `BeginPhase`/`TransitionPhase`/`FinalizeCurrentPhase` bodies (early return) so
`Phases` stays empty and nothing extra gets computed, displayed, or persisted. This means the
XAML/overlay layers need no separate gating — `HasPhases` alone is enough.

New state fields alongside the existing `_bossTimer*` fields:
```csharp
private uint? _bossTimerCurrentPhaseEntityId;
private long? _bossTimerPhaseStartIgtMs;
private long _bossTimerPhaseAccumulatedMs; // mirrors _bossTimerAccumulatedMs, one level down
```

**`HandleBossHealthBarSpawn(uint entityId)`**:
- Reset/start branch (new flag matched): after `_bossTimerSplit = ...; _bossTimerFlag = ...;`,
  clear and re-seed: `_bossTimerSplit.Phases.Clear(); BeginPhase(entityId, (long)InGameTime.TotalMilliseconds);`
  (gated by the profile flag, per above).
- Extend branch (`_bossTimerFlag == entry.EventId.Value`, currently just `return`): change to
  detect a genuine phase transition —
  ```csharp
  if (_bossTimerFlag == entry.EventId.Value)
  {
      if (entityId != _bossTimerCurrentPhaseEntityId)
          TransitionPhase(entityId);
      return;
  }
  ```
  Guardian Ape's decapitation reshow has `entityId == _bossTimerCurrentPhaseEntityId` (same ID),
  so `TransitionPhase` is skipped and this remains a pure no-op extend, exactly as today.

New helpers near `ClearBossTimerState`, mirroring the existing split-level
start/accumulate/pause/resume pattern one level down (get the accumulated+delta shape exactly
right — don't mix "accumulate on write" and "overwrite on write" styles):
```csharp
private void BeginPhase(uint entityId, long nowIgt)
{
    if (!GetRule("show_individual_phase_timers") || _bossTimerSplit == null) return;
    _bossTimerCurrentPhaseEntityId = entityId;
    _bossTimerPhaseStartIgtMs = nowIgt;
    _bossTimerPhaseAccumulatedMs = 0;
    var label = ResolvePhaseLabel(entityId, _bossTimerSplit.Phases.Count + 1);
    _bossTimerSplit.Phases.Add(new PhaseTimeViewModel(label));
}

private void TransitionPhase(uint entityId)
{
    if (!GetRule("show_individual_phase_timers")) return;
    FinalizeCurrentPhase();
    BeginPhase(entityId, (long)InGameTime.TotalMilliseconds);
}

private void FinalizeCurrentPhase()
{
    if (_bossTimerSplit == null || _bossTimerPhaseStartIgtMs is not { } startMs) return;
    _bossTimerPhaseAccumulatedMs += (long)InGameTime.TotalMilliseconds - startMs;
    var phase = _bossTimerSplit.Phases.LastOrDefault();
    if (phase != null) phase.ElapsedMs = _bossTimerPhaseAccumulatedMs;
}

private string ResolvePhaseLabel(uint entityId, int phaseIndex)
{
    var names = _gameModuleFactory.GetEntityNamesForGame(_selectedGame.Title);
    return names.TryGetValue(entityId, out var name) ? name : $"Phase {phaseIndex}";
}
```

**`UpdateInGameTime(long igt)`**: fold a live phase update into the existing split-level live-tick
block, using the same accumulated+delta shape as `BossKillTimeMs`:
```csharp
if (_bossTimerStartIgtMs is { } startMs && _bossTimerSplit != null)
{
    var previousDisplay = _bossTimerSplit.BossKillTimeDisplay;
    _bossTimerSplit.BossKillTimeMs = _bossTimerAccumulatedMs + (igt - startMs);

    if (_bossTimerPhaseStartIgtMs is { } phaseStartMs && _bossTimerSplit.Phases.LastOrDefault() is { } livePhase)
        livePhase.ElapsedMs = _bossTimerPhaseAccumulatedMs + (igt - phaseStartMs);

    if (_bossTimerSplit.BossKillTimeDisplay != previousDisplay)
        _overlayServerService.BroadcastState(OverlayMapper.MapFrom(this));
}
```

**`ToggleBossTimer()`**: manual pause should finalize+freeze the current phase
(`FinalizeCurrentPhase(); _bossTimerPhaseStartIgtMs = null;`); manual resume should re-arm it
(`_bossTimerPhaseStartIgtMs = (long)InGameTime.TotalMilliseconds;`). The manual-start branch
(entering a fresh split via the hotkey, no detected `entityId`) gets **no phase breakdown** —
`Phases` stays empty since there's nothing to seed a first phase with; phases only ever come from
real healthbar-spawn detections.

**`ResetBossTimer()`**: should fully clear phase state too (as part of whatever the current
Reset semantics are — check the reset behavior fix that was applied before this feature, since
Reset was changed to fully clear/stop rather than zero-and-continue): clear
`_bossTimerSplit.Phases`, `_bossTimerCurrentPhaseEntityId`, `_bossTimerPhaseStartIgtMs`,
`_bossTimerPhaseAccumulatedMs` wherever the base timer fields get cleared.

**`StopBossTimer()`**: call `FinalizeCurrentPhase()` before `ClearBossTimerState()` so the last
phase's time is captured (mirrors the `elapsed` computation right above it for the split total).
`Phases` itself is **not** cleared here — a finished fight's breakdown stays visible on the
completed split, same as `BossKillTimeMs` does.

**`ClearBossTimerState()`**: add `_bossTimerCurrentPhaseEntityId = null;
_bossTimerPhaseStartIgtMs = null; _bossTimerPhaseAccumulatedMs = 0;`.

No changes needed to `ManualAdvanceSplit`, `AutoAdvanceSplit`, or `IsBossTimerEligible`.

## 4. Persistence

New file `AutoHitCounter/Models/PhaseSnapshot.cs`:
```csharp
public class PhaseSnapshot
{
    public string Label { get; set; }
    public long? ElapsedMs { get; set; }
}
```

- `RunState.cs`: add `public PhaseSnapshot[][] Phases { get; set; }` (jagged, parallel to
  `BossKillTimesMs` by child-split index).
- `RunSnapshot.cs`: add `PhaseSnapshot[][] phases` ctor param + `public PhaseSnapshot[][] Phases { get; }`.
- `RunStateService.cs`, five sites:
  1. `SaveRunState` — add `Phases = children.Select(s => s.Phases.Select(p => new PhaseSnapshot
     { Label = p.Label, ElapsedMs = p.ElapsedMs }).ToArray()).ToArray()` to the `RunState` literal.
  2. `Capture` — same projection, passed to the new `RunSnapshot` ctor arg.
  3/4. `RestoreSnapshot` / `RestoreFromSavedRun` — call a new `RestorePhases(splits,
     snapshot.Phases)` / `RestorePhases(splits, state.Phases)` right after `RestoreBossKillTimes`.
  5. New shared helper, mirroring `RestoreBossKillTimes`:
     ```csharp
     private static void RestorePhases(IList<SplitViewModel> splits, PhaseSnapshot[][] phases)
     {
         if (phases == null) return; // pre-existing saved runs predate this field
         var children = splits.Where(s => s.Type == SplitType.Child).ToList();
         for (int i = 0; i < children.Count && i < phases.Length; i++)
         {
             children[i].Phases.Clear();
             if (phases[i] == null) continue;
             foreach (var p in phases[i])
                 children[i].Phases.Add(new PhaseTimeViewModel(p.Label, p.ElapsedMs));
         }
     }
     ```
- `MainViewModel.RefreshSplitValues()`, the fifth site — extend the manual capture/restore
  around `UpdateSplits()`:
  ```csharp
  var phases = Splits.Select(s => s.Phases.Select(p => new PhaseSnapshot { Label = p.Label, ElapsedMs = p.ElapsedMs }).ToArray()).ToArray();
  // ...UpdateSplits()...
  for (int i = 0; i < Splits.Count && i < hits.Length; i++)
  {
      Splits[i].NumOfHits = hits[i];
      Splits[i].BossKillTimeMs = bossKillTimes[i];
      if (i < phases.Length)
          foreach (var p in phases[i]) Splits[i].Phases.Add(new PhaseTimeViewModel(p.Label, p.ElapsedMs));
  }
  ```
  Before touching this, sanity-check `grep -n "ClearBossTimerState" ViewModels/MainViewModel.cs`
  to confirm no path calls `RefreshSplitValues()` mid-fight without also clearing boss-timer
  state — if confirmed, `_bossTimerSplit`'s stale-instance-after-rebuild concern is a non-issue
  (rebuilds only happen between runs/profile switches).

## 5. UI wiring

**`GameFlagRegistry.cs`** — add to Sekiro's list:
```csharp
[GameTitle.Sekiro] =
[
    ("should_count_roberto", "Count Roberto stagger"),
    ("show_individual_phase_timers", "Show individual phase timers")
]
```
Nothing else needed here or in `ProfileEditorViewModel.cs`/`GameFlagViewModel.cs` — confirmed
`RebuildGameFlags` generates a working checkbox for free from this tuple alone.

**`MainWindow.xaml`** — first add a new font-size resource near the existing ones
(`FontSizeSmall` does not currently exist, only `FontSizeHeader`/`FontSizeBody`):
```xml
<system:Double x:Key="FontSizeSmall">11</system:Double>
```
Then, inside the splits `DataTemplate`'s `StackPanel`, immediately after the `</Grid>` closing
the existing 5-column row and before the Notes grid, add a sibling:
```xml
<ItemsControl ItemsSource="{Binding Phases}" Visibility="{Binding HasPhases, Converter={StaticResource BoolToVisibilityConverter}}">
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <Grid Margin="24,0,10,0">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <TextBlock Grid.Column="0" Text="{Binding Label}" FontFamily="{StaticResource AppFont}" FontSize="{StaticResource FontSizeSmall}" Opacity="0.7" />
                <TextBlock Grid.Column="1" Text="{Binding Display}" FontFamily="JetBrains Mono" FontSize="{StaticResource FontSizeSmall}" Opacity="0.7" />
            </Grid>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```
(Verify a `BoolToVisibilityConverter`-equivalent already exists as a resource — the file already
uses converters elsewhere, e.g. `ParentChildMarginConverter`; if none matches exactly, add a
trivial one or use a `DataTrigger` matching the existing `IsParent`-collapse pattern already used
throughout this template.)

Since gating happens in the engine (section 3), no `ShowIndividualPhaseTimers` bindable property
is needed on `MainViewModel` — `HasPhases` alone is sufficient and correct.

**`OverlayState.cs`** — add to `OverlaySplit`:
```csharp
public List<OverlayPhase> Phases { get; set; } = new();
```
and:
```csharp
public class OverlayPhase
{
    public string Label { get; set; }
    public string Time { get; set; }
}
```

**`OverlayMapper.cs`** — extend the `Splits = vm.Splits.Select(...)` projection:
```csharp
Phases = s.Phases.Select(p => new OverlayPhase { Label = p.Label, Time = FormatBossTime(p.ElapsedMs, isPast: false) }).ToList()
```
(reuses the existing private `FormatBossTime`; `isPast: false` since there's no "past with no
time" concept per-phase). Falls out empty for free when the engine-level gate is off.

**`Overlay.html`** — extend `buildRow` to append phase rows after the main row:
```js
function buildPhaseRow(p) {
    return `<div class="split-row phase-row">
        <span class="col-name">${escapeHtml(p.label)}</span>
        <span class="col-boss-time">${p.time || ''}</span>
    </div>`;
}
```
appended in `buildRow`'s return via `+ (s.phases?.length ? s.phases.map(buildPhaseRow).join('') : '')`.
Add a `.split-row.phase-row` CSS rule near the other `.split-row.*` rules: smaller `font-size`
(e.g. `0.8em`), reduced height, indented `.col-name` padding, reduced opacity — reuse existing
config-driven colors rather than adding new customization-panel config keys.

**Known limitation to flag to the user, not silently swallow**: the overlay's row-count
windowing (`computeVisibleRowWindow`/`maxRows`) counts by split index, not rendered DOM rows, so
phase sub-rows aren't counted against the configured visible-row budget — a split with phases
will take more vertical space than the row-height math assumes. Acceptable for v1 (only 3 splits
ever have phases, 2-3 sub-rows each), worth a one-line code comment.

## Build order

1. `SKEntityNames` resx + `EventLoader.GetEntityNames` + `GameModuleFactory` wiring.
2. `GameFlagRegistry` entry (independently verifiable in a real VS build: checkbox appears in
   Profile Editor, persists via `GameSettings`).
3. `PhaseTimeViewModel` + `SplitViewModel.Phases`/`HasPhases`.
4. `MainViewModel` engine changes (section 3) — highest-risk step, get the accumulated+delta
   pattern exactly right by mirroring the existing split-level pause/resume/tick code.
5. Persistence (section 4).
6. `MainWindow.xaml` sub-rows.
7. `OverlayState`/`OverlayMapper`/`Overlay.html`.

## Verification

`dotnet build` from the CLI in this repo reports pre-existing CS0115 errors unrelated to any
boss-timer change (legacy .NET Framework WPF project, the CLI doesn't run XAML codegen for it) —
it can still catch C#-level compile errors in non-XAML files, but the `MainWindow.xaml`
binding/converter additions specifically need a real Visual Studio build to catch XAML-only
mistakes (typos, missing resource keys, binding errors).

Live in-game checks, in order:
1. **Guardian Ape** (flag 9304, 1700800→1700850): confirms decapitation reshow still doesn't
   reset or add a phase (same entity ID), and the genuine transition correctly appends a
   "Headless Ape" phase row while the combined total keeps counting through both.
2. **Isshin Ashina / Emma** (flag 9316, 1110900→1110920): cleanest positive case — "Emma" then
   "Isshin Ashina" rows in order, individual times summing to ~the combined total.
3. **Genichiro Ashina Castle** (flag 11110800, 1110800→1110801): named/fallback mix — "Genichiro
   Ashina" then generic "Phase 2" — renders without misalignment.
4. **Profile editor checkbox**: toggle on/off, confirm persistence across app restart, and that
   turning it off makes sub-rows disappear live (not just after restart).
5. **Overlay**: connect a browser/OBS source to `ws://127.0.0.1:16200`, confirm phase rows render
   during a live fight and stay absent when the profile flag is off.
6. **App-restart persistence**: mid-fight (partway into phase 2), close and relaunch, confirm both
   the combined time and the phase breakdown resume correctly rather than reverting to empty.
7. **`RefreshSplitValues()` path**: find and trigger whatever currently calls
   `RefreshSplitValues()` mid-run (e.g. editing a split's PB/notes) and confirm phase data
   survives the `SplitViewModel` rebuild instead of reverting to empty — this is exactly the bug
   class the section 4 plumbing exists to prevent.

### Critical files
- `AutoHitCounter/ViewModels/MainViewModel.cs`
- `AutoHitCounter/ViewModels/SplitViewModel.cs`
- `AutoHitCounter/ViewModels/PhaseTimeViewModel.cs` (new)
- `AutoHitCounter/Models/PhaseSnapshot.cs` (new)
- `AutoHitCounter/Models/RunState.cs`, `RunSnapshot.cs`
- `AutoHitCounter/Services/RunStateService.cs`
- `AutoHitCounter/Services/GameFlagRegistry.cs`, `GameModuleFactory.cs`
- `AutoHitCounter/Utilities/EventLoader.cs`
- `AutoHitCounter/Mappers/OverlayMapper.cs`
- `AutoHitCounter/Models/OverlayState.cs`
- `AutoHitCounter/Overlay.html`
- `AutoHitCounter/MainWindow.xaml`
- `AutoHitCounter/Properties/Resources.resx`
