-- 012_member_platforms.sql
-- The member's Platform page: a menu item for company, agency and creator members that lists every channel (a "platform") and
-- whether the Site admin has enabled it for the member's organization. Read only: enabling stays with the Site admin
-- (011_channels.sql).
--
-- Idempotent like every script: DbMigrator runs all of them on every start. Once released, never edit this file; add a
-- new numbered script instead. No existing table changes.
--
-- Design:
--   * A new permission, platforms.view, held by every role of every business model by default. The Site admin can take it
--     away per role in the permissions editor; the seed is INSERT ... ON CONFLICT DO NOTHING and never updates a grant, so a
--     restart does not undo that. Adding the grants raises the permission version once, so everybody signs in again once.
--   * The page is seeded like every other menu page (ON CONFLICT DO NOTHING, then menu_apply_default_items()): the label
--     "Platform", between Workspaces (20) and Team (30), not locked, so the Site admin may rename, hide or delete the item and a
--     restart never brings it back. The icon "list" is one the menu already has.
--   * app_user has no privilege on channels (the image bytes and the creator are not for members), so a member reads them
--     only through two SECURITY DEFINER functions (search_path fixed, not callable by PUBLIC). Both ask platform_caller_allowed()
--     first: the connection must act for a user and an organization, the user must be an ACTIVE member of an ACTIVE
--     organization, and their role must hold platforms.view. Otherwise 42501.
--   * is_enabled is read for app_agency_id() only, so an organization never sees another's assignment. A member list carries
--     no creator, no counts and no image bytes; the icon is a second function so a list stays small.
--   * The owner policies the definer functions need (channels, organization_channels, agency_members, agencies) already exist
--     (002, 007, 011), so they also work when the database owner is bound by row-level security.

INSERT INTO permissions (key, title, description, is_protected) VALUES
    ('platforms.view', 'View platforms',
        'See the channels (platforms) the platform supports and which of them are enabled for the organization: the Platform page.', false)
ON CONFLICT (key) DO NOTHING;

-- Eight (model, role) slots. Every role holds it by default.
INSERT INTO role_permissions (business_model, role, permission_key, is_granted)
SELECT s.business_model, s.role, 'platforms.view', true
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
    ('member', 'platforms', '/platforms', 'Platforms', 'platforms.view', 'Platform', 'list', 25, false)
ON CONFLICT (app, key) DO NOTHING;

SELECT menu_apply_default_items();

-- ---------------------------------------------------------------------------------------------
-- Who may read the platforms
-- ---------------------------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION platform_caller_allowed()
    RETURNS boolean
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
    SELECT app_user_id() IS NOT NULL
       AND app_agency_id() IS NOT NULL
       AND EXISTS (
            SELECT 1
            FROM agency_members m
            JOIN agencies a ON a.id = m.agency_id
            WHERE m.agency_id = app_agency_id()
              AND m.user_id = app_user_id()
              AND m.is_active
              AND a.is_active)
       AND org_caller_holds('platforms.view')
$$;

-- Every channel, oldest first, and whether it is enabled for the caller's organization.
CREATE OR REPLACE FUNCTION member_list_platforms()
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
    IF NOT platform_caller_allowed() THEN
        RAISE EXCEPTION 'Viewing the platforms requires the platforms.view permission.' USING ERRCODE = '42501';
    END IF;

    RETURN QUERY
    SELECT c.id,
           c.name,
           c.description,
           c.icon_key,
           c.icon_image IS NOT NULL,
           EXISTS (SELECT 1 FROM organization_channels oc WHERE oc.agency_id = app_agency_id() AND oc.channel_id = c.id)
    FROM channels c
    ORDER BY c.created_at, c.name;
END
$$;

-- The uploaded icon of one channel, as it was stored. No row for a channel that has a built-in icon or does not exist.
CREATE OR REPLACE FUNCTION member_platform_icon(p_id uuid)
    RETURNS TABLE (content_type text, image bytea)
    LANGUAGE plpgsql STABLE SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
BEGIN
    IF NOT platform_caller_allowed() THEN
        RAISE EXCEPTION 'Viewing the platforms requires the platforms.view permission.' USING ERRCODE = '42501';
    END IF;

    RETURN QUERY
    SELECT c.icon_content_type, c.icon_image
    FROM channels c
    WHERE c.id = p_id AND c.icon_image IS NOT NULL;
END
$$;

REVOKE ALL ON FUNCTION platform_caller_allowed() FROM PUBLIC;
REVOKE ALL ON FUNCTION member_list_platforms() FROM PUBLIC;
REVOKE ALL ON FUNCTION member_platform_icon(uuid) FROM PUBLIC;

GRANT EXECUTE ON FUNCTION member_list_platforms() TO app_user;
GRANT EXECUTE ON FUNCTION member_platform_icon(uuid) TO app_user;
