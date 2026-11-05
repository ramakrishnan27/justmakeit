-- 008_team_admin.sql
-- The team page grows three things: a person can be deactivated and activated again, an invited person can be
-- activated by the admin straight away (the admin types a default password instead of sending a link), and an
-- invitation carries the name of the person it is for.
--
-- Idempotent like every script: DbMigrator runs all of them on every start. Once released, never edit this file;
-- add a new numbered script instead.
--
-- Design:
--   * agency_members.is_active (an existing table, approved): a deactivated member keeps their row, their role and
--     their data, but cannot sign in, and their sessions end the moment they are deactivated (team_end_sessions, as for
--     a role change). Existing members stay active (DEFAULT true).
--   * member_invitations.full_name: the name the admin typed. Older invitations have none (NULL); the list then shows
--     the address, and an activation falls back to the part of the address before the @.
--   * 007 re-creates its functions on every start, so none of them can change here (a later script may never change what
--     an earlier one re-creates). The changes are new functions instead: team_members_v2, team_change_role_v2,
--     team_remove_member_v2, invitation_find_v2, team_set_member_active and team_activate_invitation. The _v2 change
--     and remove functions add the rule "the organization keeps one ACTIVE organization admin" and then call the 007
--     function, which keeps its own checks; so the 007 functions are no longer callable by app_user (the API reaches
--     them only through the new ones).
--   * Every function here is SECURITY DEFINER and checks inside the database that the caller holds members.manage
--     in the organization the connection acts for AND is an active member of it (org_caller_manages). The
--     per-organization advisory lock of 007 is taken first, then the permission is judged, so a caller deactivated
--     or removed a moment ago is refused.
--   * Rule codes raised as P0001 with the code as the message: the 007 ones, plus invitation_not_found. A password hash
--     is required to activate (22023 otherwise); the API hashes the password, the database never sees it in the clear.

-- ---------------------------------------------------------------------------------------------
-- Columns
-- ---------------------------------------------------------------------------------------------
ALTER TABLE agency_members ADD COLUMN IF NOT EXISTS is_active boolean NOT NULL DEFAULT true;

ALTER TABLE member_invitations ADD COLUMN IF NOT EXISTS full_name text NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint
                   WHERE conrelid = 'member_invitations'::regclass AND conname = 'ck_member_invitations_name') THEN
        ALTER TABLE member_invitations ADD CONSTRAINT ck_member_invitations_name
            CHECK (full_name IS NULL OR length(btrim(full_name)) BETWEEN 1 AND 200);
    END IF;
END
$$;

-- ---------------------------------------------------------------------------------------------
-- Internal helper. Not callable by app_user.
-- ---------------------------------------------------------------------------------------------
-- True when the connection's user holds members.manage in the connection's organization and is an ACTIVE member of it.
CREATE OR REPLACE FUNCTION org_caller_manages()
    RETURNS boolean
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
    SELECT org_caller_holds('members.manage')
       AND EXISTS (
           SELECT 1 FROM agency_members m
           WHERE m.agency_id = app_agency_id() AND m.user_id = app_user_id() AND m.is_active)
$$;

REVOKE ALL ON FUNCTION org_caller_manages() FROM PUBLIC;

-- ---------------------------------------------------------------------------------------------
-- The team, with whether each person is active
-- ---------------------------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION team_members_v2()
    RETURNS TABLE (user_id uuid, email text, full_name text, role text, joined_at timestamptz, is_active boolean)
    LANGUAGE plpgsql STABLE SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
BEGIN
    IF NOT org_caller_manages() THEN
        RAISE EXCEPTION 'Managing the team requires the members.manage permission.' USING ERRCODE = '42501';
    END IF;

    RETURN QUERY
    SELECT m.user_id, u.email, u.full_name, m.role, m.created_at, m.is_active
    FROM agency_members m
    JOIN users u ON u.id = m.user_id
    WHERE m.agency_id = app_agency_id()
    ORDER BY m.created_at, u.email;
END
$$;

-- ---------------------------------------------------------------------------------------------
-- Changing a role and removing a person: the 007 rules, and the organization keeps one ACTIVE organization admin
-- ---------------------------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION team_change_role_v2(p_user_id uuid, p_role text)
    RETURNS void
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
DECLARE
    v_agency   uuid := app_agency_id();
    v_old_role text;
    v_active   boolean;
BEGIN
    PERFORM pg_advisory_xact_lock(hashtextextended('team:' || coalesce(v_agency::text, ''), 0));

    IF NOT org_caller_manages() THEN
        RAISE EXCEPTION 'Managing the team requires the members.manage permission.' USING ERRCODE = '42501';
    END IF;

    IF p_user_id = app_user_id() THEN
        RAISE EXCEPTION 'cannot_change_self' USING ERRCODE = 'P0001';
    END IF;

    SELECT m.role, m.is_active INTO v_old_role, v_active
    FROM agency_members m
    WHERE m.agency_id = v_agency AND m.user_id = p_user_id;

    -- Demoting the last ACTIVE admin would leave nobody who can run the organization. (A missing member is
    -- reported by the 007 function below.)
    IF FOUND AND v_old_role = 'organizationadmin' AND v_active AND p_role <> 'organizationadmin'
       AND NOT EXISTS (
           SELECT 1 FROM agency_members m
           WHERE m.agency_id = v_agency AND m.role = 'organizationadmin' AND m.is_active AND m.user_id <> p_user_id) THEN
        RAISE EXCEPTION 'last_organization_admin' USING ERRCODE = 'P0001';
    END IF;

    PERFORM team_change_role(p_user_id, p_role);
END
$$;

CREATE OR REPLACE FUNCTION team_remove_member_v2(p_user_id uuid)
    RETURNS void
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
DECLARE
    v_agency uuid := app_agency_id();
    v_role   text;
    v_active boolean;
BEGIN
    PERFORM pg_advisory_xact_lock(hashtextextended('team:' || coalesce(v_agency::text, ''), 0));

    IF NOT org_caller_manages() THEN
        RAISE EXCEPTION 'Managing the team requires the members.manage permission.' USING ERRCODE = '42501';
    END IF;

    IF p_user_id = app_user_id() THEN
        RAISE EXCEPTION 'cannot_change_self' USING ERRCODE = 'P0001';
    END IF;

    SELECT m.role, m.is_active INTO v_role, v_active
    FROM agency_members m
    WHERE m.agency_id = v_agency AND m.user_id = p_user_id;

    IF FOUND AND v_role = 'organizationadmin' AND v_active
       AND NOT EXISTS (
           SELECT 1 FROM agency_members m
           WHERE m.agency_id = v_agency AND m.role = 'organizationadmin' AND m.is_active AND m.user_id <> p_user_id) THEN
        RAISE EXCEPTION 'last_organization_admin' USING ERRCODE = 'P0001';
    END IF;

    PERFORM team_remove_member(p_user_id);
END
$$;

-- ---------------------------------------------------------------------------------------------
-- Deactivating and activating a member
-- ---------------------------------------------------------------------------------------------
-- A deactivated member cannot sign in and their sessions end at once; activating them again lets them sign in
-- (with the password they already have). Nothing else about them changes. Asking for the state they are already in
-- changes nothing.
CREATE OR REPLACE FUNCTION team_set_member_active(p_user_id uuid, p_active boolean)
    RETURNS void
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
DECLARE
    v_agency uuid := app_agency_id();
    v_role   text;
    v_was    boolean;
BEGIN
    PERFORM pg_advisory_xact_lock(hashtextextended('team:' || coalesce(v_agency::text, ''), 0));

    IF NOT org_caller_manages() THEN
        RAISE EXCEPTION 'Managing the team requires the members.manage permission.' USING ERRCODE = '42501';
    END IF;

    IF p_active IS NULL THEN
        RAISE EXCEPTION 'The new state is required.' USING ERRCODE = '22023';
    END IF;

    IF p_user_id = app_user_id() THEN
        RAISE EXCEPTION 'cannot_change_self' USING ERRCODE = 'P0001';
    END IF;

    SELECT m.role, m.is_active INTO v_role, v_was
    FROM agency_members m
    WHERE m.agency_id = v_agency AND m.user_id = p_user_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'member_not_found' USING ERRCODE = 'P0001';
    END IF;

    IF v_was = p_active THEN
        RETURN;
    END IF;

    IF NOT p_active AND v_role = 'organizationadmin'
       AND NOT EXISTS (
           SELECT 1 FROM agency_members m
           WHERE m.agency_id = v_agency AND m.role = 'organizationadmin' AND m.is_active AND m.user_id <> p_user_id) THEN
        RAISE EXCEPTION 'last_organization_admin' USING ERRCODE = 'P0001';
    END IF;

    UPDATE agency_members
    SET is_active = p_active
    WHERE agency_id = v_agency AND user_id = p_user_id;

    IF NOT p_active THEN
        PERFORM team_end_sessions(v_agency, p_user_id);
    END IF;
END
$$;

-- ---------------------------------------------------------------------------------------------
-- Activating an invited person by hand: the admin has typed a default password
-- ---------------------------------------------------------------------------------------------
-- Does in one go what accepting the invitation does, for the person the admin names: creates the user (with the
-- password hash the API made, and the name on the invitation), their membership, and marks the invitation used, so its
-- link stops working. An invitation whose link has expired can still be activated: the admin is the one acting. A used,
-- revoked or other organization's invitation is invitation_not_found; an address that has an account meanwhile is
-- email_taken.
CREATE OR REPLACE FUNCTION team_activate_invitation(p_invitation_id uuid, p_user_id uuid, p_password_hash text)
    RETURNS void
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
DECLARE
    v_agency uuid := app_agency_id();
    v_inv    member_invitations%ROWTYPE;
BEGIN
    PERFORM pg_advisory_xact_lock(hashtextextended('team:' || coalesce(v_agency::text, ''), 0));

    IF NOT org_caller_manages() THEN
        RAISE EXCEPTION 'Managing the team requires the members.manage permission.' USING ERRCODE = '42501';
    END IF;

    IF p_password_hash IS NULL OR p_password_hash = '' THEN
        RAISE EXCEPTION 'A password is required.' USING ERRCODE = '22023';
    END IF;

    SELECT * INTO v_inv
    FROM member_invitations i
    WHERE i.id = p_invitation_id AND i.agency_id = v_agency AND i.accepted_at IS NULL AND i.revoked_at IS NULL
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'invitation_not_found' USING ERRCODE = 'P0001';
    END IF;

    IF EXISTS (SELECT 1 FROM users u WHERE lower(u.email) = lower(v_inv.email)) THEN
        RAISE EXCEPTION 'email_taken' USING ERRCODE = 'P0001';
    END IF;

    INSERT INTO users (id, email, password_hash, full_name)
    VALUES (p_user_id, v_inv.email, p_password_hash,
            coalesce(nullif(btrim(v_inv.full_name), ''), split_part(v_inv.email, '@', 1)));

    INSERT INTO agency_members (agency_id, user_id, business_model, role, last_active_at)
    VALUES (v_agency, p_user_id, v_inv.business_model, v_inv.role, now());

    UPDATE member_invitations SET accepted_at = now() WHERE id = v_inv.id;
END
$$;

-- ---------------------------------------------------------------------------------------------
-- The invitation page also knows the name the admin typed, so it can offer it
-- ---------------------------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION invitation_find_v2(p_token_hash text)
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
$$;

-- ---------------------------------------------------------------------------------------------
-- Who may call what. The 007 team functions are reached only through the new ones from now on.
-- ---------------------------------------------------------------------------------------------
REVOKE ALL ON FUNCTION team_members_v2() FROM PUBLIC;
REVOKE ALL ON FUNCTION team_change_role_v2(uuid, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION team_remove_member_v2(uuid) FROM PUBLIC;
REVOKE ALL ON FUNCTION team_set_member_active(uuid, boolean) FROM PUBLIC;
REVOKE ALL ON FUNCTION team_activate_invitation(uuid, uuid, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION invitation_find_v2(text) FROM PUBLIC;

GRANT EXECUTE ON FUNCTION team_members_v2() TO app_user;
GRANT EXECUTE ON FUNCTION team_change_role_v2(uuid, text) TO app_user;
GRANT EXECUTE ON FUNCTION team_remove_member_v2(uuid) TO app_user;
GRANT EXECUTE ON FUNCTION team_set_member_active(uuid, boolean) TO app_user;
GRANT EXECUTE ON FUNCTION team_activate_invitation(uuid, uuid, text) TO app_user;
GRANT EXECUTE ON FUNCTION invitation_find_v2(text) TO app_user;

-- 007 grants these to app_user on every start; this script runs after it and takes the grant back.
REVOKE EXECUTE ON FUNCTION team_list_members() FROM app_user;
REVOKE EXECUTE ON FUNCTION team_change_role(uuid, text) FROM app_user;
REVOKE EXECUTE ON FUNCTION team_remove_member(uuid) FROM app_user;
