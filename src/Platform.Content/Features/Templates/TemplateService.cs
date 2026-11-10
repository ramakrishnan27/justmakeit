using System.Text.Json;
using Microsoft.Extensions.Logging;
using Platform.Content.Data;
using Platform.Content.DTOs;
using Platform.Content.Entities;
using Platform.Content.Enums;
using Platform.Content.Features.Hashtags;
using Platform.Content.Features.Posts;
using Platform.Content.Interfaces;
using Platform.Content.Services;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;
using Platform.Helpers.Tenancy;
using Platform.Helpers.Validation;

namespace Platform.Content.Features.Templates;

/// <summary>
/// Reusable starting points. A template keeps what the Create page needs to fill itself: a title, the editor's document and its
/// hashtags. The plain text is in <c>body_template</c>; the document and the hashtags are in <c>template_data</c>.
/// </summary>
public sealed class TemplateService(
    ITemplateRepository templates,
    IContentLookupRepository lookups,
    ITenantContext tenant,
    TimeProvider time,
    ILogger<TemplateService> logger) : ITemplateService
{
    public async Task<IReadOnlyList<TemplateResponse>> ListAsync(TemplateListQuery query, CancellationToken cancellationToken)
    {
        var (agencyId, userId) = ContentAccess.Require(tenant, PermissionKeys.ContentStudioView, "see content");
        if (query.Type is not null && !DbEnum<ContentType>.TryParse(query.Type, out _))
        {
            new ValidationErrors().Add("type", $"Type must be one of: {string.Join(", ", DbEnum<ContentType>.All)}.").ThrowIfAny();
        }

        var rows = await templates.ListAsync(agencyId, userId, query.WorkspaceId, query.Type, cancellationToken);
        return rows.Select(ToResponse).ToList();
    }

    public async Task<TemplateResponse> CreateAsync(SaveTemplateRequest request, CancellationToken cancellationToken)
    {
        var (agencyId, userId) = ContentAccess.Require(tenant, PermissionKeys.ContentStudioCreate, "save templates");

        var errors = new ValidationErrors();
        if (request.WorkspaceId is null || request.WorkspaceId == Guid.Empty)
        {
            errors.Add("workspaceId", "Choose a workspace.");
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 120 || name.Any(char.IsControl))
        {
            errors.Add("name", "A template name is 1 to 120 characters on one line.");
        }

        var description = request.Description?.Trim() ?? string.Empty;
        if (description.Length > 500)
        {
            errors.Add("description", "Description must be at most 500 characters.");
        }

        if (!DbEnum<ContentType>.TryParse(request.ContentType, out _))
        {
            errors.Add("contentType", $"Content type must be one of: {string.Join(", ", DbEnum<ContentType>.All)}.");
        }

        var title = request.Title?.Trim() ?? string.Empty;
        if (title.Length > PostBuilder.MaxTitleLength || title.Any(char.IsControl))
        {
            errors.Add("title", $"Title must be at most {PostBuilder.MaxTitleLength} characters on one line.");
        }

        var documentErrors = new ValidationErrors();
        RichTextDocument.Validate(request.BodyDoc, "bodyDoc", documentErrors);
        foreach (var (field, messages) in documentErrors.ToDictionary())
        {
            foreach (var message in messages)
            {
                errors.Add(field, message);
            }
        }

        string? bodyDoc = null;
        var body = string.Empty;
        if (!documentErrors.Any && request.BodyDoc is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } document)
        {
            bodyDoc = document.GetRawText();
            body = RichTextDocument.ToPlainText(document);
        }

        if (title.Length == 0 && body.Length == 0)
        {
            errors.Add("content", "A template needs a title or some text.");
        }

        var hashtags = HashtagNormalizer.NormalizeAll(request.Hashtags, errors, "hashtags");
        errors.ThrowIfAny();

        if (!await lookups.WorkspaceExistsAsync(agencyId, userId, request.WorkspaceId!.Value, cancellationToken))
        {
            throw new NotFoundException("That workspace was not found.", "workspace_not_found");
        }

        var template = new ContentTemplate
        {
            Id = Guid.NewGuid(),
            AgencyId = agencyId,
            WorkspaceId = request.WorkspaceId.Value,
            Name = name,
            Description = description,
            ContentType = request.ContentType!,
            TitleTemplate = title,
            BodyTemplate = body,
            TemplateData = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["bodyDoc"] = bodyDoc is null ? null : PostMapper.Json(bodyDoc),
                ["hashtags"] = hashtags
            }),
            IsActive = true,
            CreatedBy = userId,
            CreatedAt = time.GetUtcNow(),
            UpdatedAt = time.GetUtcNow()
        };
        if (!await templates.InsertAsync(template, cancellationToken))
        {
            throw new ConflictException("This workspace already has a template with that name.", "template_exists");
        }

        logger.LogInformation("User {UserId} saved template {TemplateId} in workspace {WorkspaceId}", userId, template.Id, template.WorkspaceId);
        return ToResponse(template);
    }

    public async Task DeleteAsync(Guid templateId, CancellationToken cancellationToken)
    {
        var (agencyId, userId) = ContentAccess.Require(tenant, PermissionKeys.ContentStudioCreate, "delete templates");
        if (!await templates.DeleteAsync(agencyId, userId, templateId, cancellationToken))
        {
            throw new NotFoundException("That template was not found.", "template_not_found");
        }

        logger.LogInformation("User {UserId} deleted template {TemplateId}", userId, templateId);
    }

    private static TemplateResponse ToResponse(ContentTemplate template)
    {
        // template_data is ours (written above), but it is read defensively: a template must never take the page down.
        JsonElement? bodyDoc = null;
        var hashtags = new List<string>();
        try
        {
            var data = PostMapper.Json(template.TemplateData);
            if (data.ValueKind == JsonValueKind.Object)
            {
                if (data.TryGetProperty("bodyDoc", out var doc) && doc.ValueKind == JsonValueKind.Object)
                {
                    bodyDoc = doc;
                }

                if (data.TryGetProperty("hashtags", out var tags) && tags.ValueKind == JsonValueKind.Array)
                {
                    hashtags.AddRange(tags.EnumerateArray().Where(t => t.ValueKind == JsonValueKind.String).Select(t => t.GetString()!));
                }
            }
        }
        catch (JsonException)
        {
            // Treated as a template with no document: the plain text is still there.
        }

        return new TemplateResponse(
            template.Id, template.WorkspaceId, template.Name, template.Description, template.ContentType, template.TitleTemplate,
            bodyDoc, template.BodyTemplate, hashtags, template.CreatedAt, template.UpdatedAt);
    }
}
