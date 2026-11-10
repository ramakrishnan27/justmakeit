namespace Platform.Content.Data;

/// <summary>Explicit column lists, so a SELECT never uses * and a new column is a deliberate change here.</summary>
public static class ContentColumns
{
    public const string Post =
        "id, agency_id, workspace_id, brand_id, campaign_id, created_by, updated_by, title, content_type, body, body_doc, excerpt, " +
        "status, visibility, language_code, timezone, scheduled_at, published_at, archived_at, seo_title, seo_description, seo_slug, " +
        "seo_keywords, settings, created_at, updated_at";

    public const string PostPlatform =
        "id, agency_id, post_id, social_account_id, platform, platform_caption, platform_title, platform_settings, publish_status, " +
        "scheduled_at, published_at, external_post_id, external_post_url, last_error, retry_count, created_at, updated_at";

    public const string Tag = "id, agency_id, workspace_id, name, color, created_at";

    public const string Template =
        "id, agency_id, workspace_id, name, description, content_type, title_template, body_template, template_data, is_active, created_by, created_at, updated_at";
}
