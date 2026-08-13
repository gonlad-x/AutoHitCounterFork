//

using System;
using AutoHitCounter.Enums;
using AutoHitCounter.Interfaces;
using AutoHitCounter.Memory;
using AutoHitCounter.Utilities;
using static AutoHitCounter.Games.DS3.DS3CustomCodeOffsets;
using static AutoHitCounter.Games.DS3.DS3Offsets;

namespace AutoHitCounter.Games.DS3;

public class DS3BossHealthBarService(IMemoryService memoryService, HookManager hookManager)
{
    private const int RingSize = 512;

    private int _readIndex;

    public void InstallHook()
    {
        var code = Base + BossHealthBarCode;
        var bytes = AsmLoader.GetAsmBytes(AsmScript.DS3BossHealthBarLog);
        var writeIndex = Base + BossHealthBarWriteIdx;
        var buffer = Base + BossHealthBarBuffer;
        var hookLoc = Hooks.DisplayBossHealthBar;

        AsmHelper.WriteRelativeOffsets(bytes, [
            (code + 0x2, writeIndex, 6, 0x2 + 2),
            (code + 0x8, buffer, 7, 0x8 + 3),
            (code + 0x1D, writeIndex, 6, 0x1D + 2),
            (code + 0x2A, hookLoc + 5, 5, 0x2A + 1)
        ]);

        memoryService.WriteBytes(code, bytes);
        hookManager.InstallHook(code, hookLoc, [0x48, 0x89, 0x5C, 0x24, 0x10]);
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
