//

using AutoHitCounter.Interfaces;
using static AutoHitCounter.Games.SK.SKOffsets;

namespace AutoHitCounter.Games.SK;

// Poll-based cross-validation companion to the existing hook-based
// SKBossHealthBarService -- see boss-entity-ids-data-mining.md for the full RE
// writeup this is built from (same session as DS3/ER's own poll-based services;
// Sekiro's resolution turned out numerically identical to DS3's -- same
// WorldChrManImp+0x518 block array, same 0x38-byte entry stride, same
// FieldInsMapping CHR-category shift/mask constants). Reads
// MenuMan->BossGauge[0..2] directly instead of hooking anything. Handle is
// resolved to a live ChrIns* via a pure WorldChrManImp struct-walk that
// replicates CS::WorldChrManImp::GetChrSetEntryFromHandle's own logic rather than
// calling it, so no native function address ever needs resolving or AOB-scanning
// for this path. ChrIns.EntityId is then read directly for the real entity ID.
//
// Not wired into the live matching pipeline yet -- see SKModule.Tick(), which
// currently only logs this alongside the existing hook's output for live
// cross-validation before either replaces or is trusted alongside the hook.
public class SKBossGaugeService(IMemoryService memoryService)
{
    // Defaults to 0, not -1: the very first tick after attach never reports a
    // spawn even if a boss gauge is already showing (tool attached mid-fight) --
    // only a genuine -1 -> active transition observed across two ticks counts,
    // matching the hook-based games' own "only real transitions" behavior.
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

        // FieldInsSelector decode -- top 4 bits select the category (CHR = 1);
        // anything else isn't a character and can't resolve to a ChrIns at all.
        var mapType = (uint)handle >> 28;
        if (mapType != FieldInsMapping.ChrMapType) return false;

        var blockIndex = ((uint)handle >> FieldInsMapping.ChrBlockIndexShift) & FieldInsMapping.ChrBlockIndexMask;
        var fieldInsIndex = (uint)handle & FieldInsMapping.ChrFieldInsIndexMask;

        var worldChrManImp = memoryService.Read<nint>(WorldChrMan.Base);
        if (worldChrManImp == 0) return false;

        var blockPtr = memoryService.Read<nint>(
            worldChrManImp + WorldChrMan.WorldBlockChr0 + (nint)blockIndex * 8);
        if (blockPtr == 0) return false;

        var count = memoryService.Read<int>(blockPtr + WorldBlockChr.Count);
        if (fieldInsIndex >= count) return false;

        var entriesPtr = memoryService.Read<nint>(blockPtr + WorldBlockChr.Entries);
        if (entriesPtr == 0) return false;

        var chrIns = memoryService.Read<nint>(entriesPtr + (nint)fieldInsIndex * WorldBlockChr.EntryStride);
        if (chrIns == 0) return false;

        entityId = (uint)memoryService.Read<int>(chrIns + WorldChrMan.ChrIns.EntityId);
        return true;
    }
}
