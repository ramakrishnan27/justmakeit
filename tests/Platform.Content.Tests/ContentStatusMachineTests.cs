using Platform.Content.Enums;
using Platform.Content.Features.Scheduling;

namespace Platform.Content.Tests;

public sealed class ContentStatusMachineTests
{
    private static readonly ContentStatus[] All = Enum.GetValues<ContentStatus>();

    [Theory]
    [InlineData(ContentStatus.Draft, ContentStatus.Scheduled)]
    [InlineData(ContentStatus.Draft, ContentStatus.Publishing)]
    [InlineData(ContentStatus.Draft, ContentStatus.Archived)]
    [InlineData(ContentStatus.Scheduled, ContentStatus.Draft)]
    [InlineData(ContentStatus.Scheduled, ContentStatus.Publishing)]
    [InlineData(ContentStatus.Publishing, ContentStatus.Published)]
    [InlineData(ContentStatus.Publishing, ContentStatus.Failed)]
    [InlineData(ContentStatus.Failed, ContentStatus.Scheduled)]
    [InlineData(ContentStatus.Failed, ContentStatus.Draft)]
    [InlineData(ContentStatus.Failed, ContentStatus.Publishing)]
    [InlineData(ContentStatus.Failed, ContentStatus.Archived)]
    [InlineData(ContentStatus.Published, ContentStatus.Archived)]
    [InlineData(ContentStatus.Archived, ContentStatus.Draft)]
    [InlineData(ContentStatus.Archived, ContentStatus.Published)]
    public void CanMove_AllowsTheMovesOfThePlan(ContentStatus from, ContentStatus to) =>
        Assert.True(ContentStatusMachine.CanMove(from, to));

    [Fact]
    public void CanMove_RefusesEverythingElse()
    {
        var allowed = new HashSet<(ContentStatus, ContentStatus)>
        {
            (ContentStatus.Draft, ContentStatus.Scheduled), (ContentStatus.Draft, ContentStatus.Publishing), (ContentStatus.Draft, ContentStatus.Archived),
            (ContentStatus.Scheduled, ContentStatus.Draft), (ContentStatus.Scheduled, ContentStatus.Publishing),
            (ContentStatus.Publishing, ContentStatus.Published), (ContentStatus.Publishing, ContentStatus.Failed),
            (ContentStatus.Failed, ContentStatus.Scheduled), (ContentStatus.Failed, ContentStatus.Draft),
            (ContentStatus.Failed, ContentStatus.Publishing), (ContentStatus.Failed, ContentStatus.Archived),
            (ContentStatus.Published, ContentStatus.Archived),
            (ContentStatus.Archived, ContentStatus.Draft), (ContentStatus.Archived, ContentStatus.Published)
        };

        foreach (var from in All)
        {
            foreach (var to in All)
            {
                Assert.Equal(allowed.Contains((from, to)), ContentStatusMachine.CanMove(from, to));
            }
        }
    }

    [Fact]
    public void ASchedulePostCannotBeArchived_ItMustBeTakenBackFirst() =>
        Assert.False(ContentStatusMachine.CanMove(ContentStatus.Scheduled, ContentStatus.Archived));

    [Fact]
    public void APostThatIsGoingOutOrHasGone_IsNeverMovedBackToDraftOrScheduled()
    {
        Assert.False(ContentStatusMachine.CanMove(ContentStatus.Publishing, ContentStatus.Draft));
        Assert.False(ContentStatusMachine.CanMove(ContentStatus.Published, ContentStatus.Draft));
        Assert.False(ContentStatusMachine.CanMove(ContentStatus.Published, ContentStatus.Scheduled));
    }

    [Fact]
    public void Sources_AreTheStatusesFromWhichATargetCanBeReached()
    {
        Assert.Equal(
            [ContentStatus.Draft, ContentStatus.Published, ContentStatus.Failed],
            ContentStatusMachine.Sources(ContentStatus.Archived).Order());
        Assert.Equal(
            [ContentStatus.Publishing, ContentStatus.Archived],
            ContentStatusMachine.Sources(ContentStatus.Published).Order());
        Assert.Equal([ContentStatus.Draft, ContentStatus.Scheduled, ContentStatus.Failed], ContentStatusMachine.Sources(ContentStatus.Publishing));
        Assert.Empty(ContentStatusMachine.Sources(ContentStatus.Draft).Except([ContentStatus.Scheduled, ContentStatus.Failed, ContentStatus.Archived]));
    }

    [Theory]
    [InlineData(ContentStatus.Draft, true)]
    [InlineData(ContentStatus.Scheduled, true)]
    [InlineData(ContentStatus.Failed, true)]
    [InlineData(ContentStatus.Publishing, false)]
    [InlineData(ContentStatus.Published, false)]
    [InlineData(ContentStatus.Archived, false)]
    public void IsEditable_OnlyBeforeItGoesOut(ContentStatus status, bool expected) =>
        Assert.Equal(expected, ContentStatusMachine.IsEditable(status));

    [Theory]
    [InlineData(ContentStatus.Draft, true)]
    [InlineData(ContentStatus.Failed, true)]
    [InlineData(ContentStatus.Archived, true)]
    [InlineData(ContentStatus.Scheduled, false)]
    [InlineData(ContentStatus.Publishing, false)]
    [InlineData(ContentStatus.Published, false)]
    public void IsDeletable_NeverAPublishedOrScheduledPost(ContentStatus status, bool expected) =>
        Assert.Equal(expected, ContentStatusMachine.IsDeletable(status));

    [Theory]
    [InlineData(ContentStatus.Draft, true)]
    [InlineData(ContentStatus.Failed, true)]
    [InlineData(ContentStatus.Scheduled, true)]
    [InlineData(ContentStatus.Publishing, false)]
    [InlineData(ContentStatus.Published, false)]
    [InlineData(ContentStatus.Archived, false)]
    public void CanSchedule_IncludesRescheduling(ContentStatus status, bool expected) =>
        Assert.Equal(expected, ContentStatusMachine.CanSchedule(status));

    [Fact]
    public void RestoreTarget_IsPublishedForAPostThatWasPublished_OtherwiseDraft()
    {
        Assert.Equal(ContentStatus.Published, ContentStatusMachine.RestoreTarget(DateTimeOffset.UtcNow));
        Assert.Equal(ContentStatus.Draft, ContentStatusMachine.RestoreTarget(null));
    }
}
