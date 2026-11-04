-- 009_account_security.sql
-- Changing your own password ends your other sessions. An access token lasts up to 30 days and nothing else refers to
-- it, so without this a copied token (or an old session on another device) would outlive the password change. The
-- password itself is updated by the API under row-level security (self_access lets a user change only their own
-- row); what the API may not do is write session_revocations, so that part is this one function.
--
-- Idempotent like every script: DbMigrator runs all of them on every start. Once released, never edit this file; add
-- a new numbered script instead. No existing table changes.
--
-- Design:
--   * auth_end_own_sessions() is SECURITY DEFINER (search_path fixed, not callable by PUBLIC). It acts for the
--     connection's user only (app_user_id()): it writes a session_revocations row for each organization the user belongs to.
--   * valid_after is one second BEFORE now, not now. A token states when it was issued in whole seconds and is void when
--     that second is not after the row's, so with "now" the fresh token the API issues right after the change, for the
--     session that made it, would be void too. One second earlier voids every token issued before the previous second and
--     lets the next one through. Only a token issued within the last second could survive, and only for a person who
--     signed in that second. The API and the database are assumed to agree on the time to about a second, as every other
--     revocation already assumes.
--   * A row only ever moves forward (GREATEST), so an end that was already recorded later is never taken back.

CREATE OR REPLACE FUNCTION auth_end_own_sessions()
    RETURNS void
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
DECLARE
    v_user uuid := app_user_id();
BEGIN
    IF v_user IS NULL THEN
        RAISE EXCEPTION 'A signed-in user is required.' USING ERRCODE = '42501';
    END IF;

    INSERT INTO session_revocations (agency_id, user_id, valid_after)
    SELECT m.agency_id, v_user, clock_timestamp() - interval '1 second'
    FROM agency_members m
    WHERE m.user_id = v_user
    ON CONFLICT (agency_id, user_id)
        DO UPDATE SET valid_after = GREATEST(session_revocations.valid_after, EXCLUDED.valid_after);
END
$$;

REVOKE ALL ON FUNCTION auth_end_own_sessions() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION auth_end_own_sessions() TO app_user;
