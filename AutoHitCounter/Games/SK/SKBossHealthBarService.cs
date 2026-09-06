//

using AutoHitCounter.Interfaces;
using static AutoHitCounter.Games.SK.SKOffsets;

namespace AutoHitCounter.Games.SK;

// Poll-based, not hook-based -- a bad read fails cleanly; a bad hook can execute garbage.
//
// Polls two separate 3-slot arrays -- boss health bars (MenuMan+0x2E10) and miniboss
// health bars (MenuMan+0x2C08), each slot NameId(+0x0)/Handle(+0x4), 0xA8 stride, 3 slots.
// Handle is a FieldInsSelector resolved to a live ChrIns* by a pure WorldChrManImp walk.
//
// Two things are exposed for the boss timer + phase sub-splits:
//   * TryGetLatestSpawn -- a NEW form appeared in a slot. Handle-based (not NameId-based)
//     so it fires both on a bar appearing from nothing (start / Guardian Ape revive, where
//     the bar hides then re-shows the same handle) AND on a slot's entity changing directly
//     with no gap (Genichiro Castle 1110800 -> 1110801, which never blanks the slot).
//   * TryGetNodeChange -- a boss's deathblow/phase node (ChrData+0x25C) decremented. This is
//     the deathblow signal (see SKOffsets.ChrIns.BossNode). MainViewModel runs the phase
//     state machine off these two.
public class SKBossHealthBarService(IMemoryService memoryService)
{
    // Spawn detection: last handle reported per slot. Reset to -1 when the slot blanks, so a
    // re-show of the same handle (Ape revive) fires again; MainViewModel dedupes repeats.
    private readonly int[] _spawnBossHandle = NewNegOne();
    private readonly int[] _spawnMiniBossHandle = NewNegOne();

    // Node detection: last handle + last node per slot. NOT reset when the slot blanks, so the
    // node survives a brief bar-hide (Ape's collapse) and the revive's 0->1 reset is seen as an
    // increment (ignored), with the next 1->0 correctly detected as a decrement.
    private readonly int[] _nodeBossHandle = NewNegOne();
    private readonly int[] _nodeBossValue = new int[MenuMan.BossGaugeSlotCount];
    private readonly int[] _nodeMiniBossHandle = NewNegOne();
    private readonly int[] _nodeMiniBossValue = new int[MenuMan.BossGaugeSlotCount];

    private static int[] NewNegOne()
    {
        var arr = new int[MenuMan.BossGaugeSlotCount];
        for (var i = 0; i < arr.Length; i++) arr[i] = -1;
        return arr;
    }

    public bool TryGetLatestSpawn(out uint entityId)
    {
        entityId = 0;
        var menuMan = memoryService.Read<nint>(MenuMan.Base);
        if (menuMan == 0) return false;

        if (TryScanSpawn(menuMan, MenuMan.BossGaugeSlotBase, _spawnBossHandle, out entityId)) return true;
        if (TryScanSpawn(menuMan, MenuMan.MiniBossGaugeSlotBase, _spawnMiniBossHandle, out entityId)) return true;
        return false;
    }

    private bool TryScanSpawn(nint menuMan, int gaugeSlotBase, int[] prevHandle, out uint entityId)
    {
        entityId = 0;
        for (var slot = 0; slot < MenuMan.BossGaugeSlotCount; slot++)
        {
            var slotBase = menuMan + gaugeSlotBase + slot * MenuMan.BossGaugeStride;
            var nameId = memoryService.Read<int>(slotBase + MenuMan.BossGaugeNameId);
            if (nameId == -1)
            {
                prevHandle[slot] = -1;
                continue;
            }

            var handle = memoryService.Read<int>(slotBase + MenuMan.BossGaugeHandle);
            if (handle == -1) continue;           // not ready yet -- retry next tick
            if (handle == prevHandle[slot]) continue; // already reported this form
            if (!TryResolveEntityId(handle, out entityId)) continue;

            prevHandle[slot] = handle;
            return true;
        }
        return false;
    }

    // A boss's phase node decremented. Reports the entity and its NEW node value:
    //   newNode >= 1 -> a same-bar deathblow (phase boundary),
    //   newNode == 0 -> the form ended (arm a transition; the real kill is closed by the
    //                   defeat flag, a revive/entity-swap is caught by the spawn path).
    public bool TryGetNodeChange(out uint entityId, out int newNode)
    {
        entityId = 0;
        newNode = 0;
        var menuMan = memoryService.Read<nint>(MenuMan.Base);
        if (menuMan == 0) return false;

        if (TryScanNode(menuMan, MenuMan.BossGaugeSlotBase, _nodeBossHandle, _nodeBossValue, out entityId, out newNode)) return true;
        if (TryScanNode(menuMan, MenuMan.MiniBossGaugeSlotBase, _nodeMiniBossHandle, _nodeMiniBossValue, out entityId, out newNode)) return true;
        return false;
    }

    private bool TryScanNode(nint menuMan, int gaugeSlotBase, int[] prevHandle, int[] prevNode, out uint entityId, out int newNode)
    {
        entityId = 0;
        newNode = 0;
        for (var slot = 0; slot < MenuMan.BossGaugeSlotCount; slot++)
        {
            var slotBase = menuMan + gaugeSlotBase + slot * MenuMan.BossGaugeStride;
            var nameId = memoryService.Read<int>(slotBase + MenuMan.BossGaugeNameId);
            if (nameId == -1) continue;           // slot blanked -- keep prev so node survives a hide

            var handle = memoryService.Read<int>(slotBase + MenuMan.BossGaugeHandle);
            if (handle == -1) continue;
            if (!TryResolveChrIns(handle, out var chrIns)) continue;

            var modules = memoryService.Read<nint>(chrIns + WorldChrMan.ChrIns.Modules);
            if (modules == 0) continue;
            var dataModule = memoryService.Read<nint>(modules + WorldChrMan.ChrIns.ChrDataModule);
            if (dataModule == 0) continue;
            var node = memoryService.Read<int>(dataModule + WorldChrMan.ChrIns.BossNode);

            if (prevHandle[slot] != handle)       // new form in this slot -> baseline only
            {
                prevHandle[slot] = handle;
                prevNode[slot] = node;
                continue;
            }

            var prev = prevNode[slot];
            prevNode[slot] = node;
            if (node < prev)                      // a deathblow was consumed
            {
                entityId = (uint)memoryService.Read<int>(chrIns + WorldChrMan.ChrIns.EntityId);
                newNode = node;
                return true;
            }
        }
        return false;
    }

    private bool TryResolveEntityId(int handle, out uint entityId)
    {
        entityId = 0;
        if (!TryResolveChrIns(handle, out var chrIns)) return false;
        entityId = (uint)memoryService.Read<int>(chrIns + WorldChrMan.ChrIns.EntityId);
        return true;
    }

    private bool TryResolveChrIns(int handle, out nint chrIns)
    {
        chrIns = 0;

        // FieldInsSelector decode -- top 4 bits select the category (CHR = 1).
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

        chrIns = memoryService.Read<nint>(entriesPtr + (nint)fieldInsIndex * WorldBlockChr.EntryStride);
        return chrIns != 0;
    }
}
