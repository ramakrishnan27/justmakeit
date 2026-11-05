-- 006_remove_refresh_tokens.sql
-- Sessions no longer use refresh tokens. A sign-in is one access token, kept in an HttpOnly cookie, that lasts
-- Jwt:AccessTokenDays (30) and is never renewed: when it runs out the person signs in again. Nothing is stored for a
-- session, so the two tables that held refresh tokens and the two lookups that read them have no use.
--
-- Idempotent like every script: DbMigrator runs all of them on every start, in file-name order, in one transaction.
-- That means 001 and 002 create these tables and functions again on each start, and this script drops them again
-- straight after, in the same transaction. That is harmless and it is why this script must stay a plain drop. Nothing
-- may read or write these tables any more, and nothing later may depend on them.
--
-- Once released, never edit this file; add a new numbered script instead.

DROP FUNCTION IF EXISTS auth_find_refresh_token(text);
DROP FUNCTION IF EXISTS auth_find_site_refresh_token(text);

-- The policies, indexes and grants go with the tables.
DROP TABLE IF EXISTS site_admin_refresh_tokens;
DROP TABLE IF EXISTS refresh_tokens;
