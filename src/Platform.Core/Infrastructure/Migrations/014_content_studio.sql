-- 014_content_studio.sql
-- Content Studio: posts, their media, tags and hashtags, per-platform publishing, the editor's extras (poll, call-to-action
-- buttons, mentions, location), templates, AI generations and revision history.
--
-- Idempotent like every script: DbMigrator runs all of them on every start. Once released, never edit this file; add a new
-- numbered script instead. (This script took 014; the email script is now 015_email.sql.)
--
-- Design:
--   * Every table is a tenant table: agency_id NOT NULL -> agencies ON DELETE CASCADE, row-level security enabled and forced,
--     policy tenant_isolation (USING and WITH CHECK on agency_id = app_agency_id()). The post tables of the brief say
--     workspace_id; that stays (a post belongs to one workspace), and agency_id is added beside it, as on every tenant table.
--   * A child row carries the agency_id of its parent and the foreign key is COMPOSITE (post_id, agency_id), so a row can never
--     point at another organization's post: foreign key checks skip row-level security, and without this a bug could attach a
--     row of organization A to a post of organization B. The parents therefore have UNIQUE (id, agency_id). workspaces has no
--     such key (an existing table, left alone), so workspace_id is a plain foreign key and the service must check the
--     workspace through its own row-level-security-filtered read.
--   * People columns (created_by, updated_by, changed_by, requested_by) are NULLABLE with ON DELETE SET NULL. Removing a team
--     member whose last membership ends deletes their users row (team_remove_member_v2), so RESTRICT would make it impossible to
--     remove anybody who ever wrote a post, and CASCADE would delete the organization's content. The content stays, the author
--     is forgotten.
--   * Columns that point at tables that do not exist yet are plain uuid columns WITHOUT a foreign key, because the script that
--     creates each of those tables is the one that can add it (this script runs on every start and must not fail for a table
--     that is not there). Each of those scripts must add, in the same transaction that creates the table:
--         content_posts.brand_id                   -> brands (id)           ON DELETE SET NULL
--         content_posts.campaign_id                -> campaigns (id)        ON DELETE SET NULL
--         content_post_media.media_asset_id        -> media_assets (id)     ON DELETE RESTRICT
--         content_templates.preview_asset_id       -> media_assets (id)     ON DELETE SET NULL
--         content_ai_generations.output_media_id   -> media_assets (id)     ON DELETE SET NULL
--         content_post_platforms.social_account_id -> social_accounts (id)  ON DELETE RESTRICT
--     and, so that the platform on a row cannot disagree with its account's platform, the composite
--     (social_account_id, platform) -> social_accounts (id, platform). Use composite (id, agency_id) keys for these as well
--     when those tables are tenant tables.
--   * Status, type, role and platform columns are text + CHECK. The values are the snake_case names of the brief; the web client
--     and the C# enums that come later must use exactly these (the repo's .ToDb() lower-cases, so social_post needs its own map).
--   * Time: scheduled_at, published_at and archived_at are timestamptz (an absolute moment). content_posts.timezone only says
--     which IANA zone the person scheduled it in, for showing it back; the database checks its shape, the service its name.
--   * The post's status and its timestamps must agree (scheduled needs scheduled_at, published needs published_at, archived
--     needs archived_at and nothing else has one), because the dispatcher (Phase 4) reads them.
--   * content_posts.search_vector is a generated column for the list's "Search content..." box. It uses the 'simple' text search
--     configuration on purpose: posts are in any language (language_code), and a generated column cannot pick the configuration
--     per row. body is limited to 200 000 characters, which keeps the vector far below PostgreSQL's 1 MB limit.
--   * Revisions: content_revisions gets a row from a trigger, so it cannot be forgotten by any code path. Every INSERT of a post
--     writes revision 1 and every UPDATE that changes the content (title, body, excerpt, type or SEO fields; not status, schedule
--     or visibility) appends the next one, holding the content AS SAVED, so the newest revision equals the current post and the
--     older ones are the previous versions. changed_by is the post's updated_by (created_by on insert): the service must set
--     updated_by on every edit. The application role can read and add revisions, never change or delete one (a post's deletion
--     removes them through the foreign key, which does not need the privilege).
--   * One poll and one location per post (UNIQUE post_id); any number of call-to-action buttons and mentions; at most one cover
--     and one thumbnail per post.

-- ---------------------------------------------------------------------------------------------
-- Trigger function: keep updated_at honest whatever the code path.
-- ---------------------------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION content_set_updated_at()
    RETURNS trigger
    LANGUAGE plpgsql
    SET search_path = public, pg_temp
AS $$
BEGIN
    NEW.updated_at := now();
    RETURN NEW;
END
$$;

-- ---------------------------------------------------------------------------------------------
-- content_posts
-- ---------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS content_posts (
    id               uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    agency_id        uuid        NOT NULL REFERENCES agencies (id) ON DELETE CASCADE,
    workspace_id     uuid        NOT NULL REFERENCES workspaces (id) ON DELETE CASCADE,
    brand_id         uuid        NULL,
    campaign_id      uuid        NULL,
    created_by       uuid        NULL REFERENCES users (id) ON DELETE SET NULL,
    updated_by       uuid        NULL REFERENCES users (id) ON DELETE SET NULL,
    title            text        NOT NULL DEFAULT '',
    content_type     text        NOT NULL,
    body             text        NOT NULL DEFAULT '',
    excerpt          text        NOT NULL DEFAULT '',
    status           text        NOT NULL DEFAULT 'draft',
    visibility       text        NOT NULL DEFAULT 'private',
    language_code    text        NOT NULL DEFAULT 'en',
    timezone         text        NOT NULL DEFAULT 'UTC',
    scheduled_at     timestamptz NULL,
    published_at     timestamptz NULL,
    archived_at      timestamptz NULL,
    seo_title        text        NULL,
    seo_description  text        NULL,
    seo_slug         text        NULL,
    seo_keywords     text[]      NOT NULL DEFAULT '{}',
    settings         jsonb       NOT NULL DEFAULT '{}',
    search_vector    tsvector    GENERATED ALWAYS AS (
                         setweight(to_tsvector('simple'::regconfig, title), 'A')
                      || setweight(to_tsvector('simple'::regconfig, excerpt), 'B')
                      || setweight(to_tsvector('simple'::regconfig, body), 'C')) STORED,
    created_at       timestamptz NOT NULL DEFAULT now(),
    updated_at       timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_content_posts_id_agency UNIQUE (id, agency_id),
    CONSTRAINT ck_content_posts_type CHECK (content_type IN ('social_post', 'blog_post', 'video_script', 'carousel', 'short_reel')),
    CONSTRAINT ck_content_posts_status CHECK (status IN ('draft', 'scheduled', 'publishing', 'published', 'failed', 'archived')),
    CONSTRAINT ck_content_posts_visibility CHECK (visibility IN ('public', 'private', 'unlisted')),
    CONSTRAINT ck_content_posts_title CHECK (length(title) <= 300 AND title !~ '[[:cntrl:]]'),
    CONSTRAINT ck_content_posts_body CHECK (char_length(body) <= 200000),
    CONSTRAINT ck_content_posts_excerpt CHECK (length(excerpt) <= 1000),
    CONSTRAINT ck_content_posts_language CHECK (language_code ~ '^[a-z]{2,3}(-[A-Za-z0-9]{2,8}){0,3}$'),
    CONSTRAINT ck_content_posts_timezone CHECK (timezone ~ '^[A-Za-z][A-Za-z0-9_+/-]{0,63}$'),
    CONSTRAINT ck_content_posts_seo_title CHECK (seo_title IS NULL OR (length(seo_title) <= 200 AND seo_title !~ '[[:cntrl:]]')),
    CONSTRAINT ck_content_posts_seo_description CHECK (seo_description IS NULL OR length(seo_description) <= 500),
    CONSTRAINT ck_content_posts_seo_slug CHECK (seo_slug IS NULL OR (length(seo_slug) <= 200 AND seo_slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$')),
    CONSTRAINT ck_content_posts_seo_keywords CHECK (cardinality(seo_keywords) <= 50),
    CONSTRAINT ck_content_posts_settings CHECK (jsonb_typeof(settings) = 'object'),
    CONSTRAINT ck_content_posts_scheduled CHECK (status <> 'scheduled' OR scheduled_at IS NOT NULL),
    CONSTRAINT ck_content_posts_published CHECK (status <> 'published' OR published_at IS NOT NULL),
    CONSTRAINT ck_content_posts_archived CHECK ((status = 'archived') = (archived_at IS NOT NULL))
);

CREATE INDEX IF NOT EXISTS ix_content_posts_agency ON content_posts (agency_id);
-- The list: newest first in a workspace, narrowed by status or by the tab (type).
CREATE INDEX IF NOT EXISTS ix_content_posts_workspace_created ON content_posts (workspace_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_content_posts_workspace_status ON content_posts (workspace_id, status, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_content_posts_workspace_type ON content_posts (workspace_id, content_type, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_content_posts_campaign ON content_posts (campaign_id) WHERE campaign_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_content_posts_brand ON content_posts (brand_id) WHERE brand_id IS NOT NULL;
-- The calendar (a workspace's posts in a date range) and the dispatcher (what is due, across organizations).
CREATE INDEX IF NOT EXISTS ix_content_posts_workspace_scheduled ON content_posts (workspace_id, scheduled_at) WHERE scheduled_at IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_content_posts_due ON content_posts (scheduled_at) WHERE status = 'scheduled';
CREATE INDEX IF NOT EXISTS ix_content_posts_created_by ON content_posts (created_by) WHERE created_by IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_content_posts_updated_by ON content_posts (updated_by) WHERE updated_by IS NOT NULL;
-- A slug identifies one post in a workspace.
CREATE UNIQUE INDEX IF NOT EXISTS ux_content_posts_slug ON content_posts (workspace_id, seo_slug) WHERE seo_slug IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_content_posts_search ON content_posts USING gin (search_vector);
CREATE INDEX IF NOT EXISTS ix_content_posts_settings ON content_posts USING gin (settings jsonb_path_ops);

-- ---------------------------------------------------------------------------------------------
-- content_post_media: the images and videos of a post (media_assets is the Media Library, created by a later script)
-- ---------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS content_post_media (
    id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    agency_id       uuid        NOT NULL REFERENCES agencies (id) ON DELETE CASCADE,
    post_id         uuid        NOT NULL,
    media_asset_id  uuid        NOT NULL,
    display_order   integer     NOT NULL DEFAULT 0,
    media_role      text        NOT NULL DEFAULT 'attachment',
    caption         text        NOT NULL DEFAULT '',
    created_at      timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT fk_content_post_media_post FOREIGN KEY (post_id, agency_id)
        REFERENCES content_posts (id, agency_id) ON DELETE CASCADE,
    CONSTRAINT ck_content_post_media_order CHECK (display_order >= 0),
    CONSTRAINT ck_content_post_media_role CHECK (media_role IN ('attachment', 'cover', 'thumbnail', 'slide', 'video')),
    CONSTRAINT ck_content_post_media_caption CHECK (length(caption) <= 2200),
    -- The same asset may appear in two roles (cover and attachment), never twice in one.
    CONSTRAINT uq_content_post_media UNIQUE (post_id, media_asset_id, media_role)
);

CREATE INDEX IF NOT EXISTS ix_content_post_media_agency ON content_post_media (agency_id);
CREATE INDEX IF NOT EXISTS ix_content_post_media_order ON content_post_media (post_id, display_order);
CREATE INDEX IF NOT EXISTS ix_content_post_media_asset ON content_post_media (media_asset_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_content_post_media_single_role ON content_post_media (post_id, media_role)
    WHERE media_role IN ('cover', 'thumbnail');

-- ---------------------------------------------------------------------------------------------
-- content_tags, content_post_tags: a workspace's tags, and which posts carry which
-- ---------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS content_tags (
    id            uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    agency_id     uuid        NOT NULL REFERENCES agencies (id) ON DELETE CASCADE,
    workspace_id  uuid        NOT NULL REFERENCES workspaces (id) ON DELETE CASCADE,
    name          text        NOT NULL,
    color         text        NOT NULL DEFAULT '#6B7280',
    created_at    timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_content_tags_id_agency UNIQUE (id, agency_id),
    CONSTRAINT ck_content_tags_name CHECK (length(name) BETWEEN 1 AND 50 AND name = btrim(name) AND name !~ '[[:cntrl:]]'),
    CONSTRAINT ck_content_tags_color CHECK (color ~ '^#[0-9A-Fa-f]{6}$')
);

CREATE INDEX IF NOT EXISTS ix_content_tags_agency ON content_tags (agency_id);
-- One tag per name in a workspace, whatever the letter case.
CREATE UNIQUE INDEX IF NOT EXISTS ux_content_tags_name ON content_tags (workspace_id, lower(name));

-- A pure link table: its key is the pair, so it has no id of its own.
CREATE TABLE IF NOT EXISTS content_post_tags (
    post_id     uuid        NOT NULL,
    tag_id      uuid        NOT NULL,
    agency_id   uuid        NOT NULL REFERENCES agencies (id) ON DELETE CASCADE,
    created_at  timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_content_post_tags PRIMARY KEY (post_id, tag_id),
    CONSTRAINT fk_content_post_tags_post FOREIGN KEY (post_id, agency_id)
        REFERENCES content_posts (id, agency_id) ON DELETE CASCADE,
    CONSTRAINT fk_content_post_tags_tag FOREIGN KEY (tag_id, agency_id)
        REFERENCES content_tags (id, agency_id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_content_post_tags_agency ON content_post_tags (agency_id);
CREATE INDEX IF NOT EXISTS ix_content_post_tags_tag ON content_post_tags (tag_id);

-- ---------------------------------------------------------------------------------------------
-- content_hashtags: stored without the leading #, in the order the editor shows them
-- ---------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS content_hashtags (
    id             uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    agency_id      uuid        NOT NULL REFERENCES agencies (id) ON DELETE CASCADE,
    post_id        uuid        NOT NULL,
    hashtag        text        NOT NULL,
    display_order  integer     NOT NULL DEFAULT 0,
    created_at     timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT fk_content_hashtags_post FOREIGN KEY (post_id, agency_id)
        REFERENCES content_posts (id, agency_id) ON DELETE CASCADE,
    CONSTRAINT ck_content_hashtags_hashtag CHECK (length(hashtag) BETWEEN 1 AND 100 AND hashtag !~ '[[:space:]#[:cntrl:]]'),
    CONSTRAINT ck_content_hashtags_order CHECK (display_order >= 0)
);

CREATE INDEX IF NOT EXISTS ix_content_hashtags_agency ON content_hashtags (agency_id);
CREATE INDEX IF NOT EXISTS ix_content_hashtags_post ON content_hashtags (post_id, display_order);
CREATE UNIQUE INDEX IF NOT EXISTS ux_content_hashtags_post_tag ON content_hashtags (post_id, lower(hashtag));

-- ---------------------------------------------------------------------------------------------
-- content_post_platforms: one post to many accounts, each with its own caption, settings and schedule
-- (social_accounts is created by a later script; see the header)
-- ---------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS content_post_platforms (
    id                 uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    agency_id          uuid        NOT NULL REFERENCES agencies (id) ON DELETE CASCADE,
    post_id            uuid        NOT NULL,
    social_account_id  uuid        NOT NULL,
    platform           text        NOT NULL,
    -- NULL means "publish the post's own body / title"; a value overrides it for this platform only.
    platform_caption   text        NULL,
    platform_title     text        NULL,
    platform_settings  jsonb       NOT NULL DEFAULT '{}',
    publish_status     text        NOT NULL DEFAULT 'pending',
    scheduled_at       timestamptz NULL,
    published_at       timestamptz NULL,
    external_post_id   text        NULL,
    external_post_url  text        NULL,
    last_error         text        NULL,
    retry_count        integer     NOT NULL DEFAULT 0,
    created_at         timestamptz NOT NULL DEFAULT now(),
    updated_at         timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT fk_content_post_platforms_post FOREIGN KEY (post_id, agency_id)
        REFERENCES content_posts (id, agency_id) ON DELETE CASCADE,
    CONSTRAINT uq_content_post_platforms UNIQUE (post_id, social_account_id),
    CONSTRAINT ck_content_post_platforms_platform CHECK (
        platform IN ('facebook', 'instagram', 'x', 'youtube', 'linkedin', 'tiktok', 'pinterest', 'threads')),
    CONSTRAINT ck_content_post_platforms_status CHECK (
        publish_status IN ('pending', 'scheduled', 'publishing', 'published', 'failed')),
    CONSTRAINT ck_content_post_platforms_caption CHECK (platform_caption IS NULL OR length(platform_caption) <= 20000),
    CONSTRAINT ck_content_post_platforms_title CHECK (platform_title IS NULL OR (length(platform_title) <= 300 AND platform_title !~ '[[:cntrl:]]')),
    CONSTRAINT ck_content_post_platforms_settings CHECK (jsonb_typeof(platform_settings) = 'object'),
    CONSTRAINT ck_content_post_platforms_scheduled CHECK (publish_status <> 'scheduled' OR scheduled_at IS NOT NULL),
    CONSTRAINT ck_content_post_platforms_published CHECK (publish_status <> 'published' OR published_at IS NOT NULL),
    CONSTRAINT ck_content_post_platforms_external_id CHECK (external_post_id IS NULL OR length(external_post_id) BETWEEN 1 AND 255),
    CONSTRAINT ck_content_post_platforms_external_url CHECK (
        external_post_url IS NULL OR (length(external_post_url) <= 2048 AND external_post_url ~ '^https?://[^[:space:]]+$')),
    -- Never put a token or a response body here: it is shown to people.
    CONSTRAINT ck_content_post_platforms_error CHECK (last_error IS NULL OR length(last_error) <= 2000),
    CONSTRAINT ck_content_post_platforms_retries CHECK (retry_count >= 0)
);

CREATE INDEX IF NOT EXISTS ix_content_post_platforms_agency ON content_post_platforms (agency_id);
CREATE INDEX IF NOT EXISTS ix_content_post_platforms_account ON content_post_platforms (social_account_id);
CREATE INDEX IF NOT EXISTS ix_content_post_platforms_due ON content_post_platforms (scheduled_at) WHERE publish_status = 'scheduled';
-- A provider's post id is unique within its account: a retried publish cannot be recorded twice.
CREATE UNIQUE INDEX IF NOT EXISTS ux_content_post_platforms_external ON content_post_platforms (social_account_id, external_post_id)
    WHERE external_post_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_content_post_platforms_settings ON content_post_platforms USING gin (platform_settings jsonb_path_ops);

-- ---------------------------------------------------------------------------------------------
-- Post editor extras: poll (one per post), call-to-action buttons, mentions, location (one per post)
-- ---------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS content_polls (
    id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    agency_id   uuid        NOT NULL REFERENCES agencies (id) ON DELETE CASCADE,
    post_id     uuid        NOT NULL,
    question    text        NOT NULL,
    ends_at     timestamptz NULL,
    created_at  timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_content_polls_id_agency UNIQUE (id, agency_id),
    CONSTRAINT uq_content_polls_post UNIQUE (post_id),
    CONSTRAINT fk_content_polls_post FOREIGN KEY (post_id, agency_id)
        REFERENCES content_posts (id, agency_id) ON DELETE CASCADE,
    CONSTRAINT ck_content_polls_question CHECK (length(question) BETWEEN 1 AND 300 AND question !~ '[[:cntrl:]]')
);

CREATE INDEX IF NOT EXISTS ix_content_polls_agency ON content_polls (agency_id);

CREATE TABLE IF NOT EXISTS content_poll_options (
    id             uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    agency_id      uuid        NOT NULL REFERENCES agencies (id) ON DELETE CASCADE,
    poll_id        uuid        NOT NULL,
    option_text    text        NOT NULL,
    display_order  integer     NOT NULL DEFAULT 0,
    created_at     timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT fk_content_poll_options_poll FOREIGN KEY (poll_id, agency_id)
        REFERENCES content_polls (id, agency_id) ON DELETE CASCADE,
    CONSTRAINT ck_content_poll_options_text CHECK (length(option_text) BETWEEN 1 AND 100 AND option_text !~ '[[:cntrl:]]'),
    CONSTRAINT ck_content_poll_options_order CHECK (display_order >= 0)
);

CREATE INDEX IF NOT EXISTS ix_content_poll_options_agency ON content_poll_options (agency_id);
CREATE INDEX IF NOT EXISTS ix_content_poll_options_poll ON content_poll_options (poll_id, display_order);
CREATE UNIQUE INDEX IF NOT EXISTS ux_content_poll_options_text ON content_poll_options (poll_id, lower(option_text));

CREATE TABLE IF NOT EXISTS content_ctas (
    id             uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    agency_id      uuid        NOT NULL REFERENCES agencies (id) ON DELETE CASCADE,
    post_id        uuid        NOT NULL,
    button_text    text        NOT NULL,
    target_url     text        NOT NULL,
    display_order  integer     NOT NULL DEFAULT 0,
    created_at     timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT fk_content_ctas_post FOREIGN KEY (post_id, agency_id)
        REFERENCES content_posts (id, agency_id) ON DELETE CASCADE,
    CONSTRAINT ck_content_ctas_text CHECK (length(button_text) BETWEEN 1 AND 60 AND button_text !~ '[[:cntrl:]]'),
    CONSTRAINT ck_content_ctas_url CHECK (length(target_url) <= 2048 AND target_url ~ '^https?://[^[:space:]]+$'),
    CONSTRAINT ck_content_ctas_order CHECK (display_order >= 0)
);

CREATE INDEX IF NOT EXISTS ix_content_ctas_agency ON content_ctas (agency_id);
CREATE INDEX IF NOT EXISTS ix_content_ctas_post ON content_ctas (post_id, display_order);

CREATE TABLE IF NOT EXISTS content_mentions (
    id                uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    agency_id         uuid        NOT NULL REFERENCES agencies (id) ON DELETE CASCADE,
    post_id           uuid        NOT NULL,
    platform          text        NOT NULL,
    mentioned_name    text        NOT NULL,
    platform_user_id  text        NULL,
    created_at        timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT fk_content_mentions_post FOREIGN KEY (post_id, agency_id)
        REFERENCES content_posts (id, agency_id) ON DELETE CASCADE,
    CONSTRAINT ck_content_mentions_platform CHECK (
        platform IN ('facebook', 'instagram', 'x', 'youtube', 'linkedin', 'tiktok', 'pinterest', 'threads')),
    CONSTRAINT ck_content_mentions_name CHECK (length(mentioned_name) BETWEEN 1 AND 200 AND mentioned_name !~ '[[:cntrl:]]'),
    CONSTRAINT ck_content_mentions_user_id CHECK (platform_user_id IS NULL OR length(platform_user_id) BETWEEN 1 AND 255)
);

CREATE INDEX IF NOT EXISTS ix_content_mentions_agency ON content_mentions (agency_id);
CREATE INDEX IF NOT EXISTS ix_content_mentions_post ON content_mentions (post_id);
-- The same person once per post and platform: by their platform id when known, by name when not.
CREATE UNIQUE INDEX IF NOT EXISTS ux_content_mentions_user ON content_mentions (post_id, platform, platform_user_id)
    WHERE platform_user_id IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_content_mentions_name ON content_mentions (post_id, platform, lower(mentioned_name))
    WHERE platform_user_id IS NULL;

CREATE TABLE IF NOT EXISTS content_locations (
    id                uuid          PRIMARY KEY DEFAULT gen_random_uuid(),
    agency_id         uuid          NOT NULL REFERENCES agencies (id) ON DELETE CASCADE,
    post_id           uuid          NOT NULL,
    location_name     text          NOT NULL,
    latitude          numeric(9, 6) NULL,
    longitude         numeric(9, 6) NULL,
    external_place_id text          NULL,
    created_at        timestamptz   NOT NULL DEFAULT now(),
    CONSTRAINT uq_content_locations_post UNIQUE (post_id),
    CONSTRAINT fk_content_locations_post FOREIGN KEY (post_id, agency_id)
        REFERENCES content_posts (id, agency_id) ON DELETE CASCADE,
    CONSTRAINT ck_content_locations_name CHECK (length(location_name) BETWEEN 1 AND 200 AND location_name !~ '[[:cntrl:]]'),
    CONSTRAINT ck_content_locations_latitude CHECK (latitude IS NULL OR latitude BETWEEN -90 AND 90),
    CONSTRAINT ck_content_locations_longitude CHECK (longitude IS NULL OR longitude BETWEEN -180 AND 180),
    CONSTRAINT ck_content_locations_both CHECK ((latitude IS NULL) = (longitude IS NULL)),
    CONSTRAINT ck_content_locations_place CHECK (external_place_id IS NULL OR length(external_place_id) BETWEEN 1 AND 255)
);

CREATE INDEX IF NOT EXISTS ix_content_locations_agency ON content_locations (agency_id);

-- ---------------------------------------------------------------------------------------------
-- content_templates: reusable starting points for every content type
-- ---------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS content_templates (
    id                uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    agency_id         uuid        NOT NULL REFERENCES agencies (id) ON DELETE CASCADE,
    workspace_id      uuid        NOT NULL REFERENCES workspaces (id) ON DELETE CASCADE,
    name              text        NOT NULL,
    description       text        NOT NULL DEFAULT '',
    content_type      text        NOT NULL,
    title_template    text        NOT NULL DEFAULT '',
    body_template     text        NOT NULL DEFAULT '',
    preview_asset_id  uuid        NULL,
    template_data     jsonb       NOT NULL DEFAULT '{}',
    is_active         boolean     NOT NULL DEFAULT true,
    created_by        uuid        NULL REFERENCES users (id) ON DELETE SET NULL,
    created_at        timestamptz NOT NULL DEFAULT now(),
    updated_at        timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_content_templates_name CHECK (length(name) BETWEEN 1 AND 120 AND name = btrim(name) AND name !~ '[[:cntrl:]]'),
    CONSTRAINT ck_content_templates_description CHECK (length(description) <= 500),
    CONSTRAINT ck_content_templates_type CHECK (content_type IN ('social_post', 'blog_post', 'video_script', 'carousel', 'short_reel')),
    CONSTRAINT ck_content_templates_title CHECK (length(title_template) <= 300 AND title_template !~ '[[:cntrl:]]'),
    CONSTRAINT ck_content_templates_body CHECK (char_length(body_template) <= 200000),
    CONSTRAINT ck_content_templates_data CHECK (jsonb_typeof(template_data) = 'object')
);

CREATE INDEX IF NOT EXISTS ix_content_templates_agency ON content_templates (agency_id);
CREATE INDEX IF NOT EXISTS ix_content_templates_workspace_type ON content_templates (workspace_id, content_type) WHERE is_active;
CREATE UNIQUE INDEX IF NOT EXISTS ux_content_templates_name ON content_templates (workspace_id, lower(name));
CREATE INDEX IF NOT EXISTS ix_content_templates_preview ON content_templates (preview_asset_id) WHERE preview_asset_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_content_templates_created_by ON content_templates (created_by) WHERE created_by IS NOT NULL;

-- ---------------------------------------------------------------------------------------------
-- content_ai_generations: what was asked of an AI provider and what came back. A generation outlives the post it was made for
-- (post_id becomes NULL when the post is deleted), so the record of the request, and what it cost, is kept.
-- ---------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS content_ai_generations (
    id               uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    agency_id        uuid        NOT NULL REFERENCES agencies (id) ON DELETE CASCADE,
    workspace_id     uuid        NOT NULL REFERENCES workspaces (id) ON DELETE CASCADE,
    post_id          uuid        NULL,
    requested_by     uuid        NULL REFERENCES users (id) ON DELETE SET NULL,
    generation_type  text        NOT NULL,
    provider         text        NOT NULL,
    model_name       text        NOT NULL,
    prompt           text        NOT NULL,
    result_text      text        NULL,
    result_data      jsonb       NOT NULL DEFAULT '{}',
    output_media_id  uuid        NULL,
    status           text        NOT NULL DEFAULT 'pending',
    error_message    text        NULL,
    created_at       timestamptz NOT NULL DEFAULT now(),
    -- MATCH SIMPLE: with post_id NULL the key is not checked. SET NULL (post_id) keeps agency_id, which is NOT NULL.
    CONSTRAINT fk_content_ai_generations_post FOREIGN KEY (post_id, agency_id)
        REFERENCES content_posts (id, agency_id) ON DELETE SET NULL (post_id),
    CONSTRAINT ck_content_ai_generations_type CHECK (
        generation_type IN ('caption', 'rewrite', 'hashtags', 'image', 'video', 'blog', 'ideas', 'other')),
    CONSTRAINT ck_content_ai_generations_status CHECK (status IN ('pending', 'completed', 'failed')),
    CONSTRAINT ck_content_ai_generations_provider CHECK (length(provider) BETWEEN 1 AND 60),
    CONSTRAINT ck_content_ai_generations_model CHECK (length(model_name) BETWEEN 1 AND 120),
    CONSTRAINT ck_content_ai_generations_prompt CHECK (length(prompt) BETWEEN 1 AND 20000),
    CONSTRAINT ck_content_ai_generations_result CHECK (result_text IS NULL OR char_length(result_text) <= 200000),
    CONSTRAINT ck_content_ai_generations_data CHECK (jsonb_typeof(result_data) = 'object'),
    CONSTRAINT ck_content_ai_generations_error CHECK (error_message IS NULL OR length(error_message) <= 2000)
);

CREATE INDEX IF NOT EXISTS ix_content_ai_generations_agency ON content_ai_generations (agency_id);
CREATE INDEX IF NOT EXISTS ix_content_ai_generations_workspace ON content_ai_generations (workspace_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_content_ai_generations_post ON content_ai_generations (post_id, created_at DESC) WHERE post_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_content_ai_generations_requested_by ON content_ai_generations (requested_by) WHERE requested_by IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_content_ai_generations_output ON content_ai_generations (output_media_id) WHERE output_media_id IS NOT NULL;

-- ---------------------------------------------------------------------------------------------
-- content_revisions: written by the trigger below, never by hand (see the header)
-- ---------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS content_revisions (
    id               uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    agency_id        uuid        NOT NULL REFERENCES agencies (id) ON DELETE CASCADE,
    post_id          uuid        NOT NULL,
    revision_number  integer     NOT NULL,
    title            text        NOT NULL,
    body             text        NOT NULL,
    -- Everything else about the post as saved (without title, body and the search vector, which are not copied twice).
    snapshot         jsonb       NOT NULL,
    changed_by       uuid        NULL REFERENCES users (id) ON DELETE SET NULL,
    created_at       timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT fk_content_revisions_post FOREIGN KEY (post_id, agency_id)
        REFERENCES content_posts (id, agency_id) ON DELETE CASCADE,
    CONSTRAINT uq_content_revisions UNIQUE (post_id, revision_number),
    CONSTRAINT ck_content_revisions_number CHECK (revision_number >= 1),
    CONSTRAINT ck_content_revisions_snapshot CHECK (jsonb_typeof(snapshot) = 'object')
);

CREATE INDEX IF NOT EXISTS ix_content_revisions_agency ON content_revisions (agency_id);
CREATE INDEX IF NOT EXISTS ix_content_revisions_changed_by ON content_revisions (changed_by) WHERE changed_by IS NOT NULL;

-- ---------------------------------------------------------------------------------------------
-- Privileges. New tables are usable by app_user through the default privileges of 001; row-level security decides which rows.
-- History is append-only for the application role.
-- ---------------------------------------------------------------------------------------------
REVOKE ALL ON content_revisions FROM app_user;
GRANT SELECT, INSERT ON content_revisions TO app_user;

-- ---------------------------------------------------------------------------------------------
-- Row-level security: enabled and forced on every table, one policy: the current organization only.
-- ---------------------------------------------------------------------------------------------
DO $$
DECLARE
    t text;
BEGIN
    FOREACH t IN ARRAY ARRAY[
        'content_posts', 'content_post_media', 'content_tags', 'content_post_tags', 'content_hashtags',
        'content_post_platforms', 'content_polls', 'content_poll_options', 'content_ctas', 'content_mentions',
        'content_locations', 'content_templates', 'content_ai_generations', 'content_revisions']
    LOOP
        EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY', t);
        EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY', t);
        EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON %I', t);
        EXECUTE format(
            'CREATE POLICY tenant_isolation ON %I USING (agency_id = app_agency_id()) WITH CHECK (agency_id = app_agency_id())', t);
    END LOOP;
END
$$;

-- ---------------------------------------------------------------------------------------------
-- Triggers
-- ---------------------------------------------------------------------------------------------
-- Runs with the privileges of whoever saves the post (app_user): it needs INSERT on content_revisions, which app_user has, and
-- the row it writes passes the tenant policy because it carries the post's own agency_id. Two edits of one post cannot number
-- their revisions at the same time: the second UPDATE waits for the first one's row lock, and so sees its revision.
CREATE OR REPLACE FUNCTION content_posts_capture_revision()
    RETURNS trigger
    LANGUAGE plpgsql
    SET search_path = public, pg_temp
AS $$
BEGIN
    IF TG_OP = 'UPDATE'
       AND NEW.title = OLD.title
       AND NEW.body = OLD.body
       AND NEW.excerpt = OLD.excerpt
       AND NEW.content_type = OLD.content_type
       AND NEW.seo_title IS NOT DISTINCT FROM OLD.seo_title
       AND NEW.seo_description IS NOT DISTINCT FROM OLD.seo_description
       AND NEW.seo_slug IS NOT DISTINCT FROM OLD.seo_slug
       AND NEW.seo_keywords = OLD.seo_keywords
    THEN
        RETURN NULL;
    END IF;

    INSERT INTO content_revisions (agency_id, post_id, revision_number, title, body, snapshot, changed_by)
    SELECT NEW.agency_id,
           NEW.id,
           coalesce(max(r.revision_number), 0) + 1,
           NEW.title,
           NEW.body,
           to_jsonb(NEW) - 'search_vector' - 'title' - 'body',
           coalesce(NEW.updated_by, NEW.created_by)
    FROM content_revisions r
    WHERE r.post_id = NEW.id;

    RETURN NULL;
END
$$;

DROP TRIGGER IF EXISTS trg_content_posts_revision ON content_posts;
CREATE TRIGGER trg_content_posts_revision
    AFTER INSERT OR UPDATE ON content_posts
    FOR EACH ROW EXECUTE FUNCTION content_posts_capture_revision();

DROP TRIGGER IF EXISTS trg_content_posts_updated_at ON content_posts;
CREATE TRIGGER trg_content_posts_updated_at
    BEFORE UPDATE ON content_posts
    FOR EACH ROW EXECUTE FUNCTION content_set_updated_at();

DROP TRIGGER IF EXISTS trg_content_post_platforms_updated_at ON content_post_platforms;
CREATE TRIGGER trg_content_post_platforms_updated_at
    BEFORE UPDATE ON content_post_platforms
    FOR EACH ROW EXECUTE FUNCTION content_set_updated_at();

DROP TRIGGER IF EXISTS trg_content_templates_updated_at ON content_templates;
CREATE TRIGGER trg_content_templates_updated_at
    BEFORE UPDATE ON content_templates
    FOR EACH ROW EXECUTE FUNCTION content_set_updated_at();
