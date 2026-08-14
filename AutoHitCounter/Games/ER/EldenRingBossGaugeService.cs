//

using AutoHitCounter.Interfaces;
using static AutoHitCounter.Games.ER.EldenRingOffsets;

namespace AutoHitCounter.Games.ER;

// Poll-based cross-validation companion to the existing hook-based
// EldenRingBossHealthBarService -- see boss-entity-ids-data-mining.md for the full
// RE writeup this is built from (same session as DS3's DS3BossGaugeService, ER had
// full RTTI struct names throughout so no scalar-search guessing was needed).
// Reads CSFeManImp->bossHealthDisplays[0..2] directly instead of hooking anything.
// fieldInsHandle.entityHandle is resolved to a live ChrIns* via the exact same
// pure-read WorldChrMan.ChrSetPool walk TarnishedTool's own (already-tested)
// ChrInsService.ChrInsByHandle uses -- no native function call, no
// AllocateAndExecute, no address to AOB-scan for that step at all.
// ChrIns.EntityId is then read directly for the real entity ID.
//
// Not wired into the live matching pipeline yet -- see EldenRingModule.Tick(),
// which currently only logs this alongside the existing hook's output for live
// cross-validation before either replaces or is trusted alongside the hook.
public class EldenRingBossGaugeService(IMemoryService memoryService)
{
    // Defaults to 0, not -1: the very first tick after attach never reports a
    // spawn even if a boss gauge is already showing (tool attached mid-fight) --
    // only a genuine -1 -> active transition observed across two ticks counts,
    // matching the hook-based games' own "only real transitions" behavior.
    private readonly int[] _prevFmgId = new int[CSFeMan.BossHealthDisplaySlotCount];

    public bool TryGetLatestSpawn(out uint entityId)
    {
        entityId = 0;

        var csFeMan = memoryService.Read<nint>(CSFeMan.Base);
        if (csFeMan == 0) return false;

        for (var slot = 0; slot < CSFeMan.BossHealthDisplaySlotCount; slot++)
        {
            var slotBase = csFeMan + CSFeMan.BossHealthDisplaySlotBase + slot * CSFeMan.BossHealthDisplayStride;
            var fmgId = memoryService.Read<int>(slotBase + CSFeMan.BossHealthDisplayFmgId);

            var prevFmgId = _prevFmgId[slot];
            _prevFmgId[slot] = fmgId;

            if (prevFmgId != -1 || fmgId == -1) continue;

            var entityHandle = memoryService.Read<int>(slotBase + CSFeMan.BossHealthDisplayEntityHandle);
            if (entityHandle == -1) continue;

            if (TryResolveEntityId(entityHandle, out entityId)) return true;
        }

        return false;
    }

    private bool TryResolveEntityId(int entityHandle, out uint entityId)
    {
        entityId = 0;

        var poolIndex = (entityHandle >> 20) & 0xFF;
        var slotIndex = entityHandle & 0xFFFFF;

        var worldChrMan = memoryService.Read<nint>(WorldChrMan.Base);
        if (worldChrMan == 0) return false;

        var chrSet = memoryService.Read<nint>(worldChrMan + WorldChrMan.ChrSetPool + poolIndex * 8);
        if (chrSet == 0) return false;

        var entriesBase = memoryService.Read<nint>(chrSet + WorldChrMan.ChrSetEntries);
        if (entriesBase == 0) return false;

        var chrIns = memoryService.Read<nint>(entriesBase + slotIndex * 16);
        if (chrIns == 0) return false;

        entityId = (uint)memoryService.Read<int>(chrIns + WorldChrMan.ChrIns.EntityId);
        return true;
    }
}
