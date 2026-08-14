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

    // DS2 has no per-boss identifier reachable from the boss-gauge struct (see
    // DS2Offsets.cs/DS2BossGaugeService.cs comments) -- unlike OnBossHealthBarSpawn,
    // this carries no entity ID and always means "attribute this to CurrentSplit",
    // the same semantics MainViewModel's manual ToggleBossTimer hotkey already uses.
    event Action OnBossGaugeActivated;

    void UpdateEvents(Dictionary<uint, (string Name, int Required, int Hit)> events);
    void ApplySettings(bool onlyEnabled = false);
    void SetEventLogEnabled(bool enabled);
}