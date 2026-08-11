// 

using System;

namespace AutoHitCounter.Models;

public class RunSnapshot(int currentSplitIndex, int[] hitCounts, long?[] bossKillTimesMs, bool isRunComplete, TimeSpan inGameTime)
{
    public int CurrentSplitIndex { get; } = currentSplitIndex;
    public int[] HitCounts { get; } = hitCounts;
    public long?[] BossKillTimesMs { get; } = bossKillTimesMs;
    public bool IsRunComplete { get; } = isRunComplete;
    public TimeSpan InGameTime { get; } = inGameTime;
}