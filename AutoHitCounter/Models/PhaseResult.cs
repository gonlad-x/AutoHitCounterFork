//

namespace AutoHitCounter.Models;

// One completed boss phase (a "lap"): the hits taken and the real-time elapsed
// during that phase. Phases partition a boss split's total -- Hits sums to the
// split's NumOfHits and TimeMs sums to its BossKillTimeMs by construction (each
// is a slice derived by subtracting the running total at the phase boundary; see
// MainViewModel's phase bookkeeping). Persisted per split in RunState/RunSnapshot.
public class PhaseResult
{
    public int Hits { get; set; }
    public long TimeMs { get; set; }
    // A transition segment is the downtime between forms (Genichiro's transform, Guardian
    // Ape's death->revive) rather than a fightable phase. Rendered as a dimmer sub-row.
    public bool IsTransition { get; set; }
}
