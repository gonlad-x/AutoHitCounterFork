//

using System;
using System.Collections.Generic;
using AutoHitCounter.Enums;
using AutoHitCounter.Interfaces;
using AutoHitCounter.Models;
using AutoHitCounter.Utilities;
using DiscordRPC;

namespace AutoHitCounter.Services;

// Elden Ring only for now. Shows "Stalling" while attached and no boss gauge is up,
// and the boss / fight timer / hits / HP% during a fight.
//
// Fully independent of profiles and splits: the fight is detected from the live boss
// gauge (BossHealthChanged), the name comes from the boss entity ID tables, and hits
// are counted from HitReceived while a fight is active.
//
// Discord's rate limit for presence updates isn't documented anywhere official, so
// content is only re-sent when the displayed text actually changed, and never more
// often than MinSendInterval.
// The fight timer costs no updates at all: Discord counts it from a start timestamp.
public class DiscordPresenceService : IDisposable
{
    // Application ID from the Discord Developer Portal. Empty disables the feature.
    private const string ClientId = "1563517573549789254";

    // Name of the large image asset uploaded to that application (Rich Presence > Art Assets).
    private const string LargeImageKey = "eldenring";

    // Width of the HP bar in the presence text.
    private const int BarCells = 12;

    private static readonly TimeSpan MinSendInterval = TimeSpan.FromSeconds(2);

    // Phase swaps (Radagon -> Elden Beast, ...) briefly drop the gauge; don't end the fight for that.
    private static readonly TimeSpan FightEndGrace = TimeSpan.FromSeconds(3);

    private readonly IGameSessionOrchestrator _orchestrator;
    private readonly IGameModuleFactory _gameModuleFactory;

    private DiscordRpcClient _client;
    private Dictionary<uint, string> _bossNamesByEntityId;

    private bool _fightActive;
    private DateTime _fightStartUtc;
    private DateTime _lastBossSeenUtc;
    private uint _entityId;
    private int _hits;
    private int _hpCells = BarCells;
    private int _hpPercent = 100;

    private string _sentDetails;
    private string _sentState;
    private DateTime? _sentStart;
    private bool _hasSent;
    private DateTime _lastSendUtc = DateTime.MinValue;

    public DiscordPresenceService(IGameSessionOrchestrator orchestrator, IGameModuleFactory gameModuleFactory)
    {
        _orchestrator = orchestrator;
        _gameModuleFactory = gameModuleFactory;

        _orchestrator.BossHealthChanged += OnBossHealthChanged;
        _orchestrator.HitReceived += OnHitReceived;
        _orchestrator.GameUnloaded += OnGameUnloaded;
        _orchestrator.AttachmentChanged += OnAttachmentChanged;
    }

    private bool ShouldShow =>
        ClientId.Length > 0
        && SettingsManager.Default.ERDiscordPresenceEnabled
        && _orchestrator.IsAttached
        && _orchestrator.ActiveGame?.Title == GameTitle.EldenRing;

    private void OnBossHealthChanged(BossHealthSnapshot snapshot)
    {
        if (!ShouldShow)
        {
            Shutdown();
            return;
        }

        var now = DateTime.UtcNow;

        if (snapshot.IsActive)
        {
            if (!_fightActive)
            {
                _fightActive = true;
                _fightStartUtc = now;
                _hits = 0;
            }

            _lastBossSeenUtc = now;
            _entityId = snapshot.EntityId;
            _hpCells = ScaleHp(snapshot, BarCells);
            _hpPercent = ScaleHp(snapshot, 100);
        }
        else if (_fightActive && now - _lastBossSeenUtc > FightEndGrace)
        {
            _fightActive = false;
        }

        Publish(now);
    }

    private void OnHitReceived()
    {
        if (_fightActive) _hits++;
    }

    private void OnGameUnloaded() => _fightActive = false;

    private void OnAttachmentChanged()
    {
        if (_orchestrator.IsAttached) return;
        _fightActive = false;
        Shutdown();
    }

    private void Publish(DateTime now)
    {
        string details;
        string state = null;
        DateTime? start = null;

        if (_fightActive)
        {
            details = $"{GetBossName(_entityId)}";
            state = $"{BuildBar(_hpCells)} {_hpPercent}% | {_hits} {(_hits == 1 ? "hit" : "hits")}";
            start = _fightStartUtc;
        }
        else
        {
            details = "Stalling";
        }

        if (_hasSent && details == _sentDetails && state == _sentState && start == _sentStart) return;
        if (now - _lastSendUtc < MinSendInterval) return;

        _client ??= CreateClient();

        _client.SetPresence(new RichPresence
        {
            Details = details,
            State = state,
            Timestamps = start.HasValue ? new Timestamps { Start = start.Value } : null,
            Assets = new Assets { LargeImageKey = LargeImageKey, LargeImageText = "Elden Ring" },
        });

        _sentDetails = details;
        _sentState = state;
        _sentStart = start;
        _hasSent = true;
        _lastSendUtc = now;
    }

    private static DiscordRpcClient CreateClient()
    {
        var client = new DiscordRpcClient(ClientId);
        client.Initialize();
        return client;
    }

    // Scales HP to 0..scale, rounded down, so anything below full health never reads as full.
    // Any HP left keeps at least 1.
    private static int ScaleHp(BossHealthSnapshot snapshot, int scale)
    {
        if (snapshot.MaxHp <= 0 || snapshot.Hp <= 0) return 0;
        if (snapshot.Hp >= snapshot.MaxHp) return scale;
        return Math.Max(1, (int)((long)snapshot.Hp * scale / snapshot.MaxHp));
    }

    private static string BuildBar(int filledCells) =>
        new string('▰', filledCells) + new string('▱', BarCells - filledCells);

    private string GetBossName(uint entityId)
    {
        _bossNamesByEntityId ??= BuildBossNames();
        return _bossNamesByEntityId.TryGetValue(entityId, out var name) ? name : "a boss";
    }

    // entity ID -> death flag (boss entity ID table) -> boss name (events table).
    private Dictionary<uint, string> BuildBossNames()
    {
        var names = new Dictionary<uint, string>();
        var flagNames = _gameModuleFactory.GetEventsForGame(GameTitle.EldenRing);
        foreach (var pair in _gameModuleFactory.GetBossEntityIdsForGame(GameTitle.EldenRing))
        {
            if (!flagNames.TryGetValue(pair.Key, out var name)) continue;
            foreach (var entityId in pair.Value) names[entityId] = name;
        }

        return names;
    }

    private void Shutdown()
    {
        if (_client == null) return;

        try
        {
            _client.ClearPresence();
            _client.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Discord presence shutdown failed: {ex.Message}");
        }

        _client = null;
        _hasSent = false;
        _lastSendUtc = DateTime.MinValue;
    }

    public void Dispose()
    {
        _orchestrator.BossHealthChanged -= OnBossHealthChanged;
        _orchestrator.HitReceived -= OnHitReceived;
        _orchestrator.GameUnloaded -= OnGameUnloaded;
        _orchestrator.AttachmentChanged -= OnAttachmentChanged;
        Shutdown();
    }
}
