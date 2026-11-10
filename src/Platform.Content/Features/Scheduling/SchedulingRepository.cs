using Dapper;
using Platform.Content.Interfaces;
using Platform.Infrastructure.Data;
using Platform.Content.Data;

namespace Platform.Content.Features.Scheduling;

/// <summary>
/// Setting and taking back a schedule. Each method locks the post row first and then its targets (<c>FOR UPDATE</c>), so it cannot
/// interleave with another change to the same post, and the publishing worker's claim (which skips locked rows) cannot take a
/// target while it is being changed.
/// </summary>
public sealed class SchedulingRepository(IDbConnectionFactory connections) : ISchedulingRepository
{
    public async Task<ScheduleOutcome> ScheduleAsync(
        Guid agencyId, Guid userId, Guid postId, DateTimeOffset when, bool publishNow, IReadOnlyCollection<string> allowedFrom, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var status = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT status FROM content_posts WHERE id = @postId AND agency_id = @agencyId FOR UPDATE",
            new { postId, agencyId },
            transaction,
            cancellationToken: cancellationToken));
        if (status is null)
        {
            return ScheduleOutcome.NotFound;
        }

        if (!allowedFrom.Contains(status))
        {
            return ScheduleOutcome.WrongStatus;
        }

        var targets = (await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT publish_status FROM content_post_platforms WHERE post_id = @postId AND agency_id = @agencyId FOR UPDATE",
            new { postId, agencyId },
            transaction,
            cancellationToken: cancellationToken))).AsList();
        if (targets.Count == 0)
        {
            return ScheduleOutcome.NoTargets;
        }

        if (targets.Any(target => target is "publishing" or "published"))
        {
            return ScheduleOutcome.InProgress;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE content_post_platforms
            SET publish_status = 'scheduled', scheduled_at = @when, retry_count = 0, last_error = NULL
            WHERE post_id = @postId AND agency_id = @agencyId
            """,
            new { postId, agencyId, when },
            transaction,
            cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE content_posts SET status = @status, scheduled_at = @when, updated_by = @userId
            WHERE id = @postId AND agency_id = @agencyId
            """,
            new { postId, agencyId, userId, when, status = publishNow ? "publishing" : "scheduled" },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return ScheduleOutcome.Done;
    }

    public async Task<ScheduleOutcome> UnscheduleAsync(Guid agencyId, Guid userId, Guid postId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var status = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT status FROM content_posts WHERE id = @postId AND agency_id = @agencyId FOR UPDATE",
            new { postId, agencyId },
            transaction,
            cancellationToken: cancellationToken));
        if (status is null)
        {
            return ScheduleOutcome.NotFound;
        }

        if (status != "scheduled")
        {
            return ScheduleOutcome.WrongStatus;
        }

        var targets = (await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT publish_status FROM content_post_platforms WHERE post_id = @postId AND agency_id = @agencyId FOR UPDATE",
            new { postId, agencyId },
            transaction,
            cancellationToken: cancellationToken))).AsList();
        if (targets.Any(target => target is "publishing" or "published"))
        {
            return ScheduleOutcome.InProgress;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE content_post_platforms
            SET publish_status = 'pending', scheduled_at = NULL, retry_count = 0, last_error = NULL
            WHERE post_id = @postId AND agency_id = @agencyId
            """,
            new { postId, agencyId },
            transaction,
            cancellationToken: cancellationToken));
        // The date stays on the draft, as the date it was meant for.
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE content_posts SET status = 'draft', updated_by = @userId WHERE id = @postId AND agency_id = @agencyId",
            new { postId, agencyId, userId },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return ScheduleOutcome.Done;
    }
}
