using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Platform.Content.Entities;
using Platform.Content.Interfaces;
using Platform.Helpers.Constants;
using Platform.Helpers.Media;
using Platform.Helpers.Tenancy;

namespace Platform.Content.Tests.Support;

/// <summary>A clock a test sets, so "a minute from now" and "a year from now" mean something.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>The substitutes every service test starts from: repositories that find nothing and say yes to everything else.</summary>
public abstract class ServiceTestBase
{
    protected readonly IContentPostRepository Posts = Substitute.For<IContentPostRepository>();
    protected readonly ISchedulingRepository Scheduling = Substitute.For<ISchedulingRepository>();
    protected readonly IContentLookupRepository Lookups = Substitute.For<IContentLookupRepository>();
    protected readonly ICampaignLookup Campaigns = Substitute.For<ICampaignLookup>();
    protected readonly IContentMediaLibrary Media = Substitute.For<IContentMediaLibrary>();
    protected readonly IContentMediaUsage Usage = Substitute.For<IContentMediaUsage>();
    protected readonly FixedTimeProvider Time = new(TestData.Now);
    protected readonly TenantContext Tenant = new();

    protected ServiceTestBase()
    {
        Lookups.WorkspaceExistsAsync(default, default, default, default).ReturnsForAnyArgs(true);
        Lookups.EnabledPlatformsAsync(default, default, default).ReturnsForAnyArgs(
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string> { "facebook", "instagram", "x", "youtube", "linkedin" }));
        Campaigns.ExistsAsync(default, default, default, default).ReturnsForAnyArgs(false);
        // A library that is there and holds nothing, until a test says which files it holds.
        Media.IsAvailable.Returns(true);
        Media.FindAsync(default, default, default!, default).ReturnsForAnyArgs(Task.FromResult<IReadOnlyList<MediaAssetSummary>>([]));
    }

    protected static readonly NullLoggerFactory Logs = new();

    protected void SignIn(params string[] permissions) => Tenant.Set(TestData.AgencyId, TestData.UserId, "marketingmanager", permissions);

    protected void SignInWithEverything() => SignIn(TestData.AllContentPermissions);

    /// <summary>Makes the repository answer a read of the post with the given one (by id), and nothing for any other.</summary>
    protected void Stored(ContentPostAggregate post) =>
        Posts.GetAsync(TestData.AgencyId, TestData.UserId, post.Post.Id, Arg.Any<CancellationToken>()).Returns(post);
}
