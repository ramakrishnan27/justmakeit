-- 003_business_models.sql
-- Three business models (company, agency, creator) and four member roles
-- (organizationadmin, clientmanager, marketingmanager, contentwriter). An organization is still
-- called an agency in the tables; the agency business model is one kind of organization.
--
-- Idempotent like every script: DbMigrator runs all of them on every start. Once released, never
-- edit this file; add a new numbered script instead.
--
-- Because 001 and 002 also run on every start, nothing they create may change shape here:
--   * 001 skips CREATE TABLE agency_members once it exists, so it never puts the old role check back.
--   * 002 re-creates site_admin_list_agencies() with CREATE OR REPLACE, so that function must keep its
--     return type. The list with the business model is a new function, site_admin_list_agencies_v2().
--
-- Design:
--   * agencies.business_model is chosen at registration and cannot change while the organization has
--     members: agency_members repeats it and references (id, business_model) of the agency.
--   * One CHECK over (business_model, role) is the table of allowed pairs. BusinessModelRules in
--     Platform.Auth has the same table, and an integration test compares them for every pair.
--   * An individual creator is a one-member organization (partial unique index).
--   * No column default survives, so an INSERT that forgets the business model fails instead of guessing.

-- ---------------------------------------------------------------------------------------------
-- agencies.business_model
-- ---------------------------------------------------------------------------------------------
-- Organizations that exist already were all created as agencies, so they take that value.
ALTER TABLE agencies ADD COLUMN IF NOT EXISTS business_model text NOT NULL DEFAULT 'agency';
ALTER TABLE agencies ALTER COLUMN business_model DROP DEFAULT;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'agencies'::regclass AND conname = 'ck_agencies_business_model') THEN
        ALTER TABLE agencies ADD CONSTRAINT ck_agencies_business_model
            CHECK (business_model IN ('company', 'agency', 'creator'));
    END IF;

    -- The target of the composite foreign key below.
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'agencies'::regclass AND conname = 'uq_agencies_id_business_model') THEN
        ALTER TABLE agencies ADD CONSTRAINT uq_agencies_id_business_model UNIQUE (id, business_model);
    END IF;
END
$$;

-- ---------------------------------------------------------------------------------------------
-- agency_members: business_model, the roles and the pairs that are allowed
-- ---------------------------------------------------------------------------------------------
ALTER TABLE agency_members ADD COLUMN IF NOT EXISTS business_model text NOT NULL DEFAULT 'agency';
ALTER TABLE agency_members ALTER COLUMN business_model DROP DEFAULT;

-- The role list of 001 (owner, admin, manager, viewer) goes away. Dropped first, so the rows can be
-- rewritten; the new check is added once nothing holds an old name.
ALTER TABLE agency_members DROP CONSTRAINT IF EXISTS agency_members_role_check;

-- Existing members move to the nearest new role: owner and admin to organizationadmin, manager to
-- marketingmanager, viewer to contentwriter (the least-privileged role that is left).
--
-- FORCE ROW LEVEL SECURITY also binds the table owner, so for an owner that is not a superuser this
-- UPDATE would silently change nothing. A policy for the migrating role exists only while the UPDATE
-- runs, in the same transaction: row-level security stays enabled and forced, and nothing is left.
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM agency_members WHERE role IN ('owner', 'admin', 'manager', 'viewer')) THEN
        EXECUTE 'DROP POLICY IF EXISTS owner_migrate ON agency_members';
        EXECUTE format('CREATE POLICY owner_migrate ON agency_members FOR UPDATE TO %I USING (true) WITH CHECK (true)', current_user);

        UPDATE agency_members
        SET role = CASE role
                       WHEN 'owner' THEN 'organizationadmin'
                       WHEN 'admin' THEN 'organizationadmin'
                       WHEN 'manager' THEN 'marketingmanager'
                       WHEN 'viewer' THEN 'contentwriter'
                   END
        WHERE role IN ('owner', 'admin', 'manager', 'viewer');

        EXECUTE 'DROP POLICY owner_migrate ON agency_members';
    END IF;
END
$$;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'agency_members'::regclass AND conname = 'ck_agency_members_role_model') THEN
        ALTER TABLE agency_members ADD CONSTRAINT ck_agency_members_role_model CHECK (
            (business_model = 'company'
                AND role IN ('organizationadmin', 'marketingmanager', 'contentwriter'))
            OR (business_model = 'agency'
                AND role IN ('organizationadmin', 'clientmanager', 'marketingmanager', 'contentwriter'))
            OR (business_model = 'creator'
                AND role = 'contentwriter'));
    END IF;

    -- A member's model must be the model of their organization. This also freezes the organization's
    -- own value while it has members (ON UPDATE stays NO ACTION).
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'agency_members'::regclass AND conname = 'fk_agency_members_business_model') THEN
        ALTER TABLE agency_members ADD CONSTRAINT fk_agency_members_business_model
            FOREIGN KEY (agency_id, business_model) REFERENCES agencies (id, business_model) ON DELETE CASCADE;
    END IF;
END
$$;

CREATE INDEX IF NOT EXISTS ix_agency_members_agency_model ON agency_members (agency_id, business_model);

-- An individual creator is a one-member organization.
CREATE UNIQUE INDEX IF NOT EXISTS ux_agency_members_creator_single
    ON agency_members (agency_id) WHERE business_model = 'creator';

-- ---------------------------------------------------------------------------------------------
-- Site admin list with the business model. site_admin_list_agencies() from 002 stays as it is.
-- ---------------------------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION site_admin_list_agencies_v2(p_limit integer, p_offset integer)
    RETURNS TABLE (
        id uuid,
        name text,
        business_model text,
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
           a.business_model,
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

REVOKE ALL ON FUNCTION site_admin_list_agencies_v2(integer, integer) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION site_admin_list_agencies_v2(integer, integer) TO app_user;
