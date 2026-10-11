// 

using System;
using System.Collections.Generic;
using AutoHitCounter.Models;

namespace AutoHitCounter.Interfaces;

public interface IGameModule
{
    event Action OnHit;
    event Action OnEventSet;
    event Action<List<EventLogEntry>> OnEventLogEntriesReceived;
    event Action<long> OnTimeChanged;
    event Action OnRunStart;
    event Action<uint> OnBossHealthBarSpawn;

    // Fires when a displayed boss's deathblow/phase node (ChrData+0x25C) decrements.
    // Carries (entityId, newNode): newNode>=1 is a same-bar deathblow, newNode==0 means
    // the form ended (revive/transform/kill). Only Sekiro raises it today; the other
    // modules implement it as an inert no-op (interface conformance).
    event Action<uint, int> OnBossNodeChanged;

    // DS2 has no per-boss identifier reachable from the boss-gauge struct (see
    // DS2Offsets.cs/DS2BossGaugeService.cs comments) -- unlike OnBossHealthBarSpawn,
    // this carries no entity ID and always means "attribute this to CurrentSplit",
    // the same semantics MainViewModel's manual ToggleBossTimer hotkey already uses.
    event Action OnBossGaugeActivated;

    // Fires exactly once on the tick IsLoaded() transitions from true to false --
    // i.e. the player just left the game world (quitout to title, or any other
    // full unload). Used to reset an actively-running boss timer rather than
    // leaving it ticking against a stale reference once the world reloads.
    event Action OnGameUnloaded;

    void UpdateEvents(Dictionary<uint, (string Name, int Required, int Hit)> events);
    void ApplySettings(bool onlyEnabled = false);
    void SetEventLogEnabled(bool enabled);
}