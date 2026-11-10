-- 011_channels.sql
-- Channels the platform sends through, managed by the Site admin; which organizations may use which channel; and
-- deactivating a whole organization.
--
-- Idempotent like every script: DbMigrator runs all of them on every start. Once released, never edit this file; add a
-- new numbered script instead.
--
-- Design:
--   * agencies.is_active (an existing table, approved): a deactivated organization keeps its rows and data, but nobody in it
--     can sign in, and every member's sessions end the moment it is deactivated (session_revocations, as for a deactivated
--     member). Existing organizations stay active (DEFAULT true). Only the Site admin's function changes it: app_user has UPDATE on the
--     other columns of agencies as before, and not on this one.
--   * channels is a global configuration table (no agency_id, not a tenant table). app_user has NO privileges on it: the
--     uploaded icon bytes and the creator's id are only ever read through the site_admin_* functions below, each of which
--     checks site_admins inside the database. A channel's icon is either a built-in glyph the web client draws (icon_key,
--     used by the seven seeded channels) or an uploaded image (icon_image + icon_content_type), never both. The API accepts
--     only PNG, JPEG and WebP of at most 100 KB; the CHECKs repeat the limits.
--   * organization_channels is a tenant table (the mapping). A row means the channel is enabled for that organization; disabling
--     deletes the row. app_user may only SELECT it (its own organization's rows, through tenant_isolation); every write is a
--     function below.
--   * 007 and 008 re-create their functions on every start, so none of them can change here. The changes are new functions:
--     site_admin_list_agencies_v3 (the v2 return type is frozen), invitation_find_v3 and invitation_accept_v2 (an invitation into
--     a deactivated organization stops working). app_user's execute on invitation_find_v2 and invitation_accept is taken away,
--     as 008 did for the 007 team functions.
--   * Rule codes are raised as P0001 with the code as the message: organization_not_found, channel_not_found.

-- ---------------------------------------------------------------------------------------------
-- agencies.is_active
-- ---------------------------------------------------------------------------------------------
ALTER TABLE agencies ADD COLUMN IF NOT EXISTS is_active boolean NOT NULL DEFAULT true;

-- The tenant policy lets a connection acting for an organization update that organization's own row, and 001 grants UPDATE on the
-- whole table, so a bug or an injection in the API could otherwise reactivate a deactivated organization (or deactivate its own).
-- Only site_admin_set_organization_active may change is_active, so app_user keeps UPDATE on the columns it always had and
-- loses it on this one. 001 grants the whole table again on every start; this script runs after it and takes it back.
REVOKE UPDATE ON agencies FROM app_user;
GRANT UPDATE (name, plan, business_model) ON agencies TO app_user;

-- ---------------------------------------------------------------------------------------------
-- channels
-- ---------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS channels (
    id                 uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    name               text        NOT NULL,
    description        text        NOT NULL DEFAULT '',
    icon_key           text        NULL,
    icon_image         bytea       NULL,
    icon_content_type  text        NULL,
    created_by         uuid        NULL REFERENCES users (id) ON DELETE SET NULL,
    created_at         timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_channels_name CHECK (length(name) BETWEEN 1 AND 60 AND name = btrim(name) AND name !~ '[[:cntrl:]]'),
    CONSTRAINT ck_channels_description CHECK (length(description) <= 300 AND description !~ '[[:cntrl:]]'),
    CONSTRAINT ck_channels_icon_key CHECK (icon_key IS NULL OR icon_key ~ '^[a-z][a-z0-9-]{0,29}$'),
    CONSTRAINT ck_channels_icon_image CHECK (icon_image IS NULL OR length(icon_image) BETWEEN 1 AND 102400),
    CONSTRAINT ck_channels_icon_type CHECK (icon_content_type IS NULL OR icon_content_type IN ('image/png', 'image/jpeg', 'image/webp')),
    CONSTRAINT ck_channels_icon_one_of CHECK (
        (icon_key IS NOT NULL AND icon_image IS NULL AND icon_content_type IS NULL)
        OR (icon_key IS NULL AND icon_image IS NOT NULL AND icon_content_type IS NOT NULL))
);

-- One channel per name, whatever the letter case (names are stored trimmed).
CREATE UNIQUE INDEX IF NOT EXISTS ux_channels_name ON channels (lower(name));
CREATE INDEX IF NOT EXISTS ix_channels_created_by ON channels (created_by) WHERE created_by IS NOT NULL;

REVOKE ALL ON channels FROM app_user;

ALTER TABLE channels ENABLE ROW LEVEL SECURITY;
ALTER TABLE channels FORCE ROW LEVEL SECURITY;

-- The migrating role's own policy comes before the seed below: FORCE ROW LEVEL SECURITY also binds the table owner, so
-- an owner that is not a superuser could not insert the seed without it. For the owner only, by name, never for app_user.
DO $$
BEGIN
    EXECUTE 'DROP POLICY IF EXISTS owner_manage ON channels';
    EXECUTE format('CREATE POLICY owner_manage ON channels FOR ALL TO %I USING (true) WITH CHECK (true)', current_user);
END
$$;

-- The seven channels the product is about. Built-in icons; no creator. Inserted in this order (clock_timestamp moves on per
-- row), and never again once present: a restart does not bring back what is there.
INSERT INTO channels (name, description, icon_key, created_at)
SELECT s.name, s.description, s.icon_key, clock_timestamp()
FROM (VALUES
        (1, 'Instagram', 'Publish posts and stories to Instagram.', 'instagram'),
        (2, 'Facebook',  'Publish posts to Facebook pages.',        'facebook'),
        (3, 'YouTube',   'Publish videos to YouTube.',              'youtube'),
        (4, 'Email',     'Send email campaigns to contacts.',       'email'),
        (5, 'SMS',       'Send text messages to contacts.',         'sms'),
        (6, 'WhatsApp',  'Send messages on WhatsApp.',              'whatsapp'),
        (7, 'LinkedIn',  'Publish posts to LinkedIn.',              'linkedin')
     ) AS s (n, name, description, icon_key)
ORDER BY s.n
ON CONFLICT ((lower(name))) DO NOTHING;

-- ---------------------------------------------------------------------------------------------
-- organization_channels
-- ---------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS organization_channels (
    id           uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    agency_id    uuid        NOT NULL REFERENCES agencies (id) ON DELETE CASCADE,
    channel_id   uuid        NOT NULL REFERENCES channels (id) ON DELETE CASCADE,
    assigned_by  uuid        NULL REFERENCES users (id) ON DELETE SET NULL,
    created_at   timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_organization_channels UNIQUE (agency_id, channel_id)
);

CREATE INDEX IF NOT EXISTS ix_organization_channels_channel ON organization_channels (channel_id);
CREATE INDEX IF NOT EXISTS ix_organization_channels_assigned_by ON organization_channels (assigned_by) WHERE assigned_by IS NOT NULL;

-- Read only for the application role: a member can see their own organization's channels, never change them.
REVOKE ALL ON organization_channels FROM app_user;
GRANT SELECT ON organization_channels TO app_user;

ALTER TABLE organization_channels ENABLE ROW LEVEL SECURITY;
ALTER TABLE organization_channels FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation ON organization_channels;
CREATE POLICY tenant_isolation ON organization_channels
    USING (agency_id = app_agency_id())
    WITH CHECK (agency_id = app_agency_id());

-- ---------------------------------------------------------------------------------------------
-- More policies for the migrating role (channels got its own above). The functions below run as the owner: a superuser skips
-- the policies anyway, any other owner needs these. For the owner only, by name, never for app_user. agencies already has
-- the owner's SELECT policy (002); the functions also update it.
-- ---------------------------------------------------------------------------------------------
DO $$
BEGIN
    EXECUTE 'DROP POLICY IF EXISTS owner_manage ON organization_channels';
    EXECUTE format('CREATE POLICY owner_manage ON organization_channels FOR ALL TO %I USING (true) WITH CHECK (true)', current_user);
    EXECUTE 'DROP POLICY IF EXISTS owner_site_update ON agencies';
    EXECUTE format('CREATE POLICY owner_site_update ON agencies FOR UPDATE TO %I USING (true) WITH CHECK (true)', current_user);
END
$$;

-- ---------------------------------------------------------------------------------------------
-- Internal helpers. Not callable by app_user: only the functions below call them.
-- ---------------------------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION site_admin_require()
    RETURNS void
    LANGUAGE plpgsql STABLE SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM site_admins s WHERE s.user_id = app_user_id()) THEN
        RAISE EXCEPTION 'Site admin access is required.' USING ERRCODE = '42501';
    END IF;
END
$$;

-- The channels as a list shows them, all of them or one. The creator is shown by name, or by address when there is no name.
CREATE OR REPLACE FUNCTION channel_rows(p_only uuid DEFAULT NULL)
    RETURNS TABLE (
        id uuid,
        name text,
        description text,
        icon_key text,
        has_image boolean,
        created_by_name text,
        created_at timestamptz,
        organization_count bigint)
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
    SELECT c.id,
           c.name,
           c.description,
           c.icon_key,
           c.icon_image IS NOT NULL,
           coalesce(nullif(u.full_name, ''), u.email),
           c.created_at,
           (SELECT count(*) FROM organization_channels oc WHERE oc.channel_id = c.id)
    FROM channels c
    LEFT JOIN users u ON u.id = c.created_by
    WHERE p_only IS NULL OR c.id = p_only
    ORDER BY c.created_at, c.name
$$;

REVOKE ALL ON FUNCTION site_admin_require() FROM PUBLIC;
REVOKE ALL ON FUNCTION channel_rows(uuid) FROM PUBLIC;

-- ---------------------------------------------------------------------------------------------
-- Channels: read and create (Site admin only)
-- ---------------------------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION site_admin_list_channels()
    RETURNS TABLE (
        id uuid,
        name text,
        description text,
        icon_key text,
        has_image boolean,
        created_by_name text,
        created_at timestamptz,
        organization_count bigint)
    LANGUAGE plpgsql STABLE SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
BEGIN
    PERFORM site_admin_require();
    RETURN QUERY SELECT * FROM channel_rows(NULL);
END
$$;

-- Creates a channel for the calling Site admin and returns it as a list shows it. A duplicate name (any letter case) is a
-- unique violation on ux_channels_name; a bad name, description or icon is a CHECK violation. The API validates first.
CREATE OR REPLACE FUNCTION site_admin_create_channel(
    p_name         text,
    p_description  text,
    p_icon_key     text,
    p_image        bytea,
    p_content_type text)
    RETURNS TABLE (
        id uuid,
        name text,
        description text,
        icon_key text,
        has_image boolean,
        created_by_name text,
        created_at timestamptz,
        organization_count bigint)
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
DECLARE
    v_id uuid;
BEGIN
    PERFORM site_admin_require();

    INSERT INTO channels (name, description, icon_key, icon_image, icon_content_type, created_by)
    VALUES (btrim(p_name), coalesce(btrim(p_description), ''), p_icon_key, p_image, p_content_type, app_user_id())
    RETURNING channels.id INTO v_id;

    RETURN QUERY SELECT * FROM channel_rows(v_id);
END
$$;

-- The uploaded image of a channel. No row for a channel with a built-in icon, or one that does not exist.
CREATE OR REPLACE FUNCTION site_admin_channel_icon(p_id uuid)
    RETURNS TABLE (content_type text, image bytea)
    LANGUAGE plpgsql STABLE SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
BEGIN
    PERFORM site_admin_require();
    RETURN QUERY SELECT c.icon_content_type, c.icon_image FROM channels c WHERE c.id = p_id AND c.icon_image IS NOT NULL;
END
$$;

-- ---------------------------------------------------------------------------------------------
-- Channels of one organization (Site admin only)
-- ---------------------------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION site_admin_organization_channels(p_agency_id uuid)
    RETURNS TABLE (
        id uuid,
        name text,
        description text,
        icon_key text,
        has_image boolean,
        is_enabled boolean)
    LANGUAGE plpgsql STABLE SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
BEGIN
    PERFORM site_admin_require();

    IF NOT EXISTS (SELECT 1 FROM agencies a WHERE a.id = p_agency_id) THEN
        RAISE EXCEPTION 'organization_not_found' USING ERRCODE = 'P0001';
    END IF;

    RETURN QUERY
    SELECT c.id,
           c.name,
           c.description,
           c.icon_key,
           c.icon_image IS NOT NULL,
           EXISTS (SELECT 1 FROM organization_channels oc WHERE oc.agency_id = p_agency_id AND oc.channel_id = c.id)
    FROM channels c
    ORDER BY c.created_at, c.name;
END
$$;

-- Enables or disables a channel for an organization (every kind of organization may have any channel). True when something
-- changed, false when it already was that way, so repeating a call is harmless.
CREATE OR REPLACE FUNCTION site_admin_set_organization_channel(p_agency_id uuid, p_channel_id uuid, p_enabled boolean)
    RETURNS boolean
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
DECLARE
    v_rows integer;
BEGIN
    PERFORM site_admin_require();

    IF p_enabled IS NULL THEN
        RAISE EXCEPTION 'Enabled must be true or false.' USING ERRCODE = '22023';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM agencies a WHERE a.id = p_agency_id) THEN
        RAISE EXCEPTION 'organization_not_found' USING ERRCODE = 'P0001';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM channels c WHERE c.id = p_channel_id) THEN
        RAISE EXCEPTION 'channel_not_found' USING ERRCODE = 'P0001';
    END IF;

    IF p_enabled THEN
        INSERT INTO organization_channels (agency_id, channel_id, assigned_by)
        VALUES (p_agency_id, p_channel_id, app_user_id())
        ON CONFLICT ON CONSTRAINT uq_organization_channels DO NOTHING;
    ELSE
        DELETE FROM organization_channels oc WHERE oc.agency_id = p_agency_id AND oc.channel_id = p_channel_id;
    END IF;

    GET DIAGNOSTICS v_rows = ROW_COUNT;
    RETURN v_rows > 0;
END
$$;

-- ---------------------------------------------------------------------------------------------
-- Organizations: the list with whether each is active and how many channels it has, and deactivating
-- ---------------------------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION site_admin_list_agencies_v3(p_limit integer, p_offset integer)
    RETURNS TABLE (
        id uuid,
        name text,
        business_model text,
        plan text,
        created_at timestamptz,
        member_count bigint,
        workspace_count bigint,
        is_active boolean,
        channel_count bigint,
        total_count bigint)
    LANGUAGE plpgsql STABLE SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
BEGIN
    PERFORM site_admin_require();

    IF p_limit IS NULL OR p_limit NOT BETWEEN 1 AND 100 OR p_offset IS NULL OR p_offset < 0 THEN
        RAISE EXCEPTION 'Invalid paging arguments.' USING ERRCODE = '22023';
    END IF;

    RETURN QUERY
    SELECT a.id,
           a.name,
           a.business_model,
           a.plan,
           a.created_at,
           (SELECT count(*) FROM agency_members m WHERE m.agency_id = a.id),
           (SELECT count(*) FROM workspaces w WHERE w.agency_id = a.id),
           a.is_active,
           (SELECT count(*) FROM organization_channels oc WHERE oc.agency_id = a.id),
           count(*) OVER ()
    FROM agencies a
    ORDER BY a.created_at DESC, a.id
    LIMIT p_limit OFFSET p_offset;
END
$$;

-- Deactivates or activates an organization. Deactivating ends every member's sessions at once (a token issued up to now is
-- void) and keeps everything else; activating changes no session, the people sign in again. True when the state changed.
-- The per-organization lock of the team functions is taken first, so this does not interleave with a team change or an
-- invitation being accepted.
CREATE OR REPLACE FUNCTION site_admin_set_organization_active(p_agency_id uuid, p_active boolean)
    RETURNS boolean
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
DECLARE
    v_rows integer;
BEGIN
    PERFORM site_admin_require();

    IF p_active IS NULL THEN
        RAISE EXCEPTION 'Active must be true or false.' USING ERRCODE = '22023';
    END IF;

    PERFORM pg_advisory_xact_lock(hashtextextended('team:' || coalesce(p_agency_id::text, ''), 0));

    UPDATE agencies a SET is_active = p_active WHERE a.id = p_agency_id AND a.is_active IS DISTINCT FROM p_active;
    GET DIAGNOSTICS v_rows = ROW_COUNT;

    IF v_rows = 0 AND NOT EXISTS (SELECT 1 FROM agencies a WHERE a.id = p_agency_id) THEN
        RAISE EXCEPTION 'organization_not_found' USING ERRCODE = 'P0001';
    END IF;

    IF v_rows > 0 AND NOT p_active THEN
        INSERT INTO session_revocations (agency_id, user_id, valid_after)
        SELECT m.agency_id, m.user_id, clock_timestamp()
        FROM agency_members m
        WHERE m.agency_id = p_agency_id
        ON CONFLICT ON CONSTRAINT uq_session_revocations_agency_user DO UPDATE SET valid_after = EXCLUDED.valid_after;
    END IF;

    RETURN v_rows > 0;
END
$$;

-- ---------------------------------------------------------------------------------------------
-- Invitations into a deactivated organization stop working
-- ---------------------------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION invitation_find_v3(p_token_hash text)
    RETURNS TABLE (id uuid, agency_id uuid, organization_name text, business_model text, email text, role text,
                   expires_at timestamptz, full_name text)
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
    SELECT i.id, i.agency_id, a.name, i.business_model, i.email, i.role, i.expires_at, i.full_name
    FROM member_invitations i
    JOIN agencies a ON a.id = i.agency_id
    WHERE i.token_hash = p_token_hash
      AND i.accepted_at IS NULL
      AND i.revoked_at IS NULL
      AND i.expires_at > now()
      AND a.is_active
$$;

-- The 007 function with one more rule: the organization must be active. It takes the organization's lock first, so an
-- invitation cannot be accepted while the organization is being deactivated.
CREATE OR REPLACE FUNCTION invitation_accept_v2(
    p_token_hash     text,
    p_user_id        uuid,
    p_full_name      text,
    p_password_hash  text,
    p_provider       text DEFAULT NULL,
    p_subject        text DEFAULT NULL,
    p_provider_email text DEFAULT NULL)
    RETURNS TABLE (agency_id uuid, role text)
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
DECLARE
    v_agency uuid;
BEGIN
    SELECT i.agency_id INTO v_agency FROM member_invitations i WHERE i.token_hash = p_token_hash;

    IF v_agency IS NOT NULL THEN
        PERFORM pg_advisory_xact_lock(hashtextextended('team:' || v_agency::text, 0));

        IF EXISTS (SELECT 1 FROM agencies a WHERE a.id = v_agency AND NOT a.is_active) THEN
            RAISE EXCEPTION 'invitation_invalid' USING ERRCODE = 'P0001';
        END IF;
    END IF;

    RETURN QUERY
    SELECT r.agency_id, r.role
    FROM invitation_accept(p_token_hash, p_user_id, p_full_name, p_password_hash, p_provider, p_subject, p_provider_email) AS r;
END
$$;

-- ---------------------------------------------------------------------------------------------
-- Who may call what
-- ---------------------------------------------------------------------------------------------
REVOKE ALL ON FUNCTION site_admin_list_channels() FROM PUBLIC;
REVOKE ALL ON FUNCTION site_admin_create_channel(text, text, text, bytea, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION site_admin_channel_icon(uuid) FROM PUBLIC;
REVOKE ALL ON FUNCTION site_admin_organization_channels(uuid) FROM PUBLIC;
REVOKE ALL ON FUNCTION site_admin_set_organization_channel(uuid, uuid, boolean) FROM PUBLIC;
REVOKE ALL ON FUNCTION site_admin_list_agencies_v3(integer, integer) FROM PUBLIC;
REVOKE ALL ON FUNCTION site_admin_set_organization_active(uuid, boolean) FROM PUBLIC;
REVOKE ALL ON FUNCTION invitation_find_v3(text) FROM PUBLIC;
REVOKE ALL ON FUNCTION invitation_accept_v2(text, uuid, text, text, text, text, text) FROM PUBLIC;

GRANT EXECUTE ON FUNCTION site_admin_list_channels() TO app_user;
GRANT EXECUTE ON FUNCTION site_admin_create_channel(text, text, text, bytea, text) TO app_user;
GRANT EXECUTE ON FUNCTION site_admin_channel_icon(uuid) TO app_user;
GRANT EXECUTE ON FUNCTION site_admin_organization_channels(uuid) TO app_user;
GRANT EXECUTE ON FUNCTION site_admin_set_organization_channel(uuid, uuid, boolean) TO app_user;
GRANT EXECUTE ON FUNCTION site_admin_list_agencies_v3(integer, integer) TO app_user;
GRANT EXECUTE ON FUNCTION site_admin_set_organization_active(uuid, boolean) TO app_user;
GRANT EXECUTE ON FUNCTION invitation_find_v3(text) TO app_user;
GRANT EXECUTE ON FUNCTION invitation_accept_v2(text, uuid, text, text, text, text, text) TO app_user;

-- 007 and 008 grant these to app_user on every start; this script runs after them and takes the grant back, so the API
-- reaches an invitation only through the versions that know about deactivated organizations.
REVOKE EXECUTE ON FUNCTION invitation_find_v2(text) FROM app_user;
REVOKE EXECUTE ON FUNCTION invitation_accept(text, uuid, text, text, text, text, text) FROM app_user;
