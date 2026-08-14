//

using AutoHitCounter.Interfaces;
using static AutoHitCounter.Games.DSR.DSROffsets;

namespace AutoHitCounter.Games.DSR;

// Poll-based, not hook-based -- see project notes on why. In short: DSR's "show boss
// health bar" logic isn't one shared function the way DS3/Sekiro/ER's is (it turned out
// to be scattered across many small per-boss/per-scene functions), so there's no single
// good hook point. MenuMan->BossGauge[0..1], however, is a plain, static, resolvable
// struct, so this reads it directly each tick instead: NameId != -1 means the slot is
// active, and a -1 -> real-value transition means a boss gauge just appeared. From
// there, Handle is resolved back to an EntityId by scanning the loaded WorldBlockChr
// entity tables (the same data WorldBlockChr_GetHandleFromChrId binary-searches for the
// EntityId -> Handle direction; this does the reverse via a linear scan since the array
// is sorted by the other field).
public class DSRBossHealthBarService(IMemoryService memoryService)
{
    // Defaults to 0, not -1: this deliberately means the very first tick after attach
    // never reports a spawn even if a boss gauge is already showing (tool attached
    // mid-fight) -- only a genuine -1 -> active transition observed across two ticks
    // counts, matching the DS3/Sekiro/ER hooks' own "only real transitions" behavior.
    private readonly int[] _prevNameId = new int[MenuMan.BossGaugeSlotCount];

    public bool TryGetLatestSpawn(out uint entityId)
    {
        entityId = 0;

        var menuMan = memoryService.Read<nint>(MenuMan.Base);
        if (menuMan == 0) return false;

        for (var slot = 0; slot < MenuMan.BossGaugeSlotCount; slot++)
        {
            var slotBase = menuMan + MenuMan.BossGaugeSlotBase + slot * MenuMan.BossGaugeStride;
            var nameId = memoryService.Read<int>(slotBase + MenuMan.BossGaugeNameId);

            var prevNameId = _prevNameId[slot];
            _prevNameId[slot] = nameId;

            if (prevNameId != -1 || nameId == -1) continue;

            var handle = memoryService.Read<int>(slotBase + MenuMan.BossGaugeHandle);
            if (handle == -1) continue;

            if (TryResolveEntityId(handle, out entityId)) return true;
        }

        return false;
    }

    private bool TryResolveEntityId(int handle, out uint entityId)
    {
        entityId = 0;

        var worldChrManImp = memoryService.Read<nint>(WorldChrMan.Base);
        if (worldChrManImp == 0) return false;

        var blockCount = memoryService.Read<int>(worldChrManImp + WorldChrMan.NumLoadedWorldBlockChrs);
        for (var i = 0; i < blockCount; i++)
        {
            var blockPtr = memoryService.Read<nint>(worldChrManImp + WorldChrMan.WorldBlockChr0 + i * 8);
            if (blockPtr == 0) continue;

            var entryCount = memoryService.Read<int>(blockPtr + WorldBlockChr.Count);
            var entriesPtr = memoryService.Read<nint>(blockPtr + WorldBlockChr.Entries);
            if (entriesPtr == 0) continue;

            for (var e = 0; e < entryCount; e++)
            {
                var entryAddr = entriesPtr + e * WorldBlockChr.EntryStride;
                var entryHandle = memoryService.Read<int>(entryAddr + WorldBlockChr.EntryHandle);
                if (entryHandle != handle) continue;

                entityId = (uint)memoryService.Read<int>(entryAddr + WorldBlockChr.EntryEntityId);
                return true;
            }
        }

        return false;
    }
}
