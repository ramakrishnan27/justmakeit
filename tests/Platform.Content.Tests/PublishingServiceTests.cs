using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Platform.Content.Entities;
using Platform.Content.Features.Publishing;
using Platform.Content.Interfaces;
using Platform.Content.Settings;
using Platform.Content.Tests.Support;
using Platform.Helpers.Publishing;

namespace Platform.Content.Tests;

public sealed class PublishingServiceTests
{
    private readonly IPublicationRepository _publications = Substitute.For<IPublicationRepository>();
    private readonly IPublisherCatalog _catalog = Substitute.For<IPublisherCatalog>();
    private readonly ISocialPublisher _publisher = Substitute.For<ISocialPublisher>();
    private readonly FixedTimeProvider _time = new(TestData.Now);
    private readonly ContentOptions _options = new();
    private readonly PublishingService _service;
    private static readonly CancellationToken Ct = CancellationToken.None;

    public PublishingServiceTests()
    {
        _service = new PublishingService(_publications, _catalog, Options.Create(_options), _time, NullLogger<PublishingService>.Instance);
        _catalog.Find("instagram").Returns(_publisher);
        _publisher.Platform.Returns("instagram");
    }

    private static ClaimedPublication Claim(Guid? id = null) =>
        new() { AgencyId = TestData.AgencyId, PostId = Guid.NewGuid(), PublicationId = id ?? Guid.NewGuid() };

    private static PublicationWork Work(ClaimedPublication claim, string platform = "instagram", int retries = 0, string? caption = null, string? title = null) =>
        new()
        {
            AgencyId = claim.AgencyId,
            PostId = claim.PostId,
            Title = "Dream Bigger",
            Body = "Post text",
            Hashtags = ["a", "b"],
            Target = new ContentPostPlatform
            {
                Id = claim.PublicationId, AgencyId = claim.AgencyId, PostId = claim.PostId, Platform = platform, RetryCount = retries,
                PlatformCaption = caption, PlatformTitle = title, PlatformSettings = """{"x":1}""", PublishStatus = "publishing"
            }
        };

    private ClaimedPublication Arrange(PublicationWork? work = null, ClaimedPublication? claim = null)
    {
        claim ??= Claim();
        _publications.ClaimDueAsync(default, default, default).ReturnsForAnyArgs([claim]);
        _publications.LoadAsync(claim.AgencyId, claim.PostId, claim.PublicationId, Arg.Any<CancellationToken>()).Returns(work ?? Work(claim));
        return claim;
    }

    [Fact]
    public async Task WhenNothingIsDue_NothingHappens()
    {
        _publications.ClaimDueAsync(default, default, default).ReturnsForAnyArgs([]);

        Assert.Equal(0, await _service.ProcessDueAsync(Ct));
        await _publications.DidNotReceiveWithAnyArgs().LoadAsync(default, default, default, default);
    }

    [Fact]
    public async Task ItClaimsAsManyAsTheBatchSizeAndTakesBackWhatIsStale()
    {
        _options.Publishing.BatchSize = 7;
        _options.Publishing.StaleMinutes = 3;
        _publications.ClaimDueAsync(default, default, default).ReturnsForAnyArgs([]);

        await _service.ProcessDueAsync(Ct);

        await _publications.Received(1).ClaimDueAsync(7, 3, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASuccess_IsRecordedWithTheExternalIdAndAddress()
    {
        var claim = Arrange();
        _publisher.PublishAsync(default!, default).ReturnsForAnyArgs(SocialPublishResult.Success("ext-1", "https://example.com/p/1"));

        Assert.Equal(1, await _service.ProcessDueAsync(Ct));

        await _publications.Received(1).RecordSuccessAsync(TestData.AgencyId, claim.PublicationId, "ext-1", "https://example.com/p/1", TestData.Now, Arg.Any<CancellationToken>());
        await _publications.DidNotReceiveWithAnyArgs().RecordFailureAsync(default, default, default!, default, default, default);
    }

    [Fact]
    public async Task ThePublisherIsGivenWhatThePlatformShouldShow_ThePostsTextAndTitleUnlessThePlatformHasItsOwn()
    {
        Arrange();
        SocialPublishRequest? seen = null;
        _publisher.PublishAsync(Arg.Do<SocialPublishRequest>(r => seen = r), Arg.Any<CancellationToken>()).Returns(SocialPublishResult.Success("e"));

        await _service.ProcessDueAsync(Ct);

        Assert.Equal(("Post text", "Dream Bigger", """{"x":1}"""), (seen!.Text, seen.Title, seen.SettingsJson));
        Assert.Equal(["a", "b"], seen.Hashtags);
        Assert.Equal(TestData.AgencyId, seen.AgencyId);
    }

    [Fact]
    public async Task APlatformsOwnCaptionAndTitle_WinOverThePosts()
    {
        var claim = Claim();
        Arrange(Work(claim, caption: "Own caption", title: "Own title"), claim);
        SocialPublishRequest? seen = null;
        _publisher.PublishAsync(Arg.Do<SocialPublishRequest>(r => seen = r), Arg.Any<CancellationToken>()).Returns(SocialPublishResult.Success("e"));

        await _service.ProcessDueAsync(Ct);

        Assert.Equal(("Own caption", "Own title", claim.PublicationId), (seen!.Text, seen.Title, seen.PublicationId));
    }

    [Fact]
    public async Task AnUntitledPost_GivesThePublisherNoTitle()
    {
        var claim = Claim();
        var work = Work(claim);
        Arrange(new PublicationWork { AgencyId = work.AgencyId, PostId = work.PostId, Title = "", Body = work.Body, Hashtags = work.Hashtags, Target = work.Target }, claim);
        SocialPublishRequest? seen = null;
        _publisher.PublishAsync(Arg.Do<SocialPublishRequest>(r => seen = r), Arg.Any<CancellationToken>()).Returns(SocialPublishResult.Success("e"));

        await _service.ProcessDueAsync(Ct);

        Assert.Null(seen!.Title);
    }

    [Fact]
    public async Task ATargetThatIsGoneByTheTimeItIsLoaded_IsSkipped()
    {
        var claim = Claim();
        _publications.ClaimDueAsync(default, default, default).ReturnsForAnyArgs([claim]);
        _publications.LoadAsync(default, default, default, default).ReturnsForAnyArgs((PublicationWork?)null);

        await _service.ProcessDueAsync(Ct);

        await _publisher.DidNotReceiveWithAnyArgs().PublishAsync(default!, default);
        await _publications.DidNotReceiveWithAnyArgs().RecordSuccessAsync(default, default, default!, default, default, default);
        await _publications.DidNotReceiveWithAnyArgs().RecordFailureAsync(default, default, default!, default, default, default);
    }

    [Theory]
    [InlineData("instagram", "Instagram")]
    [InlineData("youtube", "YouTube")]
    public async Task APlatformWithNoPublisher_FailsForGood_WithAMessageThatNamesIt(string platform, string name)
    {
        var claim = Claim();
        Arrange(Work(claim, platform), claim);
        _catalog.Find(platform).Returns((ISocialPublisher?)null);

        await _service.ProcessDueAsync(Ct);

        await _publications.Received(1).RecordFailureAsync(
            TestData.AgencyId, claim.PublicationId, $"Publishing to {name} is not available yet.", null, TestData.Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFailureThatMayWorkLater_IsScheduledAgainAfterAPause_UntilTheAttemptsAreUsedUp()
    {
        var claim = Claim();
        _publisher.PublishAsync(default!, default).ReturnsForAnyArgs(SocialPublishResult.Failure("Rate limited.", retryable: true));

        foreach (var (retries, pause) in new[] { (0, 1), (1, 5) })
        {
            _publications.ClearReceivedCalls();
            Arrange(Work(claim, retries: retries), claim);

            await _service.ProcessDueAsync(Ct);

            await _publications.Received(1).RecordFailureAsync(
                TestData.AgencyId, claim.PublicationId, "Rate limited.", TestData.Now + TimeSpan.FromMinutes(pause), TestData.Now, Arg.Any<CancellationToken>());
        }

        // Third attempt (MaxAttempts is 3): no more.
        _publications.ClearReceivedCalls();
        Arrange(Work(claim, retries: 2), claim);
        await _service.ProcessDueAsync(Ct);
        await _publications.Received(1).RecordFailureAsync(TestData.AgencyId, claim.PublicationId, "Rate limited.", null, TestData.Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFailureThatWillNeverWork_IsNotRetried()
    {
        var claim = Arrange();
        _publisher.PublishAsync(default!, default).ReturnsForAnyArgs(SocialPublishResult.Failure("The account is not connected."));

        await _service.ProcessDueAsync(Ct);

        await _publications.Received(1).RecordFailureAsync(TestData.AgencyId, claim.PublicationId, "The account is not connected.", null, TestData.Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task APublisherThatThrows_IsAnOrdinaryRetryableFailure_AndItsMessageNeverReachesThePeopleOrTheLog()
    {
        var claim = Arrange();
        _publisher.PublishAsync(default!, default).ThrowsAsyncForAnyArgs(new HttpRequestException("secret token abc123 in url https://x?token=abc123"));

        await _service.ProcessDueAsync(Ct);

        await _publications.Received(1).RecordFailureAsync(
            TestData.AgencyId, claim.PublicationId,
            Arg.Is<string>(message => !message.Contains("abc123") && message.Contains("tried again")),
            TestData.Now + TimeSpan.FromMinutes(1), TestData.Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhatThePublisherReturns_IsMadeToFitTheColumnsBeforeItIsStored()
    {
        var claim = Arrange();
        var longId = new string('i', 400);
        _publisher.PublishAsync(default!, default).ReturnsForAnyArgs(SocialPublishResult.Success(longId, "javascript:alert(1)"));

        await _service.ProcessDueAsync(Ct);

        await _publications.Received(1).RecordSuccessAsync(TestData.AgencyId, claim.PublicationId, new string('i', 255), null, TestData.Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFailureMessage_IsOneLineOfAtMostTwoThousandCharacters()
    {
        var claim = Arrange();
        _publisher.PublishAsync(default!, default).ReturnsForAnyArgs(SocialPublishResult.Failure("first\nsecond\tthird " + new string('e', 3000)));

        await _service.ProcessDueAsync(Ct);

        await _publications.Received(1).RecordFailureAsync(
            TestData.AgencyId, claim.PublicationId,
            Arg.Is<string>(m => m.Length == 2000 && !m.Any(char.IsControl) && m.StartsWith("first second third")),
            null, TestData.Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OneTargetGoingWrong_DoesNotStopTheOthers()
    {
        var bad = Claim();
        var good = Claim();
        _publications.ClaimDueAsync(default, default, default).ReturnsForAnyArgs([bad, good]);
        _publications.LoadAsync(bad.AgencyId, bad.PostId, bad.PublicationId, Arg.Any<CancellationToken>()).Returns<PublicationWork?>(_ => throw new InvalidOperationException("database hiccup"));
        _publications.LoadAsync(good.AgencyId, good.PostId, good.PublicationId, Arg.Any<CancellationToken>()).Returns(Work(good));
        _publisher.PublishAsync(default!, default).ReturnsForAnyArgs(SocialPublishResult.Success("e"));

        var handled = await _service.ProcessDueAsync(Ct);

        Assert.Equal(1, handled);
        await _publications.Received(1).RecordSuccessAsync(TestData.AgencyId, good.PublicationId, "e", null, TestData.Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AShutdown_IsNotSwallowed()
    {
        Arrange();
        using var cancellation = new CancellationTokenSource();
        _publisher.PublishAsync(default!, default).ReturnsForAnyArgs(_ =>
        {
            cancellation.Cancel();
            return Task.FromException<SocialPublishResult>(new OperationCanceledException(cancellation.Token));
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.ProcessDueAsync(cancellation.Token));
        await _publications.DidNotReceiveWithAnyArgs().RecordFailureAsync(default, default, default!, default, default, default);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 5)]
    [InlineData(3, 15)]
    [InlineData(9, 15)]
    public void TheBackoff_GrowsFromOneToFifteenMinutes(int attempts, int minutes) =>
        Assert.Equal(TimeSpan.FromMinutes(minutes), PublishingService.Backoff(attempts));
}
