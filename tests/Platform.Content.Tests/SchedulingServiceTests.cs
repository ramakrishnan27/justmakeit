using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Platform.Content.DTOs;
using Platform.Content.Entities;
using Platform.Content.Features.Scheduling;
using Platform.Content.Interfaces;
using Platform.Content.Tests.Support;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;

namespace Platform.Content.Tests;

public sealed class SchedulingServiceTests : ServiceTestBase
{
    private readonly SchedulingService _service;
    private static readonly CancellationToken Ct = CancellationToken.None;

    public SchedulingServiceTests()
    {
        _service = new SchedulingService(Posts, Scheduling, Lookups, Media, Tenant, Time, NullLogger<SchedulingService>.Instance);
        Scheduling.ScheduleAsync(default, default, default, default, default, default!, default).ReturnsForAnyArgs(ScheduleOutcome.Done);
        Scheduling.UnscheduleAsync(default, default, default, default).ReturnsForAnyArgs(ScheduleOutcome.Done);
    }

    private ContentPostAggregate Ready(string status = "draft") =>
        TestData.StoredPost(TestData.NewPost("Hello world", platforms: ["instagram", "x"]), status, status == "scheduled" ? TestData.Now.AddDays(1) : null);

    private ContentPostAggregate Have(ContentPostAggregate post)
    {
        Stored(post);
        return post;
    }

    private static ScheduleRequest In(TimeSpan lead) => new(TestData.Now + lead);

    // --- Permission -------------------------------------------------------------------------------

    [Fact]
    public async Task EveryMethod_NeedsThePublishPermission_AWriterWhoMayOnlyCreateIsRefused()
    {
        SignIn(PermissionKeys.ContentStudioView, PermissionKeys.ContentStudioCreate);
        var id = Guid.NewGuid();

        foreach (var call in new Func<Task>[]
                 {
                     () => _service.ScheduleAsync(id, In(TimeSpan.FromDays(1)), Ct),
                     () => _service.UnscheduleAsync(id, Ct),
                     () => _service.PublishNowAsync(id, Ct)
                 })
        {
            var failure = await Assert.ThrowsAsync<ForbiddenException>(call);
            Assert.Equal("insufficient_permission", failure.Code);
        }

        Assert.Empty(Posts.ReceivedCalls());
        Assert.Empty(Scheduling.ReceivedCalls());
    }

    // --- Scheduling -------------------------------------------------------------------------------

    [Fact]
    public async Task Schedule_SetsTheTimeForEveryPlatform_AndReturnsThePost()
    {
        SignInWithEverything();
        var post = Have(Ready());

        var response = await _service.ScheduleAsync(post.Post.Id, In(TimeSpan.FromDays(2)), Ct);

        Assert.Equal(post.Post.Id, response.Id);
        await Scheduling.Received(1).ScheduleAsync(
            TestData.AgencyId, TestData.UserId, post.Post.Id, TestData.Now.AddDays(2), false,
            Arg.Is<IReadOnlyCollection<string>>(s => s.Order().SequenceEqual(new[] { "draft", "failed", "scheduled" })),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(-60)]
    [InlineData(0)]
    [InlineData(30)]
    public async Task Schedule_ForATimeLessThanAMinuteAhead_IsRefused(int seconds)
    {
        SignInWithEverything();
        var post = Have(Ready());

        var failure = await Assert.ThrowsAsync<BadRequestException>(() => _service.ScheduleAsync(post.Post.Id, In(TimeSpan.FromSeconds(seconds)), Ct));

        Assert.Contains("scheduledAt", failure.Errors!.Keys);
        await Scheduling.DidNotReceiveWithAnyArgs().ScheduleAsync(default, default, default, default, default, default!, default);
    }

    [Fact]
    public async Task Schedule_AMinuteAheadIsFine_AYearAndADayIsNot()
    {
        SignInWithEverything();
        var post = Have(Ready());

        await _service.ScheduleAsync(post.Post.Id, In(SchedulingService.MinimumLead), Ct);
        await _service.ScheduleAsync(post.Post.Id, In(SchedulingService.MaximumLead), Ct);
        var failure = await Assert.ThrowsAsync<BadRequestException>(
            () => _service.ScheduleAsync(post.Post.Id, In(SchedulingService.MaximumLead + TimeSpan.FromSeconds(1)), Ct));

        Assert.Contains("scheduledAt", failure.Errors!.Keys);
    }

    [Fact]
    public async Task Schedule_WithNoTime_IsRefused()
    {
        SignInWithEverything();

        var failure = await Assert.ThrowsAsync<BadRequestException>(() => _service.ScheduleAsync(Guid.NewGuid(), new ScheduleRequest(null), Ct));

        Assert.Contains("scheduledAt", failure.Errors!.Keys);
    }

    [Fact]
    public async Task Schedule_OfAPostThatIsNotThere_IsNotFound()
    {
        SignInWithEverything();

        await Assert.ThrowsAsync<NotFoundException>(() => _service.ScheduleAsync(Guid.NewGuid(), In(TimeSpan.FromDays(1)), Ct));
    }

    [Theory]
    [InlineData("publishing")]
    [InlineData("published")]
    [InlineData("archived")]
    public async Task Schedule_OfAPostThatIsGoingOutOrHasGone_IsRefused(string status)
    {
        SignInWithEverything();
        var post = Have(Ready(status));

        var failure = await Assert.ThrowsAsync<ConflictException>(() => _service.ScheduleAsync(post.Post.Id, In(TimeSpan.FromDays(1)), Ct));

        Assert.Equal("invalid_transition", failure.Code);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("failed")]
    [InlineData("scheduled")]
    public async Task Schedule_IsAllowedFromDraftFailedAndScheduled_TheLastIsARescheduling(string status)
    {
        SignInWithEverything();
        var post = Have(Ready(status));

        await _service.ScheduleAsync(post.Post.Id, In(TimeSpan.FromDays(1)), Ct);

        await Scheduling.Received(1).ScheduleAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), false, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Schedule_OfAPostThatIsNotFitToGoOut_SaysWhatIsMissing()
    {
        SignInWithEverything();
        var post = Have(TestData.StoredPost(TestData.NewPost())); // text, but no platform

        var failure = await Assert.ThrowsAsync<BadRequestException>(() => _service.ScheduleAsync(post.Post.Id, In(TimeSpan.FromDays(1)), Ct));

        Assert.Contains("platforms", failure.Errors!.Keys);
        await Scheduling.DidNotReceiveWithAnyArgs().ScheduleAsync(default, default, default, default, default, default!, default);
    }

    [Fact]
    public async Task Schedule_ToAPlatformThatHasBeenSwitchedOff_IsRefused()
    {
        SignInWithEverything();
        Lookups.EnabledPlatformsAsync(default, default, default).ReturnsForAnyArgs(Task.FromResult<IReadOnlySet<string>>(new HashSet<string> { "x" }));
        var post = Have(Ready());

        var failure = await Assert.ThrowsAsync<BadRequestException>(() => _service.ScheduleAsync(post.Post.Id, In(TimeSpan.FromDays(1)), Ct));

        Assert.Equal("platform_not_enabled", failure.Code);
        Assert.Equal(["instagram is not enabled for your organization."], failure.Errors!["platforms"]);
    }

    [Theory]
    [InlineData(ScheduleOutcome.NotFound, typeof(NotFoundException), "post_not_found")]
    [InlineData(ScheduleOutcome.WrongStatus, typeof(ConflictException), "invalid_transition")]
    [InlineData(ScheduleOutcome.InProgress, typeof(ConflictException), "post_publishing")]
    [InlineData(ScheduleOutcome.NoTargets, typeof(BadRequestException), "validation_failed")]
    public async Task Schedule_TurnsWhatTheRepositoryFoundIntoTheRightError(ScheduleOutcome outcome, Type exception, string code)
    {
        SignInWithEverything();
        var post = Have(Ready());
        Scheduling.ScheduleAsync(default, default, default, default, default, default!, default).ReturnsForAnyArgs(outcome);

        var failure = (AppException)await Assert.ThrowsAsync(exception, () => _service.ScheduleAsync(post.Post.Id, In(TimeSpan.FromDays(1)), Ct));

        Assert.Equal(code, failure.Code);
    }

    // --- Taking it back ----------------------------------------------------------------------------

    [Fact]
    public async Task Unschedule_IsOnlyForAScheduledPost()
    {
        SignInWithEverything();
        var scheduled = Have(Ready("scheduled"));
        var draft = Have(Ready());

        await _service.UnscheduleAsync(scheduled.Post.Id, Ct);
        var failure = await Assert.ThrowsAsync<ConflictException>(() => _service.UnscheduleAsync(draft.Post.Id, Ct));

        Assert.Equal("invalid_transition", failure.Code);
        await Scheduling.Received(1).UnscheduleAsync(TestData.AgencyId, TestData.UserId, scheduled.Post.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unschedule_WhenAPlatformHasStartedPublishing_IsAConflict()
    {
        SignInWithEverything();
        var scheduled = Have(Ready("scheduled"));
        Scheduling.UnscheduleAsync(default, default, default, default).ReturnsForAnyArgs(ScheduleOutcome.InProgress);

        var failure = await Assert.ThrowsAsync<ConflictException>(() => _service.UnscheduleAsync(scheduled.Post.Id, Ct));

        Assert.Equal("post_publishing", failure.Code);
    }

    // --- Publishing now ------------------------------------------------------------------------------

    [Fact]
    public async Task PublishNow_SchedulesForThisMomentAsAPublishingPost()
    {
        SignInWithEverything();
        var post = Have(Ready());

        await _service.PublishNowAsync(post.Post.Id, Ct);

        await Scheduling.Received(1).ScheduleAsync(
            TestData.AgencyId, TestData.UserId, post.Post.Id, TestData.Now, true,
            Arg.Is<IReadOnlyCollection<string>>(s => s.Contains("draft") && s.Contains("failed") && s.Contains("scheduled") && !s.Contains("published")),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("publishing")]
    [InlineData("published")]
    [InlineData("archived")]
    public async Task PublishNow_OfAPostThatIsGoingOutOrHasGone_IsRefused(string status)
    {
        SignInWithEverything();
        var post = Have(Ready(status));

        var failure = await Assert.ThrowsAsync<ConflictException>(() => _service.PublishNowAsync(post.Post.Id, Ct));

        Assert.Equal("invalid_transition", failure.Code);
    }

    [Fact]
    public async Task PublishNow_OfAPostThatIsNotFitToGoOut_IsRefused()
    {
        SignInWithEverything();
        var post = Have(TestData.StoredPost(TestData.NewPost()));

        await Assert.ThrowsAsync<BadRequestException>(() => _service.PublishNowAsync(post.Post.Id, Ct));
        await Scheduling.DidNotReceiveWithAnyArgs().ScheduleAsync(default, default, default, default, default, default!, default);
    }
}
