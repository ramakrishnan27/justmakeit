-- 013_member_studio_menu.sql
-- Four more pages in the members' sidebar, for company, agency and creator members: Content Studio, Media Library,
-- AI Studio and Calendar. The Site admin's own sidebar is not touched. None of the four features exists yet, so each page
-- is a "coming soon" placeholder in the web app; the menu item, its permission and its place in the menu are real, so the
-- Site admin can already rename, reorder, hide or take them away per role.
--
-- Idempotent like every script: DbMigrator runs all of them on every start. Once released, never edit this file; add a
-- new numbered script instead. No existing table changes.
--
-- Design (the same pattern as 012_member_platforms.sql):
--   * One permission per page, held by every role of every business model by default. The Site admin can take any of them
--     away per role in the permissions editor; the seed is INSERT ... ON CONFLICT DO NOTHING and never updates a grant, so a
--     restart does not undo that. Adding the grants raises the permission version once, so everybody signs in again once.
--   * The key shape (ck_permissions_key) allows an underscore only after the dot, so the keys are one word before it:
--     contentstudio.view, medialibrary.view, aistudio.view, calendar.view.
--   * The pages are seeded like every other menu page (ON CONFLICT DO NOTHING, then menu_apply_default_items()), in the
--     order of the Site admin's design: right after Dashboard (10) and before Workspaces (20). Not locked, so a restart never
--     brings back an item the Site admin deleted. The icons edit, image, sparkles and calendar are new names in MenuIcons
--     (the database only checks their shape).

INSERT INTO permissions (key, title, description, is_protected) VALUES
    ('contentstudio.view', 'Open Content Studio',
        'Open the Content Studio page, where content for the organization''s channels will be created.', false),
    ('medialibrary.view', 'Open Media Library',
        'Open the Media Library page, where the organization''s images and videos will be kept.', false),
    ('aistudio.view', 'Open AI Studio',
        'Open the AI Studio page, where content will be drafted and improved with AI.', false),
    ('calendar.view', 'Open Calendar',
        'Open the Calendar page, where scheduled content will be shown by date.', false)
ON CONFLICT (key) DO NOTHING;

-- Eight (model, role) slots by four keys. Every role holds all four by default.
INSERT INTO role_permissions (business_model, role, permission_key, is_granted)
SELECT s.business_model, s.role, k.key, true
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
        ('contentstudio.view'),
        ('medialibrary.view'),
        ('aistudio.view'),
        ('calendar.view')
     ) AS k (key)
ON CONFLICT (business_model, role, permission_key) DO NOTHING;

INSERT INTO menu_pages
    (app, key, path, title, permission_key, default_label, default_icon, default_sort_order, default_is_locked)
VALUES
    ('member', 'content-studio', '/content-studio', 'Content Studio', 'contentstudio.view', 'Content Studio', 'edit', 11, false),
    ('member', 'media-library', '/media-library', 'Media Library', 'medialibrary.view', 'Media Library', 'image', 12, false),
    ('member', 'ai-studio', '/ai-studio', 'AI Studio', 'aistudio.view', 'AI Studio', 'sparkles', 13, false),
    ('member', 'calendar', '/calendar', 'Calendar', 'calendar.view', 'Calendar', 'calendar', 14, false)
ON CONFLICT (app, key) DO NOTHING;

SELECT menu_apply_default_items();
