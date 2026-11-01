-- 004_external_identities.sql
-- Sign-in with an outside identity provider (Google). A person who signs up that way has no password,
-- so users.password_hash may be empty, and the provider's own id for the person is kept in a new table.
--
-- Idempotent like every script: DbMigrator runs all of them on every start. Once released, never
-- edit this file; add a new numbered script instead.
--
-- Design:
--   * user_identities holds (provider, subject) for a user. The pair is unique, so one Google account
--     can belong to one user only. app_user can read and add rows for the user it is acting as, and
--     never change or delete them.
--   * A Google sign-in must find its user before any user is known, so that lookup is a narrow
--     SECURITY DEFINER function that takes one exact (provider, subject) and returns one user id.
--   * An existing password account is never linked to a Google identity by this script or by the API:
--     password sign-ups are not email-verified, so linking by email would let whoever registered an
--     address first keep access. (A later change can link after the email is verified.)
--   * Every user needs a way in: a password or an identity. That spans two tables, so it is a deferred
--     constraint trigger, checked when the transaction commits (the registration transaction adds the
--     user and the identity together).

-- ---------------------------------------------------------------------------------------------
-- users.password_hash may be empty
-- ---------------------------------------------------------------------------------------------
ALTER TABLE users ALTER COLUMN password_hash DROP NOT NULL;

-- ---------------------------------------------------------------------------------------------
-- user_identities
-- ---------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS user_identities (
    id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id     uuid        NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    provider    text        NOT NULL CHECK (provider IN ('google')),
    subject     text        NOT NULL CHECK (length(subject) BETWEEN 1 AND 255),
    email       text        NOT NULL CHECK (length(email) BETWEEN 3 AND 254),
    created_at  timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_user_identities_provider_subject UNIQUE (provider, subject),
    CONSTRAINT uq_user_identities_user_provider UNIQUE (user_id, provider)
);

-- Default privileges gave app_user full DML on this new table; it may only read and add.
REVOKE ALL ON user_identities FROM app_user;
GRANT SELECT, INSERT ON user_identities TO app_user;

ALTER TABLE user_identities ENABLE ROW LEVEL SECURITY;
ALTER TABLE user_identities FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS own_select ON user_identities;
CREATE POLICY own_select ON user_identities FOR SELECT
    USING (user_id = app_user_id());

DROP POLICY IF EXISTS own_insert ON user_identities;
CREATE POLICY own_insert ON user_identities FOR INSERT
    WITH CHECK (user_id = app_user_id());

-- FORCE ROW LEVEL SECURITY also binds the table owner, and the lookup function below runs as the owner.
-- A superuser skips the policies anyway; for any other owner this lets the function see the rows.
DO $$
BEGIN
    EXECUTE 'DROP POLICY IF EXISTS owner_lookup ON user_identities';
    EXECUTE format('CREATE POLICY owner_lookup ON user_identities FOR SELECT TO %I USING (true)', current_user);
END
$$;

-- ---------------------------------------------------------------------------------------------
-- Lookup before the user is known
-- ---------------------------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION auth_find_user_by_identity(p_provider text, p_subject text)
    RETURNS TABLE (user_id uuid)
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
    SELECT i.user_id
    FROM user_identities i
    WHERE i.provider = p_provider AND i.subject = p_subject
$$;

REVOKE ALL ON FUNCTION auth_find_user_by_identity(text, text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION auth_find_user_by_identity(text, text) TO app_user;

-- ---------------------------------------------------------------------------------------------
-- A user needs a password or an identity
-- ---------------------------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION users_require_credential()
    RETURNS trigger
    LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW.password_hash IS NULL
       AND NOT EXISTS (SELECT 1 FROM user_identities i WHERE i.user_id = NEW.id) THEN
        RAISE EXCEPTION 'A user needs a password or a linked sign-in.'
            USING ERRCODE = '23514', CONSTRAINT = 'ck_users_credential';
    END IF;
    RETURN NULL;
END
$$;

DROP TRIGGER IF EXISTS ck_users_credential ON users;
CREATE CONSTRAINT TRIGGER ck_users_credential
    AFTER INSERT OR UPDATE OF password_hash ON users
    DEFERRABLE INITIALLY DEFERRED
    FOR EACH ROW EXECUTE FUNCTION users_require_credential();
