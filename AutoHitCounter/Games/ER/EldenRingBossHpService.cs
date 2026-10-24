//

using AutoHitCounter.Interfaces;
using AutoHitCounter.Models;
using static AutoHitCounter.Games.ER.EldenRingOffsets;

namespace AutoHitCounter.Games.ER;

// Poll-only, like EldenRingBossHealthBarService: reads the same CSFeMan boss gauge
// slots, resolves each active slot's entityHandle to its ChrIns, then reads
// ChrData health. Pointer chain and HP offsets are TarnishedTool's
// (ChrInsService.GetCurrentHp/GetMaxHp), live-confirmed on 1.17.1 (Malenia, 26250 max HP).
// Every read fails cleanly on a bad address.
public class EldenRingBossHpService(IMemoryService memoryService, EldenRingBossHealthBarService bossHealthBarService)
{
    public BossHealthSnapshot Read()
    {
        var csFeMan = memoryService.Read<nint>(CSFeMan.Base);
        if (csFeMan == 0) return BossHealthSnapshot.Inactive;

        var active = false;
        uint entityId = 0;
        long hp = 0;
        long maxHp = 0;

        for (var slot = 0; slot < CSFeMan.BossHealthDisplaySlotCount; slot++)
        {
            var slotBase = csFeMan + CSFeMan.BossHealthDisplaySlotBase + slot * CSFeMan.BossHealthDisplayStride;
            if (memoryService.Read<int>(slotBase + CSFeMan.BossHealthDisplayFmgId) == -1) continue;

            var entityHandle = memoryService.Read<int>(slotBase + CSFeMan.BossHealthDisplayEntityHandle);
            if (entityHandle == -1) continue;

            var chrIns = bossHealthBarService.ResolveChrIns(entityHandle);
            if (chrIns == 0) continue;

            var modules = memoryService.Read<nint>(chrIns + WorldChrMan.ChrIns.Modules);
            if (modules == 0) continue;

            var chrData = memoryService.Read<nint>(modules + WorldChrMan.ChrIns.ChrDataModule);
            if (chrData == 0) continue;

            var slotMax = memoryService.Read<int>(chrData + WorldChrMan.ChrIns.ChrDataMaxHealth);
            if (slotMax <= 0) continue;

            if (!active)
            {
                active = true;
                entityId = (uint)memoryService.Read<int>(chrIns + WorldChrMan.ChrIns.EntityId);
            }

            hp += memoryService.Read<int>(chrData + WorldChrMan.ChrIns.ChrDataHealth);
            maxHp += slotMax;
        }

        return active
            ? new BossHealthSnapshot(true, entityId, (int)hp, (int)maxHp)
            : BossHealthSnapshot.Inactive;
    }
}
