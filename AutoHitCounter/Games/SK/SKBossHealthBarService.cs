//

using AutoHitCounter.Interfaces;
using static AutoHitCounter.Games.SK.SKOffsets;

namespace AutoHitCounter.Games.SK;

// Poll-based, not hook-based -- see project notes on why. In short: a poll can
// never crash the game (a bad address just fails the read cleanly), unlike a
// hook (a wrong or prematurely-installed hook can execute garbage as code).
// Live-tested and confirmed matching the previous hook-based implementation
// across real fights (see boss-entity-ids-data-mining.md for the full RE
// writeup and cross-validation session this is built from).
//
// Polls two separate 3-slot arrays -- BossGauge (majors, MenuMan+0x2E10) and
// MiniBossGauge (minibosses, MenuMan+0x2C08). These are NOT the same array:
// live testing 2026-08-16 showed minibosses never write into BossGauge at all
// (an earlier assumption, based only on both firing through the same hooked
// native function, turned out wrong). MiniBossGauge was found via its own
// slot-accessor function (FUN_1408c51a0), the miniboss-side sibling of
// BossGauge's FUN_1408c4c80 -- same 0xA8 stride/3-slot bounds check, same
// NameId(+0x0)/Handle(+0x4) slot layout, just a different base offset.
//
// Each array: NameId != -1 means the slot is active, and a -1 -> real-value
// transition means a gauge just appeared. Handle is a FieldInsSelector (a
// packed block-index/instance-index value shared engine-wide across
// DS3/Sekiro/ER, not an opaque handle -- numerically identical decode
// constants to DS3's own table here), resolved to a live ChrIns* via a pure
// WorldChrManImp struct-walk that replicates the native resolver's own logic
// (CS::WorldChrManImp::GetChrSetEntryFromHandle) rather than calling it -- no
// native function address ever needs resolving or AOB-scanning for this path.
// ChrIns.EntityId is then read directly for the real entity ID.
public class SKBossHealthBarService(IMemoryService memoryService)
{
    // Defaults to 0, not -1: the very first tick after attach never reports a
    // spawn even if a gauge is already showing (tool attached mid-fight) --
    // only a genuine -1 -> active transition observed across two ticks counts.
    private readonly int[] _prevBossNameId = new int[MenuMan.BossGaugeSlotCount];
    private readonly int[] _prevMiniBossNameId = new int[MenuMan.BossGaugeSlotCount];

    public bool TryGetLatestSpawn(out uint entityId)
    {
        entityId = 0;

        var menuMan = memoryService.Read<nint>(MenuMan.Base);
        if (menuMan == 0) return false;

        if (TryScanGauge(menuMan, MenuMan.BossGaugeSlotBase, _prevBossNameId, out entityId)) return true;
        if (TryScanGauge(menuMan, MenuMan.MiniBossGaugeSlotBase, _prevMiniBossNameId, out entityId)) return true;

        return false;
    }

    private bool TryScanGauge(nint menuMan, int gaugeSlotBase, int[] prevNameId, out uint entityId)
    {
        entityId = 0;

        for (var slot = 0; slot < MenuMan.BossGaugeSlotCount; slot++)
        {
            var slotBase = menuMan + gaugeSlotBase + slot * MenuMan.BossGaugeStride;
            var nameId = memoryService.Read<int>(slotBase + MenuMan.BossGaugeNameId);

            if (nameId == -1)
            {
                prevNameId[slot] = -1;
                continue;
            }

            if (prevNameId[slot] != -1) continue;

            // NameId and Handle aren't necessarily written in the same tick --
            // if Handle isn't ready yet, leave prevNameId[slot] at -1 (don't
            // mark this activation as seen) so the next tick retries instead of
            // permanently missing the spawn.
            var handle = memoryService.Read<int>(slotBase + MenuMan.BossGaugeHandle);
            if (handle == -1) continue;

            if (!TryResolveEntityId(handle, out entityId)) continue;

            prevNameId[slot] = nameId;
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
