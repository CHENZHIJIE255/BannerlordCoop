using Missions.Agents.Handlers;
using System;
using Xunit;

namespace E2E.Tests.Services.Missions;

public sealed class MovementPrioritySchedulerTests
{
    private readonly MovementPriorityScheduler scheduler = new MovementPriorityScheduler();

    [Fact]
    public void EqualAge_CloserAgentHasPriority()
    {
        MovementPriorityKey close = Create(distance: 5f, currentTime: 10f, lastSentTime: 9.9f);
        MovementPriorityKey far = Create(distance: 75f, currentTime: 10f, lastSentTime: 9.9f);

        Assert.True(scheduler.Compare(close, far) < 0);
    }

    [Fact]
    public void NearTierWinsOverMediumTierEvenWhenMediumAgentIsOlder()
    {
        MovementPriorityKey near = Create(distance: 20f, currentTime: 10f, lastSentTime: 9f);
        MovementPriorityKey medium = Create(distance: 100f, currentTime: 10f, lastSentTime: 9.9f);

        Assert.True(scheduler.Compare(near, medium) < 0);
    }

    [Fact]
    public void MediumTierWinsOverFarTierWhenAgeIsEqual()
    {
        MovementPriorityKey medium = Create(distance: 100f, currentTime: 10f, lastSentTime: 9.9f);
        MovementPriorityKey far = Create(distance: 250f, currentTime: 10f, lastSentTime: 9.9f);

        Assert.True(scheduler.Compare(medium, far) < 0);
    }

    [Fact]
    public void EqualDistance_OlderAgentHasPriorityWithinSameTier()
    {
        MovementPriorityKey older = Create(distance: 25f, currentTime: 10f, lastSentTime: 9.7f);
        MovementPriorityKey newer = Create(distance: 25f, currentTime: 10f, lastSentTime: 9.95f);

        Assert.True(scheduler.Compare(older, newer) < 0);
    }

    [Fact]
    public void FarAgentDoesNotImmediatelyDisplaceFreshNearAgent()
    {
        MovementPriorityKey freshNear = Create(
            distance: 10f,
            currentTime: 10f,
            lastSentTime: 10f);
        MovementPriorityKey staleFar = Create(
            distance: 250f,
            currentTime: 10f,
            lastSentTime: 9.2f);

        Assert.True(scheduler.Compare(freshNear, staleFar) < 0);
    }

    [Fact]
    public void FarAgentStillEventuallyAgesWithinItsTier()
    {
        MovementPriorityKey olderFar = Create(
            distance: 250f,
            currentTime: 10f,
            lastSentTime: 7.5f);
        MovementPriorityKey newerFar = Create(
            distance: 250f,
            currentTime: 10f,
            lastSentTime: 9.9f);

        Assert.True(scheduler.Compare(olderFar, newerFar) < 0);
    }

    [Fact]
    public void MissionTimeOffsetDoesNotChangeOrdering()
    {
        MovementPriorityKey first = Create(distance: 20f, currentTime: 2f, lastSentTime: 1.9f);
        MovementPriorityKey second = Create(distance: 50f, currentTime: 2f, lastSentTime: 1.7f);
        MovementPriorityKey shiftedFirst = Create(distance: 20f, currentTime: 1002f, lastSentTime: 1001.9f);
        MovementPriorityKey shiftedSecond = Create(distance: 50f, currentTime: 1002f, lastSentTime: 1001.7f);

        Assert.Equal(
            Math.Sign(scheduler.Compare(first, second)),
            Math.Sign(scheduler.Compare(shiftedFirst, shiftedSecond)));
    }

    [Fact]
    public void MissingFocusFallsBackToTheFarthestTier()
    {
        MovementPriorityKey missingFocus = Create(distance: null, currentTime: 10f, lastSentTime: 9.9f);
        MovementPriorityKey knownFar = Create(distance: 250f, currentTime: 10f, lastSentTime: 9.9f);

        Assert.Equal(4, missingFocus.Tier);
        Assert.Equal(4, knownFar.Tier);
    }

    [Fact]
    public void LocalMainAgentWinsBeforeDistanceTier()
    {
        MovementPriorityKey main = Create(
            distance: 75f,
            currentTime: 10f,
            lastSentTime: 10f,
            isMain: true);
        MovementPriorityKey nearAgent = Create(
            distance: 0f,
            currentTime: 10f,
            lastSentTime: 9f);

        Assert.True(scheduler.Compare(main, nearAgent) < 0);
    }

    [Fact]
    public void TierBoundariesAreStable()
    {
        Assert.Equal(1, Create(30f, 10f, 9.9f).Tier);
        Assert.Equal(2, Create(30.01f, 10f, 9.9f).Tier);
        Assert.Equal(2, Create(75f, 10f, 9.9f).Tier);
        Assert.Equal(3, Create(75.01f, 10f, 9.9f).Tier);
        Assert.Equal(3, Create(150f, 10f, 9.9f).Tier);
        Assert.Equal(4, Create(150.01f, 10f, 9.9f).Tier);
        Assert.Equal(4, Create(300f, 10f, 9.9f).Tier);
    }

    [Fact]
    public void InterestCadenceSlowsWithDistance()
    {
        float near = scheduler.GetUpdateIntervalSeconds(false, 10f, false);
        float medium = scheduler.GetUpdateIntervalSeconds(false, 100f, false);
        float far = scheduler.GetUpdateIntervalSeconds(false, 250f, false);
        float distant = scheduler.GetUpdateIntervalSeconds(false, 500f, false);

        Assert.True(near < medium);
        Assert.True(medium < far);
        Assert.True(far < distant);
    }

    [Fact]
    public void MountInterestCadenceIsSlowerThanOnFootAtLongRange()
    {
        Assert.True(
            scheduler.GetUpdateIntervalSeconds(false, 250f, true) >
            scheduler.GetUpdateIntervalSeconds(false, 250f, false));
    }

    [Fact]
    public void MissingFocusUsesConservativeCadence()
    {
        Assert.Equal(
            0.75f,
            scheduler.GetUpdateIntervalSeconds(false, null, false));
        Assert.Equal(
            1f,
            scheduler.GetUpdateIntervalSeconds(false, null, true));
    }

    private MovementPriorityKey Create(
        float? distance,
        float currentTime,
        float? lastSentTime,
        bool isMain = false)
    {
        return scheduler.CreateKey(
            isMain,
            distance,
            currentTime,
            lastSentTime,
            pendingSince: lastSentTime ?? currentTime,
            Guid.NewGuid());
    }
}
