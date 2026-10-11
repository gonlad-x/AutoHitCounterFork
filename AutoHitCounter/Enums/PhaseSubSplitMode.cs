//

namespace AutoHitCounter.Enums;

// Per-split override for the global "Sub-split on boss phases" setting.
// Inherit  -> follow the game's global SKPhaseSubSplitEnabled flag (default).
// On/Off   -> force phase sub-splitting on or off for this specific boss,
//             regardless of the global.
public enum PhaseSubSplitMode
{
    Inherit,
    On,
    Off
}
