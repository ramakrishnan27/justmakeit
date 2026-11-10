-- 015_content_studio_posts.sql
-- What the Content Studio API needs on top of the tables of 014_content_studio.sql: two permissions, the rich-text document of a
-- post, publishing targets that exist before a social account is connected, a revision trigger that also sees the document, and
-- the one function the publishing worker uses to claim what is due.
--
-- Idempotent like every script: DbMigrator runs all of them on every start. Once released, never edit this file; add a new
-- numbered script instead. This script changes only tables that 014 created (nothing else existing). 014 re-creates its revision
-- function and trigger on every start, so this script does not redefine them: it adds a v2 of each and drops the v1 trigger
-- (every start: 014 puts it back, this script takes it away again, in the same transaction).
--
-- Design:
--   * Permissions contentstudio.create and contentstudio.publish. The first is held by every role that changes data
--     (organization admin, marketing manager, content writer). The second by organization admins and marketing managers, and by the
--     content writer of an individual creator, who works alone: a content writer in a company or an agency creates drafts for
--     someone else to publish. The Site admin can change any of it per role; the seed is INSERT ... ON CONFLICT DO NOTHING and
--     never updates a grant, so a restart does not undo that. Seeding raises the permission version once, so everybody signs in
--     again once.
--   * content_posts.body_doc is the document the editor works on (a JSON object, at most 1 MB as text). The server validates it
--     against an allowlist of node and mark types and derives the plain text from it: content_posts.body stays the plain-text
--     projection (search, excerpt, and the text sent to platforms), never taken from the client.
--   * content_post_platforms.social_account_id may be NULL: there is nothing to connect an account to yet, so a target is "this
--     post, to this platform" until an account is connected. One such target per platform per post.
--   * content_claim_due_publications() is how the publishing worker finds work across organizations, which a request-scoped
--     connection cannot (row-level security). A narrow SECURITY DEFINER function: it claims the due targets in one statement
--     (FOR UPDATE SKIP LOCKED, so two instances never take the same one), marks them 'publishing' and returns only ids; the
--     worker then does the real work on a connection scoped to that organization. A target left 'publishing' by a process that
--     died is claimed again after p_stale_minutes.

-- ---------------------------------------------------------------------------------------------
-- Permissions and their default grants
-- ---------------------------------------------------------------------------------------------
INSERT INTO permissions (key, title, description, is_protected) VALUES
    ('contentstudio.create', 'Create content',
        'Create, edit, duplicate and delete drafts in Content Studio, and save templates and tags.', false),
    ('contentstudio.publish', 'Publish content',
        'Schedule, publish, unschedule, archive and restore content in Content Studio, and delete content that is no longer a draft.', false)
ON CONFLICT (key) DO NOTHING;

-- Eight (model, role) slots by two keys.
INSERT INTO role_permissions (business_model, role, permission_key, is_granted)
SELECT s.business_model,
       s.role,
       k.key,
       CASE k.key
           WHEN 'contentstudio.create' THEN s.role IN ('organizationadmin', 'marketingmanager', 'contentwriter')
           ELSE s.role IN ('organizationadmin', 'marketingmanager') OR (s.business_model = 'creator' AND s.role = 'contentwriter')
       END
FROM (VALUES
        ('company', 'organizationadmin'),
        ('company', 'marketingmanager'),
        ('company', 'contentwriter'),
        ('agency', 'organizationadmin'),
        ('agency', 'clientmanager'),
        ('agency', 'marketingmanager'),
        ('agency', 'contentwriter'),
        ('creator', 'contentwriter')
     ) AS s (business_model, role)
CROSS JOIN (VALUES
        ('contentstudio.create'),
        ('contentstudio.publish')
     ) AS k (key)
ON CONFLICT (business_model, role, permission_key) DO NOTHING;

-- ---------------------------------------------------------------------------------------------
-- The editor's document
-- ---------------------------------------------------------------------------------------------
ALTER TABLE content_posts ADD COLUMN IF NOT EXISTS body_doc jsonb NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'ck_content_posts_body_doc' AND conrelid = 'content_posts'::regclass) THEN
        ALTER TABLE content_posts ADD CONSTRAINT ck_content_posts_body_doc
            CHECK (body_doc IS NULL OR (jsonb_typeof(body_doc) = 'object' AND octet_length(body_doc::text) <= 1000000));
    END IF;
END
$$;

-- ---------------------------------------------------------------------------------------------
-- Publishing targets without an account yet
-- ---------------------------------------------------------------------------------------------
ALTER TABLE content_post_platforms ALTER COLUMN social_account_id DROP NOT NULL;

-- uq_content_post_platforms (post, account) cannot see two NULL accounts as equal, so this says: one account-less target per
-- platform per post.
CREATE UNIQUE INDEX IF NOT EXISTS ux_content_post_platforms_no_account
    ON content_post_platforms (post_id, platform) WHERE social_account_id IS NULL;

-- ---------------------------------------------------------------------------------------------
-- Revisions: also count a change to the document. Same as content_posts_capture_revision() of 014, plus body_doc.
-- ---------------------------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION content_posts_capture_revision_v2()
    RETURNS trigger
    LANGUAGE plpgsql
    SET search_path = public, pg_temp
AS $$
BEGIN
    IF TG_OP = 'UPDATE'
       AND NEW.title = OLD.title
       AND NEW.body = OLD.body
       AND NEW.body_doc IS NOT DISTINCT FROM OLD.body_doc
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
DROP TRIGGER IF EXISTS trg_content_posts_revision_v2 ON content_posts;
CREATE TRIGGER trg_content_posts_revision_v2
    AFTER INSERT OR UPDATE ON content_posts
    FOR EACH ROW EXECUTE FUNCTION content_posts_capture_revision_v2();

-- ---------------------------------------------------------------------------------------------
-- Claiming what is due. For a database owner that is not a superuser the function needs to see and change these two tables
-- through row-level security: policies for the migrating role only, by name, never for app_user.
-- ---------------------------------------------------------------------------------------------
DO $$
BEGIN
    EXECUTE 'DROP POLICY IF EXISTS owner_claim ON content_post_platforms';
    EXECUTE format('CREATE POLICY owner_claim ON content_post_platforms FOR ALL TO %I USING (true) WITH CHECK (true)', current_user);
    EXECUTE 'DROP POLICY IF EXISTS owner_claim ON content_posts';
    EXECUTE format('CREATE POLICY owner_claim ON content_posts FOR SELECT TO %I USING (true)', current_user);
END
$$;

CREATE OR REPLACE FUNCTION content_claim_due_publications(p_limit integer, p_stale_minutes integer DEFAULT 10)
    RETURNS TABLE (agency_id uuid, post_id uuid, publication_id uuid)
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
#variable_conflict use_column
BEGIN
    IF p_limit IS NULL OR p_limit NOT BETWEEN 1 AND 100 THEN
        RAISE EXCEPTION 'The limit must be between 1 and 100.' USING ERRCODE = '22023';
    END IF;
    IF p_stale_minutes IS NULL OR p_stale_minutes NOT BETWEEN 1 AND 1440 THEN
        RAISE EXCEPTION 'The stale time must be between 1 and 1440 minutes.' USING ERRCODE = '22023';
    END IF;

    RETURN QUERY
    WITH due AS (
        SELECT c.id
        FROM content_post_platforms c
        JOIN content_posts p ON p.id = c.post_id AND p.agency_id = c.agency_id
        WHERE p.status IN ('scheduled', 'publishing')
          AND ((c.publish_status = 'scheduled' AND c.scheduled_at <= now())
               OR (c.publish_status = 'publishing' AND c.updated_at < now() - make_interval(mins => p_stale_minutes)))
        ORDER BY c.scheduled_at NULLS LAST, c.id
        LIMIT p_limit
        FOR UPDATE OF c SKIP LOCKED
    )
    UPDATE content_post_platforms u
       SET publish_status = 'publishing'
      FROM due
     WHERE u.id = due.id
    RETURNING u.agency_id, u.post_id, u.id;
END
$$;

REVOKE ALL ON FUNCTION content_claim_due_publications(integer, integer) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION content_claim_due_publications(integer, integer) TO app_user;
