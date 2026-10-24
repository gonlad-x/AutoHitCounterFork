//

using System;
using AutoHitCounter.Models;

namespace AutoHitCounter.Interfaces;

// Optional capability, like IVersionedGameModule: only modules that can read a
// boss's live HP implement it (Elden Ring so far), so IGameModule stays untouched.
public interface IBossHealthSource
{
    // Raised every game tick while loaded; an inactive snapshot means no boss gauge is showing.
    event Action<BossHealthSnapshot> OnBossHealth;
}
