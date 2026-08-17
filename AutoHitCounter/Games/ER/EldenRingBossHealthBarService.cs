//

using AutoHitCounter.Interfaces;
using static AutoHitCounter.Games.ER.EldenRingOffsets;

namespace AutoHitCounter.Games.ER;

// Poll-based, not hook-based -- see project notes on why. In short: a poll can
// never crash the game (a bad address just fails the read cleanly), unlike a
// hook (a wrong or prematurely-installed hook can execute garbage as code).
// Live-tested and confirmed matching the previous hook-based implementation
// across real fights, both open-field and Legacy Dungeon (see
// boss-entity-ids-data-mining.md for the full RE writeup and cross-validation
// session this is built from).
//
// Reads CSFeManImp->bossHealthDisplays[0..2] directly: fmgId != -1 means the
// slot is active, and a -1 -> real-value transition means a boss gauge just
// appeared. fieldInsHandle.entityHandle is resolved to a live ChrIns* via the
// exact same pure-read WorldChrMan.ChrSetPool walk TarnishedTool's own
// (already-tested) ChrInsService.ChrInsByHandle uses -- no native function
// call, no AllocateAndExecute, no address to AOB-scan for that step at all.
// ChrIns.EntityId is then read directly for the real entity ID.
public class EldenRingBossHealthBarService(IMemoryService memoryService)
{
    // Defaults to 0, not -1: the very first tick after attach never reports a
    // spawn even if a boss gauge is already showing (tool attached mid-fight) --
    // only a genuine -1 -> active transition observed across two ticks counts.
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

            if (fmgId == -1)
            {
                _prevFmgId[slot] = -1;
                continue;
            }

            if (_prevFmgId[slot] != -1) continue;

            // fmgId and entityHandle aren't necessarily written in the same
            // tick -- if entityHandle isn't ready yet, leave _prevFmgId[slot]
            // at -1 (don't mark this activation as seen) so the next tick
            // retries instead of permanently missing the spawn.
            var entityHandle = memoryService.Read<int>(slotBase + CSFeMan.BossHealthDisplayEntityHandle);
            if (entityHandle == -1) continue;

            if (!TryResolveEntityId(entityHandle, out entityId)) continue;

            _prevFmgId[slot] = fmgId;
            return true;
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
