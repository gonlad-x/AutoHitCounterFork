// 

namespace AutoHitCounter.Models;

public class RunState
{
    public int CurrentSplitIndex { get; set; }
    public int[] HitCounts { get; set; }
    public long?[] BossKillTimesMs { get; set; }
    // One completed-phase array per child split (jagged). Null when saved by a build
    // before this field existed; an empty inner array means "no phases for that split".
    public PhaseResult[][] PhaseResults { get; set; }
    public bool IsRunComplete { get; set; }
    public long IgtMilliseconds { get; set; }
}