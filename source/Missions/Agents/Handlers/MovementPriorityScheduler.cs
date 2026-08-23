using System;

namespace Missions.Agents.Handlers;

public interface IMovementPriorityScheduler
{
    MovementPriorityKey CreateKey(
        bool isLocalMainAgent,
        float? distanceToRecipientFocus,
        float currentTime,
        float? lastSuccessfulSendTime,
        float pendingSince,
        Guid agentId);

    int Compare(MovementPriorityKey left, MovementPriorityKey right);

    float GetUpdateIntervalSeconds(
        bool isLocalMainAgent,
        float? distanceToRecipientFocus,
        bool isMount);
}

/// <summary>Stable per-recipient ordering key for one current movement snapshot.</summary>
public readonly struct MovementPriorityKey
{
    public int Tier { get; }
    public double Score { get; }
    public float LastSuccessfulSendTime { get; }
    public float PendingSince { get; }
    public Guid AgentId { get; }

    public MovementPriorityKey(
        int tier,
        double score,
        float lastSuccessfulSendTime,
        float pendingSince,
        Guid agentId)
    {
        Tier = tier;
        Score = score;
        LastSuccessfulSendTime = lastSuccessfulSendTime;
        PendingSince = pendingSince;
        AgentId = agentId;
    }
}

/// <summary>
/// Ranks movement snapshots by local-player priority, distance bands and staleness.
/// Also defines the per-recipient interest cadence used to avoid producing needless remote movement traffic.
/// Large battle tuning intentionally makes distant agents age more slowly so they do not repeatedly reclaim
/// bandwidth from nearby combatants after only a few missed snapshots.
/// </summary>
public sealed class MovementPriorityScheduler : IMovementPriorityScheduler
{
    public const float NearRadius = 30f;
    public const float InterestRadius = 75f;
    public const float MediumRadius = 150f;
    public const float FarRadius = 300f;
    public const double DistanceWeight = 7d;
    public const double NearbyStalenessHalfLifeSeconds = 0.15d;
    public const double MediumStalenessHalfLifeSeconds = 0.35d;
    public const double FarStalenessHalfLifeSeconds = 0.9d;
    public const double DistantStalenessHalfLifeSeconds = 2.0d;
    public const float MaximumPriorityAgingSeconds = 0.5f;

    public MovementPriorityKey CreateKey(
        bool isLocalMainAgent,
        float? distanceToRecipientFocus,
        float currentTime,
        float? lastSuccessfulSendTime,
        float pendingSince,
        Guid agentId)
    {
        float distance = distanceToRecipientFocus.HasValue
            ? Math.Max(0f, distanceToRecipientFocus.Value)
            : float.PositiveInfinity;

        // Keep the local player absolute-highest priority. For other agents, use explicit
        // interest bands so a far-away agent cannot routinely displace a nearby combatant just
        // because its snapshot happened to age by a few frames.
        int tier = isLocalMainAgent
            ? 0
            : distance <= NearRadius
                ? 1
                : distance <= InterestRadius
                    ? 2
                    : distance <= MediumRadius
                        ? 3
                        : 4;

        double normalizedDistance = float.IsPositiveInfinity(distance)
            ? 1d
            : Math.Max(0d, Math.Min(1d, distance / InterestRadius));
        double tierDistanceBias = tier == 1
            ? 0.5d
            : tier == 2
                ? 1.0d
                : tier == 3
                    ? 8.0d
                    : 32.0d;
        double distanceComponent = tierDistanceBias +
            (DistanceWeight * normalizedDistance);

        float effectiveLastSent = lastSuccessfulSendTime ??
            (pendingSince - MaximumPriorityAgingSeconds);
        double age = Math.Max(0d, currentTime - effectiveLastSent);
        double halfLife = tier <= 2
            ? NearbyStalenessHalfLifeSeconds
            : tier == 3
                ? MediumStalenessHalfLifeSeconds
                : tier == 4
                    ? (float.IsPositiveInfinity(distance)
                        ? DistantStalenessHalfLifeSeconds
                        : FarStalenessHalfLifeSeconds)
                    : NearbyStalenessHalfLifeSeconds;
        double lastUpdatedComponent = Math.Pow(0.5d, age / halfLife);

        return new MovementPriorityKey(
            tier,
            distanceComponent * lastUpdatedComponent,
            lastSuccessfulSendTime ?? float.MinValue,
            pendingSince,
            agentId);
    }

    public float GetUpdateIntervalSeconds(
        bool isLocalMainAgent,
        float? distanceToRecipientFocus,
        bool isMount)
    {
        if (isLocalMainAgent)
            return isMount ? 1f / 30f : 1f / 40f;

        if (!distanceToRecipientFocus.HasValue)
            return isMount ? 1f : 0.75f;

        float distance = Math.Max(0f, distanceToRecipientFocus.Value);
        if (distance <= NearRadius)
            return isMount ? 1f / 30f : 1f / 30f;
        if (distance <= InterestRadius)
            return isMount ? 1f / 12f : 1f / 15f;
        if (distance <= MediumRadius)
            return isMount ? 0.4f : 0.2f;
        if (distance <= FarRadius)
            return isMount ? 0.75f : 0.4f;

        return isMount ? 1.25f : 0.75f;
    }

    public int Compare(MovementPriorityKey left, MovementPriorityKey right)
    {
        int result = left.Tier.CompareTo(right.Tier);
        if (result != 0) return result;

        result = left.Score.CompareTo(right.Score);
        if (result != 0) return result;

        result = left.LastSuccessfulSendTime.CompareTo(right.LastSuccessfulSendTime);
        if (result != 0) return result;

        result = left.PendingSince.CompareTo(right.PendingSince);
        if (result != 0) return result;

        return left.AgentId.CompareTo(right.AgentId);
    }
}
