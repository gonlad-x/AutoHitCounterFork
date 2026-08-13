//

using System;
using AutoHitCounter.Enums;
using AutoHitCounter.Interfaces;
using AutoHitCounter.Memory;
using AutoHitCounter.Utilities;
using static AutoHitCounter.Games.ER.EldenRingCustomCodeOffsets;
using static AutoHitCounter.Games.ER.EldenRingOffsets;

namespace AutoHitCounter.Games.ER;

public class EldenRingBossHealthBarService(IMemoryService memoryService, HookManager hookManager)
{
    private const int RingSize = 512;

    private int _readIndex;

    public void InstallHook()
    {
        var code = Base + BossHealthBarCode;
        var bytes = AsmLoader.GetAsmBytes(AsmScript.EldenRingBossHealthBarLog);
        var writeIndex = Base + BossHealthBarWriteIdx;
        var buffer = Base + BossHealthBarBuffer;
        var hookLoc = Hooks.DisplayBossHealthBar;

        // Layout differs from DS3/Sekiro's template by a leading `mov r10d, [rcx]`
        // (3 bytes) -- ER's entity ID arrives as a pointer-to-int in RCX, not the
        // value directly (confirmed live 2026-08-13: capturing RCX's raw bits
        // produced a garbage ~2.1 billion "entity ID"). Dereferencing into R10D
        // rather than RCX itself is deliberate: the function's own very next
        // instruction (`MOV RDI, RCX`) needs the original pointer intact, or the
        // game's own boss-health-bar display breaks along with our capture.
        AsmHelper.WriteRelativeOffsets(bytes, [
            (code + 0x5, writeIndex, 6, 0x5 + 2),
            (code + 0xB, buffer, 7, 0xB + 3),
            (code + 0x21, writeIndex, 6, 0x21 + 2),
            (code + 0x2E, hookLoc + 5, 5, 0x2E + 1)
        ]);

        memoryService.WriteBytes(code, bytes);
        hookManager.InstallHook(code, hookLoc, [0x48, 0x89, 0x5C, 0x24, 0x08]);
    }

    // Restarting the timer from the latest spawn is the desired behavior (a retry
    // before the kill flag fires should reset the clock), so only the most recent
    // ring entry since the last poll is returned -- older entries in between are
    // deliberately discarded, not queued.
    public bool TryGetLatestSpawn(out uint entityId)
    {
        entityId = 0;
        var writeIndex = memoryService.Read<int>(Base + BossHealthBarWriteIdx);
        if (writeIndex == _readIndex) return false;

        var entriesToRead = (writeIndex - _readIndex) & (RingSize - 1);
        var dataBytes = ReadWrapping(entriesToRead);

        entityId = BitConverter.ToUInt32(dataBytes, (entriesToRead - 1) * 4);
        _readIndex = writeIndex;
        return true;
    }

    private byte[] ReadWrapping(int entriesToRead)
    {
        var logBuffer = Base + BossHealthBarBuffer;
        var tail = RingSize - _readIndex;
        if (entriesToRead <= tail)
            return memoryService.ReadBytes(logBuffer + (_readIndex * 4), entriesToRead * 4);

        var part1 = memoryService.ReadBytes(logBuffer + (_readIndex * 4), tail * 4);
        var part2 = memoryService.ReadBytes(logBuffer, (entriesToRead - tail) * 4);
        var result = new byte[entriesToRead * 4];
        part1.CopyTo(result, 0);
        part2.CopyTo(result, part1.Length);
        return result;
    }
}
