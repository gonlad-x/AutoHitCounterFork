# Boss Kill Timer -- porting from Sekiro to DS3/DSR/DS2/Elden Ring

Status: **research only, nothing built**. Written 2026-08-11 after a full audit of what
already exists per game vs. what is fully missing, to scope the porting work. The Sekiro
feature itself (branch `boss_timers`) is built and confirmed working (Isshin Ashina/Emma,
Guardian Ape) -- see `resources/phase-timer-breakdown-plan.md` (same folder) for the
in-flight, not-yet-built per-phase-breakdown work on top of it. This doc is the cross-game
companion: what it takes to bring the base "per-split boss kill timer" feature (not the
per-phase breakdown) to the other four games.

## How the feature works (Sekiro, for reference)

- `SKEventService` hooks the game's native `SetEventFlag` function (code-cave shellcode +
  ring buffer, polled every tick) to drive existing auto-splitting.
- `SKBossHealthBarService` is a second, independent instance of the exact same
  hook-install-ring-buffer-poll pattern, hooking the native function behind the EMEVD
  instruction `Display Boss Health Bar` (confirmed same hook also fires for `Display
  Miniboss Health Bar`), exposing entity IDs of boss/miniboss healthbars as they spawn.
- `IGameModule.OnBossHealthBarSpawn` (uniform member on the interface, only `SKModule`
  actually raises it) leads to `GameSessionOrchestrator.BossHealthBarSpawnDetected` leads to
  `MainViewModel.HandleBossHealthBarSpawn(uint entityId)`, which matches the fired entity
  ID against a per-game `Dictionary<uint, uint[]>` (flag -> possible entity IDs) from
  `IGameModuleFactory.GetBossEntityIdsForGame(GameTitle)`. For Sekiro this is backed by
  `SKBossEntityIds` in `AutoHitCounter/Properties/Resources.resx`, hand-curated by
  data-mining Sekiro's EMEVD event scripts with `soulstruct`, matching healthbar-spawn
  entity IDs to each split's existing auto-split flag.
- Gated by a single settings flag, `SettingsManager.Default.SKBossTimeTrackersEnabled`
  ("Boss Time Trackers" checkbox, currently living inside the Sekiro-only block of
  `Views/Controls/SettingsTab.xaml`, next to "No Logo"/"No Tutorials").

## Per-game status

| Game | OnBossHealthBarSpawn raised? | Healthbar hook service | GetBossEntityIdsForGame data | AOB fallback infra (XPatterns.cs) | SetEvent hook (reusable template) |
|---|---|---|---|---|---|
| Sekiro | Yes (SKModule.cs) | SKBossHealthBarService.cs | SKBossEntityIds resx, real data | Yes (SKPatterns.cs) | Yes |
| DS3 | No -- event declared, never invoked | None | Empty (no resource registered) | Yes (DS3Patterns.cs) | Yes (DS3EventService.cs) |
| Elden Ring | No -- event declared, never invoked | None | Empty (no resource registered) | Yes (EldenRingPatterns.cs) | Yes (EldenRingEventService.cs) |
| DSR | No -- event declared, never invoked | None | Empty (no resource registered) | No -- no Patterns.cs/IsAobFallback at all | Yes (DSREventService.cs) |
| DS2 | No -- event declared, never invoked | None | Empty (no resource registered) | No -- no Patterns.cs/IsAobFallback at all, plus dual Vanilla/Scholar hook variants | Yes (DS2EventService.cs, two variants) |

Confirmed via grep: `AutoHitCounter/Games/{DS3,DSR,DS2,ER}/{X}Module.cs` all declare
`public event Action<uint> OnBossHealthBarSpawn;` purely to satisfy `IGameModule` (matches
the CS0414 "assigned but never used" build warnings) and null it in `Dispose()` -- genuinely
dead code, not a stub with placeholder logic.

`GameModuleFactory.cs`'s `Registrations` dictionary only supplies a `bossEntityIdResource`
for `GameTitle.Sekiro` (`SKBossEntityIds`); the other four rows omit the parameter, so
`GetBossEntityIdsForGame` falls through to `new()` for all of them -- a genuinely empty
dictionary, not stubbed/placeholder data.

## What is already reusable per game (the good news)

**The hook mechanism itself needs no new invention for any game.** Every game already has
a fully working `SetEventFlag` hook (`{X}EventService.cs` extends `EventServiceBase`) built
on exactly the same pattern `SKBossHealthBarService` reuses: AOB/version-resolved
`Hooks.SetEvent` native function address, then shellcode loaded via
`AsmLoader.GetAsmBytes(AsmScript.XEventLog)`, then addresses patched in via
`AsmHelper.WriteRelativeOffsets`, then written into an allocated code-cave region, then
`HookManager.InstallHook` (passive JMP-into-code-cave, not `AllocateAndExecute`), then
`EventServiceBase.ShouldSplit()`'s ring-buffer poll/wraparound logic. A new
`XBossHealthBarService.cs` per game is close to a copy of `SKBossHealthBarService.cs` once
the target hook address is known.

**Code-cave headroom exists in every game's `CustomCodeOffsets` for a new ring-buffer
section.** Shared code cave is `0x5000` bytes (`MemoryService.CodeCaveSize`); current usage
tops out around `0x3700`-`0x4100` depending on the game, leaving room for
`BossHealthBarWriteIdx`/`Code`/`Buffer` constants the same way SK added
`0x4000`/`0x4020`/`0x4100`.

**Downstream plumbing is already fully game-agnostic -- zero changes needed there:**
`IGameModule`/`IGameModuleFactory` interfaces, `GameSessionOrchestrator`'s wiring
(`_currentModule.OnBossHealthBarSpawn += entityId => BossHealthBarSpawnDetected?.Invoke(entityId);`),
`MainViewModel.HandleBossHealthBarSpawn`'s matching logic, `EventLoader.GetEntityIds`'s
resx-CSV aggregation. A new game just needs its own resx resource registered in
`GameModuleFactory`'s `Registrations` row, same shape as Sekiro's.

## What is fully missing per game (the real work)

For each of DS3 / DSR / DS2 / Elden Ring, from scratch:

1. **Reverse-engineer the native "Display Boss Health Bar" function address.** Real RE
   work (Ghidra), not scaffolding -- use the already-hooked `SetEvent` address as a
   landmark, same methodology used to find Sekiro's. Note even Sekiro's own hook is only
   confirmed for one file version (1.6.0) out of five tracked -- DS3 tracks 18 versions
   and Elden Ring tracks 26, a much larger matrix than Sekiro's. Scope realistically to
   "current/latest version resolved, AOB fallback for the rest" rather than assuming full
   version-matrix coverage up front -- and for DSR/DS2, AOB fallback infrastructure
   (Patterns.cs/IsAobFallback) does not exist at all yet, so either build it first or
   accept version-locked-only support there.
2. **Write and resx-embed that game's shellcode variant** -- new `AsmScript` enum entries
   (`Enums/AsmScript.cs`) mirroring `SKBossHealthBarLog`, embedded the same way the
   existing `X EventLog` shellcode resources are.
3. **Add `XBossHealthBarService.cs`** mirroring `SKBossHealthBarService.cs` (hook install +
   ring buffer read, about 70 lines).
4. **Wire it into that game's `XModule.cs`** -- instantiate the service, call
   `InstallHook()`, poll `TryGetLatestSpawn` every `Tick()`, actually invoke
   `OnBossHealthBarSpawn?.Invoke(entityId)` (currently dead on all four) -- mirrors
   `SKModule.cs`'s `Initialize`/`Tick`.
5. **Extend `XCustomCodeOffsets.cs`** with a new ring-buffer section (room confirmed to
   exist).
6. **Hand-mine flag-to-entityID data per game** (same soulstruct/DarkScript3 methodology
   used for Sekiro -- see the family skill doc's "Mining game data" section) and add a
   `XBossEntityIds` resx CSV, registered in `GameModuleFactory`'s `Registrations` row.
   Watch for the same failure modes already hit in Sekiro's own data (see this session's
   Guardian Ape/Genichiro findings below) -- multi-phase bosses reusing or not-reusing
   entity IDs across phases needs verifying per-boss, not assumed.
7. **DS2 specifically** needs the boss-healthbar hook installed in both Vanilla and
   Scholar variants, mirroring `DS2EventService`'s existing `InstallScholarHook`/
   `InstallVanillaHook` split.

**Not per-game, but worth doing once, up front, before porting to any game**: the
`SKBossTimeTrackersEnabled` gate is a single global setting, Sekiro-named, living in the
Sekiro-only settings block -- either generalize it to one shared toggle covering every
game, or split it per-game, before wiring a second game's data into the same code path
that checks it.

## Lessons from this session's Sekiro data bugs (apply proactively when porting)

Found and fixed live in this session:

- A single continuous fight can fire the healthbar hook twice for two different entity
  IDs without any split boundary in between (Guardian Ape: 1700800, then decapitation,
  then 1700850, still one split, flag 9304). Missing the second entity ID as an
  aggregated CSV row does not just under-count time, it can cause the spawn to get
  matched against a different, later split if that entity ID happens to also be used by
  an unrelated later encounter (Guardian Ape's 1700850 collides with the separate
  "Headless Ape"/Duo Ape rematch split, flag 11700850, further down the split list) --
  `HandleBossHealthBarSpawn`'s forward search from the current split's index will walk
  right past the intended split and match the wrong one. Always check for entity-ID
  reuse across different splits when mining a new game's data, not just within one
  boss's own phases.
- A boss's phase-2 entity ID can be completely absent from the data with no visible
  symptom other than "it happens to work" (Genichiro Ashina Castle, 1110801, was
  unseeded -- the timer looked correct only because a non-match is a no-op, not because
  it was deliberately handled). Do not treat "looks correct in one test" as confirmation
  a multi-phase boss's data is complete; check EMEVD directly for every "Display Boss
  Health Bar" instruction tied to the fight, not just the ones a quick playtest happens
  to hit.
- The code-level fix that makes aggregating multiple entity IDs under one flag safe
  (already applied to Sekiro, uncommitted on `boss_timers` as of this session): drop
  automatic retry/wipe detection from `HandleBossHealthBarSpawn` entirely. Any repeat
  healthbar match on the flag already being timed is now a no-op (extend, do not reset)
  -- there is no reliable way to distinguish "genuine phase transition" from "same
  entity's bar hiding/reshowing" from "player wiped and retried" using entity IDs alone,
  so the clock now only resets via an explicit action (Reset Boss Timer hotkey, split
  reset, run reset). This logic is already game-agnostic in `MainViewModel.cs` -- no
  per-game porting work needed for it, it will apply automatically to any new game's
  data once that game's `OnBossHealthBarSpawn` actually fires.
- Relatedly, `ResetBossTimer()` was fixed to fully stop/clear tracking
  (`ClearBossTimerState()` + `BossKillTimeMs = null`) rather than zero-and-continue --
  the earlier zero-and-continue version left the clock live-ticking from 0 indefinitely
  if the player quit the boss area and hit reset without re-engaging. Also already
  game-agnostic, no per-game work needed.

## Sibling single-game tools -- reference check

Grepped `TarnishedTool` and `SilkySouls3` case-insensitively for HealthBar/BossHealth:
nothing usable in either. TarnishedTool has two incidental hits (an NPC name list entry
containing the word "Healthbar", and a README line describing TarnishedTool's own
self-drawn target-HP-bar UI overlay) -- neither touches the game's native healthbar-display
function or exposes an entity ID. SilkySouls3: zero hits. No AOB pattern to borrow from
either sibling repo for this specific hook.

## Suggested build order per game

Mirrors the Sekiro build shape:
1. RE the hook address for the current/latest tracked version (accept version gaps).
2. Shellcode (AsmScript entry + resx).
3. XBossHealthBarService.cs.
4. Wire into XModule.cs (InstallHook, tick-poll, actually invoke the event).
5. XCustomCodeOffsets.cs ring-buffer section.
6. Mine and add XBossEntityIds resx data plus GameModuleFactory registration.
7. Settle the SKBossTimeTrackersEnabled generalization question (once, not per game).
8. Live-test against a boss with a known/likely multi-phase healthbar (check for the two
   failure modes above) before considering the game's data "done."

### Key files (per game, X = DS3/DSR/DS2/EldenRing prefix)
- `AutoHitCounter/Games/{DS3,DSR,DS2,ER}/X Module.cs`
- `AutoHitCounter/Games/{DS3,DSR,DS2,ER}/X Offsets.cs`, `X Patterns.cs` (DS3/ER only)
- `AutoHitCounter/Games/{DS3,DSR,DS2,ER}/X CustomCodeOffsets.cs`
- `AutoHitCounter/Games/{DS3,DSR,DS2,ER}/X EventService.cs` (template to mirror)
- `AutoHitCounter/Enums/AsmScript.cs`
- `AutoHitCounter/Properties/Resources.resx`
- `AutoHitCounter/Services/GameModuleFactory.cs`, `AutoHitCounter/Interfaces/IGameModuleFactory.cs`
- `AutoHitCounter/ViewModels/MainViewModel.cs` (`HandleBossHealthBarSpawn` and friends --
  no changes expected, already game-agnostic)
- `AutoHitCounter/Views/Controls/SettingsTab.xaml`, `SettingsViewModel.cs`
  (`SKBossTimeTrackersEnabled` generalization)
