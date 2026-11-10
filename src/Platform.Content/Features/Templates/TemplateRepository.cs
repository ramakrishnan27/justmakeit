using Dapper;
using Platform.Content.Data;
using Platform.Content.Entities;
using Platform.Content.Interfaces;
using Platform.Infrastructure.Data;

namespace Platform.Content.Features.Templates;

public sealed class TemplateRepository(IDbConnectionFactory connections) : ITemplateRepository
{
    public async Task<IReadOnlyList<ContentTemplate>> ListAsync(
        Guid agencyId, Guid userId, Guid? workspaceId, string? contentType, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        return (await connection.QueryAsync<ContentTemplate>(new CommandDefinition(
            $"""
            SELECT {ContentColumns.Template} FROM content_templates
            WHERE agency_id = @agencyId AND is_active
              AND (@workspaceId::uuid IS NULL OR workspace_id = @workspaceId)
              AND (@contentType::text IS NULL OR content_type = @contentType)
            ORDER BY lower(name), id
            """,
            new { agencyId, workspaceId, contentType },
            cancellationToken: cancellationToken))).AsList();
    }

    public async Task<bool> InsertAsync(ContentTemplate template, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(template.AgencyId, template.CreatedBy, cancellationToken);
        var inserted = await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO content_templates
                (id, agency_id, workspace_id, name, description, content_type, title_template, body_template, template_data, is_active, created_by)
            VALUES
                (@Id, @AgencyId, @WorkspaceId, @Name, @Description, @ContentType, @TitleTemplate, @BodyTemplate, @TemplateData, true, @CreatedBy)
            ON CONFLICT (workspace_id, lower(name)) DO NOTHING
            """,
            new
            {
                template.Id, template.AgencyId, template.WorkspaceId, template.Name, template.Description, template.ContentType,
                template.TitleTemplate, template.BodyTemplate, TemplateData = new JsonbParameter(template.TemplateData), template.CreatedBy
            },
            cancellationToken: cancellationToken));
        return inserted == 1;
    }

    public async Task<bool> DeleteAsync(Guid agencyId, Guid userId, Guid templateId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        var deleted = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM content_templates WHERE id = @templateId AND agency_id = @agencyId",
            new { templateId, agencyId },
            cancellationToken: cancellationToken));
        return deleted == 1;
    }
}
