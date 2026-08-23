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
/// Nearby agents stay ahead of distant agents when the shared movement budget is constrained.
/// </summary>
public sealed class MovementPriorityScheduler : IMovementPriorityScheduler
{
    public const float NearRadius = 30f;
    public const float InterestRadius = 75f;
    public const float MediumRadius = 150f;
    public const double DistanceWeight = 7d;
    public const double StalenessHalfLifeSeconds = 0.15d;
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
        double distanceComponent = 1d + (DistanceWeight * normalizedDistance);

        float effectiveLastSent = lastSuccessfulSendTime ??
            (pendingSince - MaximumPriorityAgingSeconds);
        double age = Math.Max(0d, currentTime - effectiveLastSent);
        double lastUpdatedComponent = Math.Pow(
            0.5d,
            age / StalenessHalfLifeSeconds);

        return new MovementPriorityKey(
            tier,
            distanceComponent * lastUpdatedComponent,
            lastSuccessfulSendTime ?? float.MinValue,
            pendingSince,
            agentId);
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
