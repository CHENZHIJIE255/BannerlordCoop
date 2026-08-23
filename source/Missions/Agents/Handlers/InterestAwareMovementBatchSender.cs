using Common.PacketHandlers;
using System;
using System.Collections.Generic;

namespace Missions.Agents.Handlers;

/// <summary>
/// Gates movement snapshots before they reach the serializer/bandwidth scheduler.
/// This is the large-battle interest-management layer: nearby agents retain battle-rate updates,
/// while remote agents are deliberately sampled less often. The existing MovementBatchSender remains
/// responsible for compression, packet sizing, fairness and traffic budgets.
/// </summary>
public sealed class InterestAwareMovementBatchSender : IMovementBatchSender
{
    private readonly IMovementBatchSender inner;
    private readonly Dictionary<string, Dictionary<Guid, float>> lastSent =
        new Dictionary<string, Dictionary<Guid, float>>(StringComparer.Ordinal);
    private float simulationTime;

    public InterestAwareMovementBatchSender(IMovementBatchSender inner)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public int AvailableOutgoingBytes => inner.AvailableOutgoingBytes;

    public void BeginFrame(float elapsedSeconds)
    {
        simulationTime += Math.Max(0f, elapsedSeconds);
        inner.BeginFrame(elapsedSeconds);
    }

    public void ConfigureRecipient(string controllerId, double bytesPerSecond, int maxPayloadBytes) =>
        inner.ConfigureRecipient(controllerId, bytesPerSecond, maxPayloadBytes);

    public MovementSendResult Send<T>(
        string controllerId,
        IEnumerable<MovementBatch<T>> scopedBatches,
        MovementBatch<T> legacyBatch,
        int maxPayloadBytes,
        Func<string, ushort[], Guid[], T[], IPacket> createPacket,
        Action<Guid, T> onSent)
    {
        MovementBatch<T>[] filtered = FilterBatches(
            controllerId,
            scopedBatches,
            legacyBatch);

        return inner.Send(
            controllerId,
            filtered,
            null,
            maxPayloadBytes,
            createPacket,
            (agentId, data) =>
            {
                RecordSent(controllerId, agentId);
                onSent?.Invoke(agentId, data);
            });
    }

    public MovementSendPairResult SendInterleaved<TFirst, TSecond>(
        string controllerId,
        IEnumerable<MovementBatch<TFirst>> firstScopedBatches,
        MovementBatch<TFirst> firstLegacyBatch,
        Func<string, ushort[], Guid[], TFirst[], IPacket> createFirstPacket,
        Action<Guid, TFirst> onFirstSent,
        IEnumerable<MovementBatch<TSecond>> secondScopedBatches,
        MovementBatch<TSecond> secondLegacyBatch,
        Func<string, ushort[], Guid[], TSecond[], IPacket> createSecondPacket,
        Action<Guid, TSecond> onSecondSent,
        int maxPayloadBytes)
    {
        MovementBatch<TFirst>[] first = FilterBatches(
            controllerId,
            firstScopedBatches,
            firstLegacyBatch);
        MovementBatch<TSecond>[] second = FilterBatches(
            controllerId,
            secondScopedBatches,
            secondLegacyBatch);

        return inner.SendInterleaved(
            controllerId,
            first,
            null,
            createFirstPacket,
            (agentId, data) =>
            {
                RecordSent(controllerId, agentId);
                onFirstSent?.Invoke(agentId, data);
            },
            second,
            null,
            createSecondPacket,
            (agentId, data) =>
            {
                RecordSent(controllerId, agentId);
                onSecondSent?.Invoke(agentId, data);
            },
            maxPayloadBytes);
    }

    public MovementTrafficFrame EndFrame(
        string controllerId,
        int deferredSnapshots,
        float maximumDeferredAgeSeconds) =>
        // Interest gating is intentional scheduling, not network backlog. Do not feed its cadence age
        // into the adaptive rate controller as if it were congestion.
        inner.EndFrame(controllerId, deferredSnapshots, 0f);

    public void RemoveRecipient(string controllerId)
    {
        inner.RemoveRecipient(controllerId);
        lastSent.Remove(controllerId);
    }

    public void Clear()
    {
        inner.Clear();
        lastSent.Clear();
        simulationTime = 0f;
    }

    private MovementBatch<T>[] FilterBatches<T>(
        string controllerId,
        IEnumerable<MovementBatch<T>> scopedBatches,
        MovementBatch<T> legacyBatch)
    {
        var result = new List<MovementBatch<T>>();
        if (scopedBatches != null)
        {
            foreach (MovementBatch<T> source in scopedBatches)
            {
                MovementBatch<T> filtered = FilterBatch(controllerId, source);
                if (filtered != null && filtered.Data.Count > 0)
                    result.Add(filtered);
            }
        }

        // Legacy batches do not carry MovementPriorityKey values, so retain their existing semantics.
        if (legacyBatch != null && legacyBatch.Data.Count > 0)
            result.Add(legacyBatch);

        return result.ToArray();
    }

    private MovementBatch<T> FilterBatch<T>(
        string controllerId,
        MovementBatch<T> source)
    {
        if (source == null || source.Data.Count == 0 || !source.HasPriorities)
            return source;

        var filtered = new MovementBatch<T>(source.IdentityScopeId, source.IsPriority);
        for (int i = 0; i < source.Data.Count; i++)
        {
            MovementPriorityKey priority = source.Priorities[i];
            if (!IsDue(controllerId, priority))
                continue;

            if (source.IdentityScopeId != null)
                filtered.CompactIds.Add(source.CompactIds[i]);
            filtered.CanonicalIds.Add(source.CanonicalIds[i]);
            filtered.Data.Add(source.Data[i]);
            filtered.Priorities.Add(priority);
        }

        return filtered;
    }

    private bool IsDue(string controllerId, MovementPriorityKey priority)
    {
        float interval = Math.Max(0.01f, priority.UpdateIntervalSeconds);
        if (!lastSent.TryGetValue(controllerId, out Dictionary<Guid, float> recipient))
            return true;

        return !recipient.TryGetValue(priority.AgentId, out float sentAt) ||
               simulationTime - sentAt >= interval;
    }

    private void RecordSent(string controllerId, Guid agentId)
    {
        if (!lastSent.TryGetValue(
                controllerId,
                out Dictionary<Guid, float> recipient))
        {
            recipient = new Dictionary<Guid, float>();
            lastSent.Add(controllerId, recipient);
        }

        recipient[agentId] = simulationTime;
    }
}
