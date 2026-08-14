//

using AutoHitCounter.Interfaces;
using static AutoHitCounter.Games.DS2.DS2Offsets;

namespace AutoHitCounter.Games.DS2;

// Poll-based, no hook -- same lesson as DSR's boss-timer feature: FeOperatorFrontend's
// boss-gauge array is a plain, resolvable struct the game itself already maintains for
// its own UI, so this reads it directly each tick instead of hooking anything.
//
// Unlike DS3/Sekiro/ER/DSR, this has no per-boss entity ID to report. The one field
// that looked like a per-character identifier (+0xce, "target character tag" per prior
// RE) stayed 0 through two live boss fights, so it isn't populated the way expected --
// the write path to it was never found (see project notes on the ESD command registry
// dead end). What IS confirmed live and reliable is +0xc4 -- a float that's 0 while the
// slot is inactive and a real ratio (current/max HP) once a boss is showing, going
// hand-in-hand with +0xd8/+0xdc (current/max HP as plain ints) jumping from 0 to real
// values at the same moment. That 0 -> nonzero transition on +0xc4 is what this reports
// as "a boss gauge activated" -- with no entity ID, matching entities is not possible,
// so the caller (MainViewModel) always attributes this to whichever split is current,
// the same semantics the manual ToggleBossTimer hotkey already uses.
public class DS2BossGaugeService(IMemoryService memoryService)
{
    private const int SlotCount = 3;

    // Defaults to 0, matching DSR's boss-timer service: the very first tick after
    // attach never reports an activation even if a gauge is already showing (tool
    // attached mid-fight) -- only a genuine 0 -> nonzero transition counts.
    private readonly float[] _prevRatio = new float[SlotCount];

    public bool TryGetActivation()
    {
        var gameMan = memoryService.Read<nint>(GameManagerImp.Base);
        if (gameMan == 0) return false;

        var subsystemGroup = memoryService.Read<nint>(gameMan + GameManagerImp.FeSubsystemGroup);
        if (subsystemGroup == 0) return false;

        var feOperatorFrontend = memoryService.Read<nint>(subsystemGroup + 0x10);
        if (feOperatorFrontend == 0) return false;

        int[] slotOffsets =
        [
            FeOperatorFrontend.BossGauge0,
            FeOperatorFrontend.BossGauge1,
            FeOperatorFrontend.BossGauge2
        ];

        var activated = false;
        for (var slot = 0; slot < SlotCount; slot++)
        {
            var gaugePtr = memoryService.Read<nint>(feOperatorFrontend + slotOffsets[slot]);
            var ratio = gaugePtr == 0 ? 0f : memoryService.Read<float>(gaugePtr + FeSceneBossHpGuage.Ratio);

            var prevRatio = _prevRatio[slot];
            _prevRatio[slot] = ratio;

            if (prevRatio == 0 && ratio != 0) activated = true;
        }

        return activated;
    }
}
