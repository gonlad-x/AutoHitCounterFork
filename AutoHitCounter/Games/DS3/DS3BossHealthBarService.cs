//

using AutoHitCounter.Interfaces;
using static AutoHitCounter.Games.DS3.DS3Offsets;

namespace AutoHitCounter.Games.DS3;

// Poll-based, not hook-based -- see project notes on why. In short: a poll can
// never crash the game (a bad address just fails the read cleanly), unlike a
// hook (a wrong or prematurely-installed hook can execute garbage as code).
// Live-tested and confirmed matching the previous hook-based implementation
// across real fights (including a multi-entity duo, Crucible Knight & Ordovis --
// see boss-entity-ids-data-mining.md for that cross-validation session and the
// full RE writeup this is built from).
//
// Reads MenuMan->BossGauge[0..2] directly: NameId != -1 means the slot is
// active, and a -1 -> real-value transition means a boss gauge just appeared.
// Handle is a FieldInsSelector (a packed block-index/instance-index value
// shared engine-wide across DS3/Sekiro/ER, not an opaque handle), decoded here
// for the CHR category and resolved to a live ChrIns* via a pure
// WorldChrManImp struct-walk that replicates the native resolver's own logic
// rather than calling it -- no native function address ever needs resolving
// or AOB-scanning for this path. ChrIns.EntityId is then read directly for the
// real entity ID.
public class DS3BossHealthBarService(IMemoryService memoryService)
{
    // Defaults to 0, not -1: the very first tick after attach never reports a
    // spawn even if a boss gauge is already showing (tool attached mid-fight) --
    // only a genuine -1 -> active transition observed across two ticks counts.
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

            if (nameId == -1)
            {
                _prevNameId[slot] = -1;
                continue;
            }

            if (_prevNameId[slot] != -1) continue;

            // NameId and Handle aren't necessarily written in the same tick --
            // if Handle isn't ready yet, leave _prevNameId[slot] at -1 (don't
            // mark this activation as seen) so the next tick retries instead of
            // permanently missing the spawn.
            var handle = memoryService.Read<int>(slotBase + MenuMan.BossGaugeHandle);
            if (handle == -1) continue;

            if (!TryResolveEntityId(handle, out entityId)) continue;

            _prevNameId[slot] = nameId;
            return true;
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
