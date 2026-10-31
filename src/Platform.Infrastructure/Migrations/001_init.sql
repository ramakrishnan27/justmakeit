-- 001_init.sql
-- Core multi-tenant schema: agencies, users, memberships, workspaces, refresh tokens, the app_user
-- role, the tenant helper functions and row-level security.
--
-- Idempotent: DbMigrator runs every script on every start. Once released, never edit this file;
-- add a new numbered script instead.
--
-- Connection model: the API connects as app_user (no superuser, no BYPASSRLS). On every connection
-- it sets app.agency_id and app.user_id with set_config(); the policies below read them through
-- app_agency_id() and app_user_id(). Unset or empty values give NULL, so access fails closed.

-- ---------------------------------------------------------------------------------------------
-- Runtime role. The migrator sets its password from ConnectionStrings:App after the scripts ran.
-- Attributes are not altered here: a role that already exists keeps its own, and the check at the
-- end of this script fails the migration if it is a superuser or can bypass RLS.
-- ---------------------------------------------------------------------------------------------
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_user') THEN
        CREATE ROLE app_user LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
    END IF;
END
$$;

DO $$
BEGIN
    EXECUTE format('GRANT CONNECT ON DATABASE %I TO app_user', current_database());
END
$$;

GRANT USAGE ON SCHEMA public TO app_user;

-- Tables created by later scripts (run by the same owner) are usable by app_user straight away;
-- row-level security still decides which rows it sees.
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO app_user;

-- ---------------------------------------------------------------------------------------------
-- Tenant helper functions
-- ---------------------------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION app_agency_id() RETURNS uuid
    LANGUAGE sql STABLE PARALLEL SAFE
AS $$ SELECT NULLIF(current_setting('app.agency_id', true), '')::uuid $$;

CREATE OR REPLACE FUNCTION app_user_id() RETURNS uuid
    LANGUAGE sql STABLE PARALLEL SAFE
AS $$ SELECT NULLIF(current_setting('app.user_id', true), '')::uuid $$;

GRANT EXECUTE ON FUNCTION app_agency_id() TO app_user;
GRANT EXECUTE ON FUNCTION app_user_id() TO app_user;

-- ---------------------------------------------------------------------------------------------
-- Tables
-- ---------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS agencies (
    id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    name        text        NOT NULL CHECK (length(btrim(name)) BETWEEN 1 AND 200),
    plan        text        NOT NULL DEFAULT 'trial' CHECK (plan IN ('trial', 'paid')),
    created_at  timestamptz NOT NULL DEFAULT now()
);

-- Users are global: one person can belong to several agencies.
CREATE TABLE IF NOT EXISTS users (
    id             uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    email          text        NOT NULL CHECK (length(email) BETWEEN 3 AND 254),
    password_hash  text        NOT NULL,
    full_name      text        NOT NULL DEFAULT '' CHECK (length(full_name) <= 200),
    created_at     timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_users_email ON users (lower(email));

CREATE TABLE IF NOT EXISTS agency_members (
    id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    agency_id       uuid        NOT NULL REFERENCES agencies (id) ON DELETE CASCADE,
    user_id         uuid        NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    role            text        NOT NULL CHECK (role IN ('owner', 'admin', 'manager', 'viewer')),
    last_active_at  timestamptz NOT NULL DEFAULT now(),
    created_at      timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_agency_members_agency_user UNIQUE (agency_id, user_id)
);

-- Newest membership first: login picks the most recently used agency of the user.
CREATE INDEX IF NOT EXISTS ix_agency_members_user ON agency_members (user_id, last_active_at DESC);

CREATE TABLE IF NOT EXISTS workspaces (
    id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    agency_id   uuid        NOT NULL REFERENCES agencies (id) ON DELETE CASCADE,
    name        text        NOT NULL CHECK (length(btrim(name)) BETWEEN 1 AND 200),
    created_at  timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_workspaces_agency ON workspaces (agency_id, created_at);

CREATE TABLE IF NOT EXISTS refresh_tokens (
    id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    agency_id       uuid        NOT NULL REFERENCES agencies (id) ON DELETE CASCADE,
    user_id         uuid        NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    token_hash      text        NOT NULL,
    expires_at      timestamptz NOT NULL,
    revoked_at      timestamptz NULL,
    replaced_by_id  uuid        NULL REFERENCES refresh_tokens (id) ON DELETE SET NULL,
    created_at      timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_refresh_tokens_hash ON refresh_tokens (token_hash);
CREATE INDEX IF NOT EXISTS ix_refresh_tokens_user_agency ON refresh_tokens (user_id, agency_id);
CREATE INDEX IF NOT EXISTS ix_refresh_tokens_agency ON refresh_tokens (agency_id);
CREATE INDEX IF NOT EXISTS ix_refresh_tokens_replaced_by ON refresh_tokens (replaced_by_id) WHERE replaced_by_id IS NOT NULL;

GRANT SELECT, INSERT, UPDATE, DELETE ON agencies, users, agency_members, workspaces, refresh_tokens TO app_user;

-- ---------------------------------------------------------------------------------------------
-- Row-level security: enabled and forced, so the table owner is subject to the policies as well.
-- ---------------------------------------------------------------------------------------------
ALTER TABLE agencies       ENABLE ROW LEVEL SECURITY;
ALTER TABLE agencies       FORCE ROW LEVEL SECURITY;
ALTER TABLE users          ENABLE ROW LEVEL SECURITY;
ALTER TABLE users          FORCE ROW LEVEL SECURITY;
ALTER TABLE agency_members ENABLE ROW LEVEL SECURITY;
ALTER TABLE agency_members FORCE ROW LEVEL SECURITY;
ALTER TABLE workspaces     ENABLE ROW LEVEL SECURITY;
ALTER TABLE workspaces     FORCE ROW LEVEL SECURITY;
ALTER TABLE refresh_tokens ENABLE ROW LEVEL SECURITY;
ALTER TABLE refresh_tokens FORCE ROW LEVEL SECURITY;

-- agencies: the current agency. A user may also see every agency they belong to (agency switcher).
-- Registration inserts the new agency with app.agency_id already set to its id.
DROP POLICY IF EXISTS tenant_isolation ON agencies;
CREATE POLICY tenant_isolation ON agencies
    USING (id = app_agency_id())
    WITH CHECK (id = app_agency_id());

DROP POLICY IF EXISTS member_select ON agencies;
CREATE POLICY member_select ON agencies FOR SELECT
    USING (EXISTS (
        SELECT 1 FROM agency_members m
        WHERE m.agency_id = agencies.id AND m.user_id = app_user_id()));

-- users: a user sees and changes only their own row. Login finds a user by email through
-- auth_find_user_by_email() below, because no user is known yet at that point.
DROP POLICY IF EXISTS self_access ON users;
CREATE POLICY self_access ON users
    USING (id = app_user_id())
    WITH CHECK (id = app_user_id());

-- agency_members: write access is limited to the current agency; reads also cover every
-- membership of the calling user, in any agency (GET /v1/auth/me and switch-agency).
DROP POLICY IF EXISTS tenant_isolation ON agency_members;
CREATE POLICY tenant_isolation ON agency_members
    USING (agency_id = app_agency_id())
    WITH CHECK (agency_id = app_agency_id());

DROP POLICY IF EXISTS own_select ON agency_members;
CREATE POLICY own_select ON agency_members FOR SELECT
    USING (user_id = app_user_id());

DROP POLICY IF EXISTS tenant_isolation ON workspaces;
CREATE POLICY tenant_isolation ON workspaces
    USING (agency_id = app_agency_id())
    WITH CHECK (agency_id = app_agency_id());

-- refresh_tokens: only the owning user, inside the agency the token was issued for. The token is
-- found by its hash through auth_find_refresh_token() below, which tells the caller whose context to use.
DROP POLICY IF EXISTS tenant_isolation ON refresh_tokens;
CREATE POLICY tenant_isolation ON refresh_tokens
    USING (agency_id = app_agency_id() AND user_id = app_user_id())
    WITH CHECK (agency_id = app_agency_id() AND user_id = app_user_id());

-- ---------------------------------------------------------------------------------------------
-- Lookups that must work before the tenant is known. SECURITY DEFINER functions run as their
-- owner (the migration role), never as app_user. Each returns only what its caller needs and
-- matches on one exact key, so app_user cannot enumerate rows through them.
--
-- FORCE ROW LEVEL SECURITY also binds the owner. A superuser or BYPASSRLS owner skips the policies
-- anyway; for any other owner the owner_lookup policies below let these functions read.
-- ---------------------------------------------------------------------------------------------
DO $$
DECLARE
    tbl text;
BEGIN
    FOREACH tbl IN ARRAY ARRAY['users', 'refresh_tokens'] LOOP
        EXECUTE format('DROP POLICY IF EXISTS owner_lookup ON %I', tbl);
        EXECUTE format('CREATE POLICY owner_lookup ON %I FOR SELECT TO %I USING (true)', tbl, current_user);
    END LOOP;
END
$$;

CREATE OR REPLACE FUNCTION auth_find_user_by_email(p_email text)
    RETURNS TABLE (id uuid, email text, password_hash text, full_name text)
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
    SELECT u.id, u.email, u.password_hash, u.full_name
    FROM users u
    WHERE lower(u.email) = lower(p_email)
$$;

CREATE OR REPLACE FUNCTION auth_find_refresh_token(p_token_hash text)
    RETURNS TABLE (id uuid, user_id uuid, agency_id uuid, expires_at timestamptz, revoked_at timestamptz, replaced_by_id uuid)
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
    SELECT t.id, t.user_id, t.agency_id, t.expires_at, t.revoked_at, t.replaced_by_id
    FROM refresh_tokens t
    WHERE t.token_hash = p_token_hash
$$;

REVOKE ALL ON FUNCTION auth_find_user_by_email(text) FROM PUBLIC;
REVOKE ALL ON FUNCTION auth_find_refresh_token(text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION auth_find_user_by_email(text) TO app_user;
GRANT EXECUTE ON FUNCTION auth_find_refresh_token(text) TO app_user;

-- ---------------------------------------------------------------------------------------------
-- Fail closed: the runtime role must never be able to skip row-level security.
-- ---------------------------------------------------------------------------------------------
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_user' AND (rolsuper OR rolbypassrls)) THEN
        RAISE EXCEPTION 'Role app_user must not be a superuser and must not have BYPASSRLS.';
    END IF;
END
$$;
