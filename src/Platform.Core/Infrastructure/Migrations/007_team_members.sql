-- 007_team_members.sql
-- A team for company and agency organizations: the organization admin invites employees, changes their
-- roles and removes them. An invited person opens a one-time link, sets their own password (or, later, uses
-- Google) and is signed in as a member. A creator organization is one person and cannot invite.
--
-- Idempotent like every script: DbMigrator runs all of them on every start. Once released, never
-- edit this file; add a new numbered script instead.
--
-- Design:
--   * The permission members.manage is held, by default, by the organization admin of a company and of an
--     agency. The Site admin decides who else holds it (role_permissions). Whoever holds it can also make
--     organization admins, and the permission's description says so. Adding the grants raises the permission
--     version once, so everybody signs in again once.
--   * member_invitations is a tenant table. The invitation token is random and only its SHA-256 hash is stored.
--     At most one invitation per organization and email is pending at a time.
--   * The people of a team live in users, which a user sees only for themselves, and the person who accepts
--     an invitation is not signed in yet. So everything that crosses that line is a narrow SECURITY DEFINER
--     function: the team list, a role change, a removal, finding an invitation by its token hash, and accepting
--     it (all one statement-level unit of work, so a half-accepted invitation cannot exist). The functions that
--     change the team check inside the database that the caller holds members.manage in the organization the
--     connection is acting for.
--   * Ending a session. An access token lasts up to 90 days and nothing else refers to it, so a removed or
--     re-roled member would keep their access. session_revocations says, per organization and user, that tokens
--     issued up to that moment are void. The API holds the rows in memory (session_revocations_snapshot()) and
--     refuses such a token with 401. user_id has NO foreign key on purpose: removing the last membership of a
--     person deletes their account, and the revocation must outlive it or the old token would work again.
--   * Rules that the functions raise as P0001 with the code as the message: member_not_found,
--     last_organization_admin, cannot_change_self, invitation_invalid, email_taken. Everything else is a
--     constraint or 42501 (not allowed).

-- ---------------------------------------------------------------------------------------------
-- The permission, its default grants and the Team page
-- ---------------------------------------------------------------------------------------------
INSERT INTO permissions (key, title, description, is_protected) VALUES
    ('members.manage', 'Manage the team',
        'Invite people into the organization, change their roles and remove them. Whoever holds this can also make organization admins.', false)
ON CONFLICT (key) DO NOTHING;

-- Eight (model, role) slots. Only the organization admin of a company and of an agency holds it by default.
INSERT INTO role_permissions (business_model, role, permission_key, is_granted)
SELECT s.business_model, s.role, 'members.manage', s.role = 'organizationadmin'
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
ON CONFLICT (business_model, role, permission_key) DO NOTHING;

INSERT INTO menu_pages
    (app, key, path, title, permission_key, default_label, default_icon, default_sort_order, default_is_locked)
VALUES
    ('member', 'team', '/team', 'Team', 'members.manage', 'Team', 'users', 30, false)
ON CONFLICT (app, key) DO NOTHING;

SELECT menu_apply_default_items();

-- ---------------------------------------------------------------------------------------------
-- member_invitations
-- ---------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS member_invitations (
    id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    agency_id       uuid        NOT NULL REFERENCES agencies (id) ON DELETE CASCADE,
    business_model  text        NOT NULL,
    email           text        NOT NULL CHECK (length(email) BETWEEN 3 AND 254),
    role            text        NOT NULL,
    token_hash      text        NOT NULL,
    invited_by      uuid        NULL REFERENCES users (id) ON DELETE SET NULL,
    created_at      timestamptz NOT NULL DEFAULT now(),
    expires_at      timestamptz NOT NULL,
    accepted_at     timestamptz NULL,
    revoked_at      timestamptz NULL,
    -- The invitation repeats the organization's model, as agency_members does, so the pair below can be checked.
    CONSTRAINT fk_member_invitations_business_model
        FOREIGN KEY (agency_id, business_model) REFERENCES agencies (id, business_model) ON DELETE CASCADE,
    -- The table of allowed (model, role) pairs of ck_agency_members_role_model, without the creator: a creator
    -- organization has one member and nobody to invite.
    CONSTRAINT ck_member_invitations_role_model CHECK (
        (business_model = 'company'
            AND role IN ('organizationadmin', 'marketingmanager', 'contentwriter'))
        OR (business_model = 'agency'
            AND role IN ('organizationadmin', 'clientmanager', 'marketingmanager', 'contentwriter'))),
    CONSTRAINT ck_member_invitations_state CHECK (accepted_at IS NULL OR revoked_at IS NULL),
    CONSTRAINT ck_member_invitations_expiry CHECK (expires_at > created_at)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_member_invitations_token ON member_invitations (token_hash);

-- One pending invitation per organization and email. Expired ones stay pending until the next invitation replaces them.
CREATE UNIQUE INDEX IF NOT EXISTS ux_member_invitations_pending
    ON member_invitations (agency_id, lower(email))
    WHERE accepted_at IS NULL AND revoked_at IS NULL;

CREATE INDEX IF NOT EXISTS ix_member_invitations_agency ON member_invitations (agency_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_member_invitations_agency_model ON member_invitations (agency_id, business_model);
CREATE INDEX IF NOT EXISTS ix_member_invitations_invited_by ON member_invitations (invited_by) WHERE invited_by IS NOT NULL;

-- Rows are history once used, so app_user cannot delete them.
REVOKE DELETE ON member_invitations FROM app_user;

ALTER TABLE member_invitations ENABLE ROW LEVEL SECURITY;
ALTER TABLE member_invitations FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation ON member_invitations;
CREATE POLICY tenant_isolation ON member_invitations
    USING (agency_id = app_agency_id())
    WITH CHECK (agency_id = app_agency_id());

-- ---------------------------------------------------------------------------------------------
-- session_revocations
-- ---------------------------------------------------------------------------------------------
-- A tenant table that app_user cannot touch: only the functions below write it, and the API reads it through
-- session_revocations_snapshot(). valid_after: access tokens issued at or before this moment are void.
CREATE TABLE IF NOT EXISTS session_revocations (
    id           uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    agency_id    uuid        NOT NULL REFERENCES agencies (id) ON DELETE CASCADE,
    user_id      uuid        NOT NULL,
    valid_after  timestamptz NOT NULL,
    created_at   timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_session_revocations_agency_user UNIQUE (agency_id, user_id)
);

CREATE INDEX IF NOT EXISTS ix_session_revocations_valid_after ON session_revocations (valid_after);

REVOKE ALL ON session_revocations FROM app_user;

ALTER TABLE session_revocations ENABLE ROW LEVEL SECURITY;
ALTER TABLE session_revocations FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation ON session_revocations;
CREATE POLICY tenant_isolation ON session_revocations
    USING (agency_id = app_agency_id())
    WITH CHECK (agency_id = app_agency_id());

-- ---------------------------------------------------------------------------------------------
-- Policies for the migrating role. FORCE ROW LEVEL SECURITY also binds the table owner, and the functions
-- below run as the owner: a superuser skips the policies anyway, any other owner needs these. They are for
-- the owner only, by name, never for app_user. The existing tables get exactly the commands the functions use.
-- ---------------------------------------------------------------------------------------------
DO $$
BEGIN
    EXECUTE 'DROP POLICY IF EXISTS owner_team_insert ON users';
    EXECUTE format('CREATE POLICY owner_team_insert ON users FOR INSERT TO %I WITH CHECK (true)', current_user);
    EXECUTE 'DROP POLICY IF EXISTS owner_team_delete ON users';
    EXECUTE format('CREATE POLICY owner_team_delete ON users FOR DELETE TO %I USING (true)', current_user);

    EXECUTE 'DROP POLICY IF EXISTS owner_team_insert ON agency_members';
    EXECUTE format('CREATE POLICY owner_team_insert ON agency_members FOR INSERT TO %I WITH CHECK (true)', current_user);
    EXECUTE 'DROP POLICY IF EXISTS owner_team_update ON agency_members';
    EXECUTE format('CREATE POLICY owner_team_update ON agency_members FOR UPDATE TO %I USING (true) WITH CHECK (true)', current_user);
    EXECUTE 'DROP POLICY IF EXISTS owner_team_delete ON agency_members';
    EXECUTE format('CREATE POLICY owner_team_delete ON agency_members FOR DELETE TO %I USING (true)', current_user);

    EXECUTE 'DROP POLICY IF EXISTS owner_team_insert ON user_identities';
    EXECUTE format('CREATE POLICY owner_team_insert ON user_identities FOR INSERT TO %I WITH CHECK (true)', current_user);

    EXECUTE 'DROP POLICY IF EXISTS owner_manage ON member_invitations';
    EXECUTE format('CREATE POLICY owner_manage ON member_invitations FOR ALL TO %I USING (true) WITH CHECK (true)', current_user);
    EXECUTE 'DROP POLICY IF EXISTS owner_manage ON session_revocations';
    EXECUTE format('CREATE POLICY owner_manage ON session_revocations FOR ALL TO %I USING (true) WITH CHECK (true)', current_user);
END
$$;

-- ---------------------------------------------------------------------------------------------
-- Internal helpers. Not callable by app_user: only the functions below call them.
-- ---------------------------------------------------------------------------------------------
-- True when the connection's user is a member of the connection's organization and their role holds the key.
CREATE OR REPLACE FUNCTION org_caller_holds(p_key text)
    RETURNS boolean
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
    SELECT EXISTS (
        SELECT 1
        FROM agency_members m
        JOIN role_permissions rp ON rp.business_model = m.business_model AND rp.role = m.role
        WHERE m.agency_id = app_agency_id()
          AND m.user_id = app_user_id()
          AND rp.permission_key = p_key
          AND rp.is_granted)
$$;

-- Access tokens of this person for this organization, issued up to now, are void. Old rows go: a token lives
-- 90 days at the very most (Jwt:AccessTokenDays), so a row older than 91 days can no longer match a token.
CREATE OR REPLACE FUNCTION team_end_sessions(p_agency_id uuid, p_user_id uuid)
    RETURNS void
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
BEGIN
    INSERT INTO session_revocations (agency_id, user_id, valid_after)
    VALUES (p_agency_id, p_user_id, clock_timestamp())
    ON CONFLICT (agency_id, user_id) DO UPDATE SET valid_after = EXCLUDED.valid_after;

    DELETE FROM session_revocations r WHERE r.valid_after < now() - interval '91 days';
END
$$;

REVOKE ALL ON FUNCTION org_caller_holds(text) FROM PUBLIC;
REVOKE ALL ON FUNCTION team_end_sessions(uuid, uuid) FROM PUBLIC;

-- ---------------------------------------------------------------------------------------------
-- The team, for someone who holds members.manage
-- ---------------------------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION team_list_members()
    RETURNS TABLE (user_id uuid, email text, full_name text, role text, joined_at timestamptz)
    LANGUAGE plpgsql STABLE SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
BEGIN
    IF NOT org_caller_holds('members.manage') THEN
        RAISE EXCEPTION 'Managing the team requires the members.manage permission.' USING ERRCODE = '42501';
    END IF;

    RETURN QUERY
    SELECT m.user_id, u.email, u.full_name, m.role, m.created_at
    FROM agency_members m
    JOIN users u ON u.id = m.user_id
    WHERE m.agency_id = app_agency_id()
    ORDER BY m.created_at, u.email;
END
$$;

CREATE OR REPLACE FUNCTION team_change_role(p_user_id uuid, p_role text)
    RETURNS void
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
DECLARE
    v_agency   uuid := app_agency_id();
    v_old_role text;
BEGIN
    -- One change at a time per organization: the last-admin rule below reads before it writes. The lock comes
    -- first so that the permission is judged on the team as it is once the earlier change has committed (a
    -- caller removed a moment ago is no longer allowed).
    PERFORM pg_advisory_xact_lock(hashtextextended('team:' || coalesce(v_agency::text, ''), 0));

    IF NOT org_caller_holds('members.manage') THEN
        RAISE EXCEPTION 'Managing the team requires the members.manage permission.' USING ERRCODE = '42501';
    END IF;

    IF p_user_id = app_user_id() THEN
        RAISE EXCEPTION 'cannot_change_self' USING ERRCODE = 'P0001';
    END IF;

    SELECT m.role INTO v_old_role
    FROM agency_members m
    WHERE m.agency_id = v_agency AND m.user_id = p_user_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'member_not_found' USING ERRCODE = 'P0001';
    END IF;

    IF v_old_role = p_role THEN
        RETURN;
    END IF;

    IF v_old_role = 'organizationadmin'
       AND NOT EXISTS (
           SELECT 1 FROM agency_members m
           WHERE m.agency_id = v_agency AND m.role = 'organizationadmin' AND m.user_id <> p_user_id) THEN
        RAISE EXCEPTION 'last_organization_admin' USING ERRCODE = 'P0001';
    END IF;

    -- A role the organization's model does not allow is refused by ck_agency_members_role_model.
    UPDATE agency_members
    SET role = p_role
    WHERE agency_id = v_agency AND user_id = p_user_id;

    -- The role and the permissions travel in the token, so the person signs in again to get the new ones.
    PERFORM team_end_sessions(v_agency, p_user_id);
END
$$;

CREATE OR REPLACE FUNCTION team_remove_member(p_user_id uuid)
    RETURNS void
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
DECLARE
    v_agency uuid := app_agency_id();
    v_role   text;
BEGIN
    PERFORM pg_advisory_xact_lock(hashtextextended('team:' || coalesce(v_agency::text, ''), 0));

    IF NOT org_caller_holds('members.manage') THEN
        RAISE EXCEPTION 'Managing the team requires the members.manage permission.' USING ERRCODE = '42501';
    END IF;

    IF p_user_id = app_user_id() THEN
        RAISE EXCEPTION 'cannot_change_self' USING ERRCODE = 'P0001';
    END IF;

    SELECT m.role INTO v_role
    FROM agency_members m
    WHERE m.agency_id = v_agency AND m.user_id = p_user_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'member_not_found' USING ERRCODE = 'P0001';
    END IF;

    IF v_role = 'organizationadmin'
       AND NOT EXISTS (
           SELECT 1 FROM agency_members m
           WHERE m.agency_id = v_agency AND m.role = 'organizationadmin' AND m.user_id <> p_user_id) THEN
        RAISE EXCEPTION 'last_organization_admin' USING ERRCODE = 'P0001';
    END IF;

    DELETE FROM agency_members WHERE agency_id = v_agency AND user_id = p_user_id;

    PERFORM team_end_sessions(v_agency, p_user_id);

    -- The account existed for this organization only: delete it, with any Google link, so the email is free
    -- again. A person who belongs elsewhere, or who is a Site admin, keeps theirs.
    IF NOT EXISTS (SELECT 1 FROM agency_members m WHERE m.user_id = p_user_id)
       AND NOT EXISTS (SELECT 1 FROM site_admins s WHERE s.user_id = p_user_id) THEN
        DELETE FROM users WHERE id = p_user_id;
    END IF;
END
$$;

-- ---------------------------------------------------------------------------------------------
-- Inviting: is the address already an account? (Registering answers the same question with 409.)
-- ---------------------------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION auth_email_registered(p_email text)
    RETURNS boolean
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
    SELECT EXISTS (SELECT 1 FROM users u WHERE lower(u.email) = lower(p_email))
$$;

-- ---------------------------------------------------------------------------------------------
-- Accepting: before anybody is signed in. The token is the credential, so these take its hash and match it
-- exactly; a missing, used, revoked or expired invitation looks the same.
-- ---------------------------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION invitation_find(p_token_hash text)
    RETURNS TABLE (id uuid, agency_id uuid, organization_name text, business_model text, email text, role text, expires_at timestamptz)
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
    SELECT i.id, i.agency_id, a.name, i.business_model, i.email, i.role, i.expires_at
    FROM member_invitations i
    JOIN agencies a ON a.id = i.agency_id
    WHERE i.token_hash = p_token_hash
      AND i.accepted_at IS NULL
      AND i.revoked_at IS NULL
      AND i.expires_at > now()
$$;

-- Creates the person (with a password, or with the Google identity whose verified email must equal the invited
-- one) and their membership, and marks the invitation used, in one go. Returns where and as what.
CREATE OR REPLACE FUNCTION invitation_accept(
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
    v_inv member_invitations%ROWTYPE;
BEGIN
    IF p_provider IS NULL AND (p_password_hash IS NULL OR p_password_hash = '') THEN
        RAISE EXCEPTION 'A password or a linked sign-in is required.' USING ERRCODE = '22023';
    END IF;

    SELECT * INTO v_inv FROM member_invitations i WHERE i.token_hash = p_token_hash FOR UPDATE;

    IF NOT FOUND OR v_inv.accepted_at IS NOT NULL OR v_inv.revoked_at IS NOT NULL OR v_inv.expires_at <= now() THEN
        RAISE EXCEPTION 'invitation_invalid' USING ERRCODE = 'P0001';
    END IF;

    IF p_provider IS NOT NULL AND lower(coalesce(p_provider_email, '')) <> lower(v_inv.email) THEN
        RAISE EXCEPTION 'invitation_invalid' USING ERRCODE = 'P0001';
    END IF;

    IF EXISTS (SELECT 1 FROM users u WHERE lower(u.email) = lower(v_inv.email)) THEN
        RAISE EXCEPTION 'email_taken' USING ERRCODE = 'P0001';
    END IF;

    INSERT INTO users (id, email, password_hash, full_name)
    VALUES (p_user_id, v_inv.email, p_password_hash, p_full_name);

    IF p_provider IS NOT NULL THEN
        INSERT INTO user_identities (user_id, provider, subject, email)
        VALUES (p_user_id, p_provider, p_subject, v_inv.email);
    END IF;

    INSERT INTO agency_members (agency_id, user_id, business_model, role, last_active_at)
    VALUES (v_inv.agency_id, p_user_id, v_inv.business_model, v_inv.role, now());

    UPDATE member_invitations SET accepted_at = now() WHERE id = v_inv.id;

    RETURN QUERY SELECT v_inv.agency_id, v_inv.role;
END
$$;

-- ---------------------------------------------------------------------------------------------
-- The API's in-memory list of ended sessions. Only recent rows: older ones can no longer match a token.
-- ---------------------------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION session_revocations_snapshot()
    RETURNS TABLE (agency_id uuid, user_id uuid, valid_after timestamptz)
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
    SELECT r.agency_id, r.user_id, r.valid_after
    FROM session_revocations r
    WHERE r.valid_after > now() - interval '91 days'
$$;

REVOKE ALL ON FUNCTION team_list_members() FROM PUBLIC;
REVOKE ALL ON FUNCTION team_change_role(uuid, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION team_remove_member(uuid) FROM PUBLIC;
REVOKE ALL ON FUNCTION auth_email_registered(text) FROM PUBLIC;
REVOKE ALL ON FUNCTION invitation_find(text) FROM PUBLIC;
REVOKE ALL ON FUNCTION invitation_accept(text, uuid, text, text, text, text, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION session_revocations_snapshot() FROM PUBLIC;

GRANT EXECUTE ON FUNCTION team_list_members() TO app_user;
GRANT EXECUTE ON FUNCTION team_change_role(uuid, text) TO app_user;
GRANT EXECUTE ON FUNCTION team_remove_member(uuid) TO app_user;
GRANT EXECUTE ON FUNCTION auth_email_registered(text) TO app_user;
GRANT EXECUTE ON FUNCTION invitation_find(text) TO app_user;
GRANT EXECUTE ON FUNCTION invitation_accept(text, uuid, text, text, text, text, text) TO app_user;
GRANT EXECUTE ON FUNCTION session_revocations_snapshot() TO app_user;
