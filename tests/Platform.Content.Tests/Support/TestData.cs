using System.Text.Json;
using Platform.Content.DTOs;
using Platform.Content.Entities;
using Platform.Content.Features.Posts;
using Platform.Helpers.Constants;
using Platform.Helpers.Tenancy;
using Platform.Helpers.Validation;

namespace Platform.Content.Tests.Support;

/// <summary>Ready-made requests, documents and tenants, so a test says only what it is about.</summary>
public static class TestData
{
    public static readonly Guid AgencyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid WorkspaceId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly DateTimeOffset Now = new(2026, 11, 8, 10, 0, 0, TimeSpan.Zero);

    public static readonly string[] AllContentPermissions =
        [PermissionKeys.ContentStudioView, PermissionKeys.ContentStudioCreate, PermissionKeys.ContentStudioPublish];

    public static JsonElement Doc(string json) => JsonDocument.Parse(json).RootElement.Clone();

    public static JsonElement ParagraphDoc(string text)
    {
        var quoted = JsonSerializer.Serialize(text);
        return Doc($$"""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":{{quoted}} }]}]}""");
    }

    public static TenantContext Tenant(params string[] permissions)
    {
        var tenant = new TenantContext();
        tenant.Set(AgencyId, UserId, "marketingmanager", permissions);
        return tenant;
    }

    public static SavePostRequest NewPost(string text = "Hello world", string type = "social_post", string? title = null, params string[] platforms) =>
        new()
        {
            WorkspaceId = WorkspaceId,
            ContentType = type,
            Title = title,
            BodyDoc = ParagraphDoc(text),
            Platforms = [.. platforms.Select(p => new PlatformTargetInput { Platform = p })]
        };

    /// <summary>The post that <see cref="PostBuilder"/> makes of a request, as if it had been stored. Throws if the request is invalid.</summary>
    public static ContentPostAggregate StoredPost(SavePostRequest? request = null, string status = "draft", DateTimeOffset? scheduledAt = null)
    {
        var errors = new ValidationErrors();
        var aggregate = PostBuilder.Build(request ?? NewPost(), null, AgencyId, UserId, Now, errors);
        errors.ThrowIfAny();

        aggregate.Post.Status = status;
        aggregate.Post.ScheduledAt = scheduledAt;
        aggregate.Post.UpdatedAt = Now;
        foreach (var target in aggregate.Platforms)
        {
            target.PublishStatus = status == "scheduled" ? "scheduled" : "pending";
            target.ScheduledAt = status == "scheduled" ? scheduledAt : null;
        }

        return aggregate;
    }
}
