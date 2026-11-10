-- 010_site_admin_users.sql
-- The Site admin's Users and Channels pages: two new items in the admin menu, and the one list the Users page needs that
-- did not exist yet, every person on the platform with the organization they belong to.
--
-- Idempotent like every script: DbMigrator runs all of them on every start. Once released, never edit this file; add a
-- new numbered script instead. No existing table changes.
--
-- Design:
--   * The two pages are seeded like every other menu page (ON CONFLICT DO NOTHING, then menu_apply_default_items()), so a
--     restart never undoes what the Site admin did with the items (renamed, hidden, deleted). Neither is locked: unlike
--     Dashboard, Menus and Permissions they are not needed to repair a broken menu. Their order (12 and 14) puts them between
--     the Dashboard (10) and the two configuration pages (20 and 30); the Site admin can move them. They name no
--     permission: the token of a Site admin carries none, and every /v1/site route checks site_admins in the database.
--   * Channels needs no data: the list of channels is a catalog in the web client, so there is no function for it.
--   * site_admin_list_members() is SECURITY DEFINER (search_path fixed, not callable by PUBLIC) and checks inside the
--     database that the caller is a site admin, like site_admin_list_agencies_v2(). It returns only what a list needs
--     (never a password hash) and one row per membership: a person who belongs to two organizations is listed twice,
--     once for each. A site admin's own account has no membership and so is not a member.
--   * The total repeats on every row, so a page is one statement; a page past the end has no rows and so no total, and
--     the API asks site_admin_overview() for the count then.
--   * The owner policies the definer function needs to read users, agency_members and agencies already exist
--     (001 and 002), so it also works when the database owner is bound by row-level security.

INSERT INTO menu_pages
    (app, key, path, title, permission_key, default_label, default_icon, default_sort_order, default_is_locked)
VALUES
    ('admin', 'users',    '/admin/users',    'Users',    NULL, 'Users',    'users', 12, false),
    ('admin', 'channels', '/admin/channels', 'Channels', NULL, 'Channels', 'list',  14, false)
ON CONFLICT (app, key) DO NOTHING;

SELECT menu_apply_default_items();

CREATE OR REPLACE FUNCTION site_admin_list_members(p_limit integer, p_offset integer)
    RETURNS TABLE (
        user_id uuid,
        full_name text,
        email text,
        agency_id uuid,
        agency_name text,
        business_model text,
        role text,
        is_active boolean,
        joined_at timestamptz,
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
    SELECT u.id,
           u.full_name,
           u.email,
           a.id,
           a.name,
           m.business_model,
           m.role,
           m.is_active,
           m.created_at,
           count(*) OVER ()
    FROM agency_members m
    JOIN users u ON u.id = m.user_id
    JOIN agencies a ON a.id = m.agency_id
    ORDER BY m.created_at DESC, m.agency_id, m.user_id
    LIMIT p_limit OFFSET p_offset;
END
$$;

REVOKE ALL ON FUNCTION site_admin_list_members(integer, integer) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION site_admin_list_members(integer, integer) TO app_user;
