-- 005_menus_permissions.sql
-- Side menus and permissions that the Site admin configures.
--
-- Idempotent like every script: DbMigrator runs all of them on every start. Once released, never
-- edit this file; add a new numbered script instead.
--
-- Design:
--   * Five global tables, not tenant tables (no agency_id): permissions, role_permissions,
--     permission_state, menu_pages and menu_items. Row-level security is enabled and forced on all of
--     them. app_user can only SELECT the four it needs (none of permission_state), and only when a
--     user is set, so a connection with no user sees nothing. It never writes: every change goes
--     through a narrow SECURITY DEFINER function that checks site_admins inside the database.
--   * A permission is a code-defined key (PermissionKeys in Platform.Helpers). role_permissions says
--     whether each (business model, role) pair holds it. It has one row per pair and key, always
--     written and never deleted: a revoke is is_granted = false, so a restart cannot undo it.
--   * permission_state holds one counter, the permission version. A trigger raises it whenever the
--     effective grants change. The version travels in every access token (claim pv); a token with an
--     older one is refused, so a change reaches signed-in people at their next refresh.
--   * menu_pages is the registry of built-in pages a link may point to. menu_items puts a page in the
--     menu of its own app (member or admin) with a label, icon, order and visibility. A locked item
--     can be renamed and moved but never hidden or deleted.
--   * Seeds never overwrite what the Site admin changed (INSERT ... ON CONFLICT DO NOTHING). A page
--     gets its default menu item exactly once, tracked by menu_pages.default_item_created, so an item
--     that was deleted stays deleted. To add a page or permission later, a new script inserts its rows
--     the same way and calls menu_apply_default_items().
--   * The default grants are the rules that were hard-coded before (RolePermissions.GrantedByDefault);
--     an integration test compares the two.

-- ---------------------------------------------------------------------------------------------
-- permissions: the catalog
-- ---------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS permissions (
    key           text        PRIMARY KEY,
    title         text        NOT NULL,
    description   text        NOT NULL,
    is_protected  boolean     NOT NULL DEFAULT false,
    created_at    timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_permissions_key CHECK (key ~ '^[a-z][a-z0-9]*(\.[a-z][a-z0-9_]*)+$' AND length(key) <= 64),
    CONSTRAINT ck_permissions_title CHECK (length(btrim(title)) BETWEEN 1 AND 80),
    CONSTRAINT ck_permissions_description CHECK (length(btrim(description)) BETWEEN 1 AND 300)
);

-- ---------------------------------------------------------------------------------------------
-- role_permissions: who holds what
-- ---------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS role_permissions (
    business_model  text        NOT NULL,
    role            text        NOT NULL,
    permission_key  text        NOT NULL REFERENCES permissions (key),
    is_granted      boolean     NOT NULL,
    updated_at      timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (business_model, role, permission_key),
    -- The same table of allowed pairs as ck_agency_members_role_model (003_business_models.sql) and
    -- BusinessModelRules; an integration test compares them for every pair.
    CONSTRAINT ck_role_permissions_role_model CHECK (
        (business_model = 'company'
            AND role IN ('organizationadmin', 'marketingmanager', 'contentwriter'))
        OR (business_model = 'agency'
            AND role IN ('organizationadmin', 'clientmanager', 'marketingmanager', 'contentwriter'))
        OR (business_model = 'creator'
            AND role = 'contentwriter'))
);

CREATE INDEX IF NOT EXISTS ix_role_permissions_permission ON role_permissions (permission_key);

-- ---------------------------------------------------------------------------------------------
-- permission_state: the permission version (a single row)
-- ---------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS permission_state (
    id       boolean PRIMARY KEY DEFAULT true CHECK (id),
    version  bigint  NOT NULL DEFAULT 1 CHECK (version >= 1)
);

-- ---------------------------------------------------------------------------------------------
-- menu_pages: the built-in pages a menu item may link to
-- ---------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS menu_pages (
    app                   text        NOT NULL,
    key                   text        NOT NULL,
    path                  text        NOT NULL,
    title                 text        NOT NULL,
    permission_key        text        NULL REFERENCES permissions (key),
    default_label         text        NOT NULL,
    default_icon          text        NOT NULL,
    default_sort_order    integer     NOT NULL,
    default_is_locked     boolean     NOT NULL DEFAULT false,
    default_item_created  boolean     NOT NULL DEFAULT false,
    created_at            timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (app, key),
    CONSTRAINT uq_menu_pages_app_path UNIQUE (app, path),
    CONSTRAINT ck_menu_pages_app CHECK (app IN ('member', 'admin')),
    CONSTRAINT ck_menu_pages_key CHECK (key ~ '^[a-z][a-z0-9-]{0,39}$'),
    CONSTRAINT ck_menu_pages_title CHECK (length(btrim(title)) BETWEEN 1 AND 60),
    -- A path is lower-case segments and nothing else: no scheme, no host, no query, no "//".
    CONSTRAINT ck_menu_pages_path CHECK (path ~ '^/([a-z0-9-]+(/[a-z0-9-]+)*)?$' AND length(path) <= 100),
    CONSTRAINT ck_menu_pages_app_path CHECK (
        (app = 'admin' AND (path = '/admin' OR path LIKE '/admin/%'))
        OR (app = 'member' AND path <> '/' AND path !~ '^/admin(/|$)')),
    -- The site admin app has no roles, so its pages cannot require a permission.
    CONSTRAINT ck_menu_pages_admin_unguarded CHECK (app = 'member' OR permission_key IS NULL),
    CONSTRAINT ck_menu_pages_default_label CHECK (length(btrim(default_label)) BETWEEN 1 AND 60),
    CONSTRAINT ck_menu_pages_default_icon CHECK (default_icon ~ '^[a-z][a-z0-9-]{0,29}$')
);

CREATE INDEX IF NOT EXISTS ix_menu_pages_permission ON menu_pages (permission_key) WHERE permission_key IS NOT NULL;

-- ---------------------------------------------------------------------------------------------
-- menu_items: what a menu shows
-- ---------------------------------------------------------------------------------------------
-- The exact set of icons lives in MenuIcons (Platform.Auth), so a later script can add one; the
-- database only checks the shape.
CREATE TABLE IF NOT EXISTS menu_items (
    id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    menu_key    text        NOT NULL,
    page_key    text        NOT NULL,
    label       text        NOT NULL,
    icon        text        NOT NULL,
    sort_order  integer     NOT NULL,
    is_visible  boolean     NOT NULL DEFAULT true,
    is_locked   boolean     NOT NULL DEFAULT false,
    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now(),
    -- A page can only sit in the menu of its own app. The unique constraint below indexes these columns.
    CONSTRAINT fk_menu_items_page FOREIGN KEY (menu_key, page_key) REFERENCES menu_pages (app, key) ON DELETE CASCADE,
    CONSTRAINT uq_menu_items_menu_page UNIQUE (menu_key, page_key),
    CONSTRAINT ck_menu_items_label CHECK (label = btrim(label) AND length(label) BETWEEN 1 AND 60),
    CONSTRAINT ck_menu_items_icon CHECK (icon ~ '^[a-z][a-z0-9-]{0,29}$'),
    -- A locked item is one a Site admin must not lose (their own pages, the members' home page).
    CONSTRAINT ck_menu_items_locked_visible CHECK (NOT is_locked OR is_visible)
);

-- Sort order is not unique: a reorder rewrites the positions as 10, 20, 30, and readers add the id.
CREATE INDEX IF NOT EXISTS ix_menu_items_menu_order ON menu_items (menu_key, sort_order);

-- ---------------------------------------------------------------------------------------------
-- Row-level security and grants. Default privileges gave app_user full DML on the new tables;
-- take it back to read-only. The migrating role gets a policy of its own, because FORCE ROW LEVEL
-- SECURITY also binds the table owner (a superuser skips policies; any other owner needs this for
-- the seeds below and for the SECURITY DEFINER functions, which run as the owner).
-- ---------------------------------------------------------------------------------------------
DO $$
DECLARE
    tbl text;
BEGIN
    FOREACH tbl IN ARRAY ARRAY['permissions', 'role_permissions', 'permission_state', 'menu_pages', 'menu_items'] LOOP
        EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY', tbl);
        EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY', tbl);
        EXECUTE format('REVOKE ALL ON %I FROM app_user', tbl);

        EXECUTE format('DROP POLICY IF EXISTS owner_manage ON %I', tbl);
        EXECUTE format('CREATE POLICY owner_manage ON %I FOR ALL TO %I USING (true) WITH CHECK (true)', tbl, current_user);
    END LOOP;

    -- Configuration, not secrets: any signed-in user may read it, a connection with no user sees nothing.
    -- permission_state gets no policy and no grant for app_user; permission_snapshot() reads it.
    FOREACH tbl IN ARRAY ARRAY['permissions', 'role_permissions', 'menu_pages', 'menu_items'] LOOP
        EXECUTE format('GRANT SELECT ON %I TO app_user', tbl);
        EXECUTE format('DROP POLICY IF EXISTS signed_in_select ON %I', tbl);
        EXECUTE format('CREATE POLICY signed_in_select ON %I FOR SELECT USING (app_user_id() IS NOT NULL)', tbl);
    END LOOP;
END
$$;

-- ---------------------------------------------------------------------------------------------
-- Triggers on role_permissions
-- ---------------------------------------------------------------------------------------------
-- A protected permission can never be revoked, from anywhere. Cross-table, so a trigger and not a CHECK.
CREATE OR REPLACE FUNCTION role_permissions_guard()
    RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
BEGIN
    IF NOT NEW.is_granted
       AND EXISTS (SELECT 1 FROM permissions p WHERE p.key = NEW.permission_key AND p.is_protected) THEN
        RAISE EXCEPTION 'permission_protected' USING ERRCODE = 'P0001';
    END IF;

    IF TG_OP = 'UPDATE' THEN
        NEW.updated_at := now();
    END IF;

    RETURN NEW;
END
$$;

-- Raises the permission version. A counter, not a timestamp, and the row is locked while it is
-- raised, so two changes at once cannot end on the same number. The upsert means a missing row
-- cannot silently stop the version from moving.
CREATE OR REPLACE FUNCTION permission_version_bump()
    RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
BEGIN
    INSERT INTO permission_state (id, version) VALUES (true, 2)
    ON CONFLICT (id) DO UPDATE SET version = permission_state.version + 1;
    RETURN NULL;
END
$$;

REVOKE ALL ON FUNCTION role_permissions_guard() FROM PUBLIC;
REVOKE ALL ON FUNCTION permission_version_bump() FROM PUBLIC;

DROP TRIGGER IF EXISTS tr_role_permissions_guard ON role_permissions;
CREATE TRIGGER tr_role_permissions_guard
    BEFORE INSERT OR UPDATE ON role_permissions
    FOR EACH ROW EXECUTE FUNCTION role_permissions_guard();

-- Row-level, and only for a real change: the seed below runs on every start with ON CONFLICT DO
-- NOTHING, and a skipped row fires no AFTER trigger, so a restart does not move the version.
DROP TRIGGER IF EXISTS tr_role_permissions_bump_insert ON role_permissions;
CREATE TRIGGER tr_role_permissions_bump_insert
    AFTER INSERT ON role_permissions
    FOR EACH ROW EXECUTE FUNCTION permission_version_bump();

DROP TRIGGER IF EXISTS tr_role_permissions_bump_update ON role_permissions;
CREATE TRIGGER tr_role_permissions_bump_update
    AFTER UPDATE ON role_permissions
    FOR EACH ROW WHEN (OLD.is_granted IS DISTINCT FROM NEW.is_granted)
    EXECUTE FUNCTION permission_version_bump();

-- ---------------------------------------------------------------------------------------------
-- Seeds. Insert only: a row that exists is the Site admin's choice (or an earlier seed's) and wins.
-- ---------------------------------------------------------------------------------------------
INSERT INTO permission_state (id, version) VALUES (true, 1) ON CONFLICT (id) DO NOTHING;

INSERT INTO permissions (key, title, description, is_protected) VALUES
    ('dashboard.view', 'View the dashboard',
        'Open the member dashboard, the home page every member can always reach.', true),
    ('workspaces.view', 'View workspaces',
        'See the organization''s workspaces: the Workspaces page and the workspace list.', false),
    ('workspaces.create', 'Create workspaces',
        'Add a workspace to the organization.', false),
    ('data.read', 'Read data',
        'Read the organization''s data. The baseline behind the read endpoints.', false),
    ('data.write', 'Change data',
        'Create and change the organization''s data. The baseline behind the write endpoints.', false),
    ('organization.manage', 'Manage the organization',
        'Manage the organization itself: its members and its workspaces.', false)
ON CONFLICT (key) DO NOTHING;

-- The rules that were hard-coded before: every role reads; the client manager does not write;
-- only the organization admin manages and creates workspaces. Eight (model, role) slots by six keys.
INSERT INTO role_permissions (business_model, role, permission_key, is_granted)
SELECT s.business_model, s.role, d.permission_key, s.role = ANY (d.granted_roles)
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
CROSS JOIN (VALUES
        ('dashboard.view', ARRAY['organizationadmin', 'clientmanager', 'marketingmanager', 'contentwriter']),
        ('workspaces.view', ARRAY['organizationadmin', 'clientmanager', 'marketingmanager', 'contentwriter']),
        ('workspaces.create', ARRAY['organizationadmin']),
        ('data.read', ARRAY['organizationadmin', 'clientmanager', 'marketingmanager', 'contentwriter']),
        ('data.write', ARRAY['organizationadmin', 'marketingmanager', 'contentwriter']),
        ('organization.manage', ARRAY['organizationadmin'])
     ) AS d (permission_key, granted_roles)
ON CONFLICT (business_model, role, permission_key) DO NOTHING;

INSERT INTO menu_pages
    (app, key, path, title, permission_key, default_label, default_icon, default_sort_order, default_is_locked)
VALUES
    ('member', 'dashboard',   '/dashboard',         'Dashboard',   'dashboard.view',  'Dashboard',   'home',   10, true),
    ('member', 'workspaces',  '/workspaces',        'Workspaces',  'workspaces.view', 'Workspaces',  'folder', 20, false),
    ('admin',  'dashboard',   '/admin',             'Dashboard',   NULL,              'Dashboard',   'home',   10, true),
    ('admin',  'menus',       '/admin/menus',       'Menus',       NULL,              'Menus',       'menu',   20, true),
    ('admin',  'permissions', '/admin/permissions', 'Permissions', NULL,              'Permissions', 'shield', 30, true)
ON CONFLICT (app, key) DO NOTHING;

-- ---------------------------------------------------------------------------------------------
-- Default menu items. Gives every page that has not had one yet its item, once. Not callable by
-- app_user: scripts call it, nobody else.
-- ---------------------------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION menu_apply_default_items()
    RETURNS void
    LANGUAGE plpgsql
    SET search_path = public, pg_temp
AS $$
BEGIN
    INSERT INTO menu_items (menu_key, page_key, label, icon, sort_order, is_visible, is_locked)
    SELECT p.app, p.key, p.default_label, p.default_icon, p.default_sort_order, true, p.default_is_locked
    FROM menu_pages p
    WHERE NOT p.default_item_created
    ON CONFLICT (menu_key, page_key) DO NOTHING;

    UPDATE menu_pages SET default_item_created = true WHERE NOT default_item_created;
END
$$;

REVOKE ALL ON FUNCTION menu_apply_default_items() FROM PUBLIC;

SELECT menu_apply_default_items();

-- ---------------------------------------------------------------------------------------------
-- Reads for the in-memory caches. Configuration, not secrets, and needed before a user is known
-- (a token is issued while signing in), so these check nobody.
-- ---------------------------------------------------------------------------------------------
-- Every grant and the version in ONE statement, so the two are consistent. The LEFT JOIN keeps the
-- version when there are no grants at all.
CREATE OR REPLACE FUNCTION permission_snapshot()
    RETURNS TABLE (version bigint, business_model text, role text, permission_key text, is_granted boolean)
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
    SELECT s.version, rp.business_model, rp.role, rp.permission_key, rp.is_granted
    FROM permission_state s
    LEFT JOIN role_permissions rp ON true
    ORDER BY rp.business_model, rp.role, rp.permission_key
$$;

CREATE OR REPLACE FUNCTION menu_snapshot()
    RETURNS TABLE (
        menu_key text, id uuid, page_key text, page_title text, path text, permission_key text,
        label text, icon text, sort_order integer, is_visible boolean, is_locked boolean)
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
    SELECT i.menu_key, i.id, i.page_key, p.title, p.path, p.permission_key,
           i.label, i.icon, i.sort_order, i.is_visible, i.is_locked
    FROM menu_items i
    JOIN menu_pages p ON p.app = i.menu_key AND p.key = i.page_key
    ORDER BY i.menu_key, i.sort_order, i.id
$$;

REVOKE ALL ON FUNCTION permission_snapshot() FROM PUBLIC;
REVOKE ALL ON FUNCTION menu_snapshot() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION permission_snapshot() TO app_user;
GRANT EXECUTE ON FUNCTION menu_snapshot() TO app_user;

-- ---------------------------------------------------------------------------------------------
-- Writes. Each takes the caller from app_user_id() and refuses anybody who is not in site_admins
-- (42501). A broken rule raises P0001 with a short code as the message; the API maps it.
-- ---------------------------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION site_admin_create_menu_item(
        p_menu_key text, p_page_key text, p_label text, p_icon text, p_is_visible boolean)
    RETURNS uuid
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
DECLARE
    v_id uuid;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM site_admins s WHERE s.user_id = app_user_id()) THEN
        RAISE EXCEPTION 'Site admin access is required.' USING ERRCODE = '42501';
    END IF;

    -- A page that is not in this app's registry fails fk_menu_items_page (23503), a page already in
    -- the menu fails uq_menu_items_menu_page (23505), a bad label or icon fails its CHECK (23514).
    INSERT INTO menu_items (menu_key, page_key, label, icon, sort_order, is_visible)
    VALUES (p_menu_key, p_page_key, btrim(p_label), p_icon,
            COALESCE((SELECT max(i.sort_order) FROM menu_items i WHERE i.menu_key = p_menu_key), 0) + 10,
            COALESCE(p_is_visible, true))
    RETURNING id INTO v_id;

    RETURN v_id;
END
$$;

CREATE OR REPLACE FUNCTION site_admin_update_menu_item(
        p_id uuid, p_label text, p_icon text, p_is_visible boolean)
    RETURNS boolean
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
DECLARE
    v_locked boolean;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM site_admins s WHERE s.user_id = app_user_id()) THEN
        RAISE EXCEPTION 'Site admin access is required.' USING ERRCODE = '42501';
    END IF;

    SELECT i.is_locked INTO v_locked FROM menu_items i WHERE i.id = p_id FOR UPDATE;
    IF NOT FOUND THEN
        RETURN false;
    END IF;

    -- A locked item can be renamed and given another icon, never hidden.
    IF v_locked AND p_is_visible IS NOT DISTINCT FROM false THEN
        RAISE EXCEPTION 'menu_item_locked' USING ERRCODE = 'P0001';
    END IF;

    UPDATE menu_items
    SET label = btrim(p_label),
        icon = p_icon,
        is_visible = COALESCE(p_is_visible, is_visible),
        updated_at = now()
    WHERE id = p_id;

    RETURN true;
END
$$;

CREATE OR REPLACE FUNCTION site_admin_delete_menu_item(p_id uuid)
    RETURNS boolean
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
DECLARE
    v_locked boolean;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM site_admins s WHERE s.user_id = app_user_id()) THEN
        RAISE EXCEPTION 'Site admin access is required.' USING ERRCODE = '42501';
    END IF;

    SELECT i.is_locked INTO v_locked FROM menu_items i WHERE i.id = p_id FOR UPDATE;
    IF NOT FOUND THEN
        RETURN false;
    END IF;

    IF v_locked THEN
        RAISE EXCEPTION 'menu_item_locked' USING ERRCODE = 'P0001';
    END IF;

    DELETE FROM menu_items WHERE id = p_id;
    RETURN true;
END
$$;

-- The list must be exactly the ids the menu holds now, once each; anything else means the page the
-- Site admin was looking at is out of date, and nothing is changed.
CREATE OR REPLACE FUNCTION site_admin_reorder_menu(p_menu_key text, p_item_ids jsonb)
    RETURNS void
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
DECLARE
    v_given integer;
    v_current integer;
    v_matched integer;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM site_admins s WHERE s.user_id = app_user_id()) THEN
        RAISE EXCEPTION 'Site admin access is required.' USING ERRCODE = '42501';
    END IF;

    IF p_item_ids IS NULL OR jsonb_typeof(p_item_ids) <> 'array' THEN
        RAISE EXCEPTION 'menu_order_mismatch' USING ERRCODE = 'P0001';
    END IF;

    -- Lock the menu's rows so two reorders (or a reorder and a delete) cannot interleave.
    PERFORM 1 FROM menu_items i WHERE i.menu_key = p_menu_key FOR UPDATE;

    SELECT count(*) INTO v_given FROM jsonb_array_elements_text(p_item_ids);
    SELECT count(*) INTO v_current FROM menu_items i WHERE i.menu_key = p_menu_key;
    SELECT count(*) INTO v_matched
    FROM (SELECT DISTINCT g.value::uuid AS id FROM jsonb_array_elements_text(p_item_ids) AS g (value)) AS given
    JOIN menu_items i ON i.id = given.id AND i.menu_key = p_menu_key;

    IF v_given <> v_current OR v_matched <> v_current THEN
        RAISE EXCEPTION 'menu_order_mismatch' USING ERRCODE = 'P0001';
    END IF;

    UPDATE menu_items i
    SET sort_order = o.pos * 10,
        updated_at = now()
    FROM jsonb_array_elements_text(p_item_ids) WITH ORDINALITY AS o (value, pos)
    WHERE i.id = o.value::uuid AND i.menu_key = p_menu_key;
END
$$;

-- Returns false when there is no such (model, role, permission) row, which also covers a pair that
-- is not allowed or a key that does not exist. Revoking a protected permission raises
-- 'permission_protected' from the trigger.
CREATE OR REPLACE FUNCTION site_admin_set_role_permission(
        p_business_model text, p_role text, p_permission_key text, p_granted boolean)
    RETURNS boolean
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path = public, pg_temp
AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM site_admins s WHERE s.user_id = app_user_id()) THEN
        RAISE EXCEPTION 'Site admin access is required.' USING ERRCODE = '42501';
    END IF;

    IF p_granted IS NULL THEN
        RAISE EXCEPTION 'Granted must be true or false.' USING ERRCODE = '22004';
    END IF;

    UPDATE role_permissions rp
    SET is_granted = p_granted
    WHERE rp.business_model = p_business_model
      AND rp.role = p_role
      AND rp.permission_key = p_permission_key;

    RETURN FOUND;
END
$$;

REVOKE ALL ON FUNCTION site_admin_create_menu_item(text, text, text, text, boolean) FROM PUBLIC;
REVOKE ALL ON FUNCTION site_admin_update_menu_item(uuid, text, text, boolean) FROM PUBLIC;
REVOKE ALL ON FUNCTION site_admin_delete_menu_item(uuid) FROM PUBLIC;
REVOKE ALL ON FUNCTION site_admin_reorder_menu(text, jsonb) FROM PUBLIC;
REVOKE ALL ON FUNCTION site_admin_set_role_permission(text, text, text, boolean) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION site_admin_create_menu_item(text, text, text, text, boolean) TO app_user;
GRANT EXECUTE ON FUNCTION site_admin_update_menu_item(uuid, text, text, boolean) TO app_user;
GRANT EXECUTE ON FUNCTION site_admin_delete_menu_item(uuid) TO app_user;
GRANT EXECUTE ON FUNCTION site_admin_reorder_menu(text, jsonb) TO app_user;
GRANT EXECUTE ON FUNCTION site_admin_set_role_permission(text, text, text, boolean) TO app_user;
