-- 002_site_admin.sql
-- Platform-wide site admins: people above all agencies who see platform totals and every agency.
--
-- Idempotent like every script: DbMigrator runs all of them on every start. Once released, never
-- edit this file; add a new numbered script instead.
--
-- Design:
--   * Site admins live in their own table, not in a column of users. app_user can only READ it, so
--     no bug or injection in the API can promote anybody. Rows are written by the database owner
--     (the startup bootstrap), never by the application role.
--   * Cross-agency reads go through narrow SECURITY DEFINER functions that check, inside the
--     database, that the caller is a site admin. Row-level security stays enabled and forced.
--   * A site admin signs in without an agency, so their refresh tokens cannot use refresh_tokens
--     (agency_id NOT NULL); they get a small table of their own.

-- ---------------------------------------------------------------------------------------------
-- site_admins
-- ---------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS site_admins (
    user_id     uuid        PRIMARY KEY REFERENCES users (id) ON DELETE CASCADE,
    created_at  timestamptz NOT NULL DEFAULT now()
);

-- Default privileges gave app_user full DML on this new table; take it back to read-only.
REVOKE ALL ON site_admins FROM app_user;
GRANT SELECT ON site_admins TO app_user;

ALTER TABLE site_admins ENABLE ROW LEVEL SECURITY;
ALTER TABLE site_admins FORCE ROW LEVEL SECURITY;

-- A user can see whether they themselves are a site admin, and nothing else about the table.
DROP POLICY IF EXISTS own_select ON site_admins;
CREATE POLICY own_select ON site_admins FOR SELECT
    USING (user_id = app_user_id());

-- ---------------------------------------------------------------------------------------------
-- site_admin_refresh_tokens
-- ---------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS site_admin_refresh_tokens (
    id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id         uuid        NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    token_hash      text        NOT NULL,
    expires_at      timestamptz NOT NULL,
    revoked_at      timestamptz NULL,
    replaced_by_id  uuid        NULL REFERENCES site_admin_refresh_tokens (id) ON DELETE SET NULL,
    created_at      timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_site_admin_refresh_tokens_hash ON site_admin_refresh_tokens (token_hash);
CREATE INDEX IF NOT EXISTS ix_site_admin_refresh_tokens_user ON site_admin_refresh_tokens (user_id);
CREATE INDEX IF NOT EXISTS ix_site_admin_refresh_tokens_replaced_by
    ON site_admin_refresh_tokens (replaced_by_id) WHERE replaced_by_id IS NOT NULL;

ALTER TABLE site_admin_refresh_tokens ENABLE ROW LEVEL SECURITY;
ALTER TABLE site_admin_refresh_tokens FORCE ROW LEVEL SECURITY;

-- One policy per command, because only INSERT may require the user to be a site admin right now.
-- A user who was demoted must still be able to read and revoke the tokens they already have; a
-- single policy with a WITH CHECK would also apply that check to the UPDATE that revokes them.
-- A token is found by its hash through auth_find_site_refresh_token() below.
DROP POLICY IF EXISTS own_select ON site_admin_refresh_tokens;
CREATE POLICY own_select ON site_admin_refresh_tokens FOR SELECT
    USING (user_id = app_user_id());

DROP POLICY IF EXISTS own_insert ON site_admin_refresh_tokens;
CREATE POLICY own_insert ON site_admin_refresh_tokens FOR INSERT
    WITH CHECK (
        user_id = app_user_id()
        AND EXISTS (SELECT 1 FROM site_admins s WHERE s.user_id = app_user_id()));

DROP POLICY IF EXISTS own_update ON site_admin_refresh_tokens;
CREATE POLICY own_update ON site_admin_refresh_tokens FOR UPDATE
    USING (user_id = app_user_id())
    WITH CHECK (user_id = app_user_id());

DROP POLICY IF EXISTS own_delete ON site_admin_refresh_tokens;
CREATE POLICY own_delete ON site_admin_refresh_tokens FOR DELETE
    USING (user_id = app_user_id());

-- ---------------------------------------------------------------------------------------------
-- Owner access. FORCE ROW LEVEL SECURITY also binds the table owner, and the SECURITY DEFINER
-- functions below run as the owner. A superuser skips the policies anyway; for any other owner
-- these let the functions and the startup bootstrap do their narrow jobs.
-- ---------------------------------------------------------------------------------------------
DO $$
DECLARE
    tbl text;
BEGIN
    FOREACH tbl IN ARRAY ARRAY['agencies', 'agency_members', 'workspaces', 'site_admin_refresh_tokens'] LOOP
        EXECUTE format('DROP POLICY IF EXISTS owner_lookup ON %I', tbl);
        EXECUTE format('CREATE POLICY owner_lookup ON %I FOR SELECT TO %I USING (true)', tbl, current_user);
    END LOOP;

    EXECUTE 'DROP POLICY IF EXISTS owner_manage ON site_admins';
    EXECUTE format('CREATE POLICY owner_manage ON site_admins FOR ALL TO %I USING (true) WITH CHECK (true)', current_user);
END
$$;

-- ---------------------------------------------------------------------------------------------
-- Lookups and reports. Each takes the caller from app_user_id(), which the API sets from the
-- access token, and refuses anybody who is not in site_admins.
-- ---------------------------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION auth_find_site_refresh_token(p_token_hash text)
    RETURNS TABLE (id uuid, user_id uuid, expires_at timestamptz, revoked_at timestamptz, replaced_by_id uuid)
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
    SELECT t.id, t.user_id, t.expires_at, t.revoked_at, t.replaced_by_id
    FROM site_admin_refresh_tokens t
    WHERE t.token_hash = p_token_hash
$$;

CREATE OR REPLACE FUNCTION site_admin_overview()
    RETURNS TABLE (
        agency_count bigint,
        trial_agency_count bigint,
        paid_agency_count bigint,
        user_count bigint,
        workspace_count bigint,
        member_count bigint)
    LANGUAGE plpgsql STABLE SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM site_admins s WHERE s.user_id = app_user_id()) THEN
        RAISE EXCEPTION 'Site admin access is required.' USING ERRCODE = '42501';
    END IF;

    RETURN QUERY SELECT
        (SELECT count(*) FROM agencies),
        (SELECT count(*) FROM agencies a WHERE a.plan = 'trial'),
        (SELECT count(*) FROM agencies a WHERE a.plan = 'paid'),
        (SELECT count(*) FROM users),
        (SELECT count(*) FROM workspaces),
        (SELECT count(*) FROM agency_members);
END
$$;

CREATE OR REPLACE FUNCTION site_admin_list_agencies(p_limit integer, p_offset integer)
    RETURNS TABLE (
        id uuid,
        name text,
        plan text,
        created_at timestamptz,
        member_count bigint,
        workspace_count bigint,
        total_count bigint)
    LANGUAGE plpgsql STABLE SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM site_admins s WHERE s.user_id = app_user_id()) THEN
        RAISE EXCEPTION 'Site admin access is required.' USING ERRCODE = '42501';
    END IF;

    IF p_limit IS NULL OR p_limit NOT BETWEEN 1 AND 100 OR p_offset IS NULL OR p_offset < 0 THEN
        RAISE EXCEPTION 'Invalid paging arguments.' USING ERRCODE = '22023';
    END IF;

    RETURN QUERY
    SELECT a.id,
           a.name,
           a.plan,
           a.created_at,
           (SELECT count(*) FROM agency_members m WHERE m.agency_id = a.id),
           (SELECT count(*) FROM workspaces w WHERE w.agency_id = a.id),
           count(*) OVER ()
    FROM agencies a
    ORDER BY a.created_at DESC, a.id
    LIMIT p_limit OFFSET p_offset;
END
$$;

REVOKE ALL ON FUNCTION auth_find_site_refresh_token(text) FROM PUBLIC;
REVOKE ALL ON FUNCTION site_admin_overview() FROM PUBLIC;
REVOKE ALL ON FUNCTION site_admin_list_agencies(integer, integer) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION auth_find_site_refresh_token(text) TO app_user;
GRANT EXECUTE ON FUNCTION site_admin_overview() TO app_user;
GRANT EXECUTE ON FUNCTION site_admin_list_agencies(integer, integer) TO app_user;
