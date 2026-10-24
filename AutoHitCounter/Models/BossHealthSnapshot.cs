//

namespace AutoHitCounter.Models;

// One poll's view of whatever boss gauges are currently showing. Hp/MaxHp are
// summed across every active gauge, so a two-boss fight reads as one bar.
public readonly struct BossHealthSnapshot(bool isActive, uint entityId, int hp, int maxHp)
{
    public bool IsActive { get; } = isActive;

    // Entity ID of the first active gauge -- the same ID space as the boss entity ID tables.
    public uint EntityId { get; } = entityId;

    public int Hp { get; } = hp;
    public int MaxHp { get; } = maxHp;

    public static BossHealthSnapshot Inactive => new(false, 0, 0, 0);
}
