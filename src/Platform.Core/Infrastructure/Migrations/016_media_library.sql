-- 016_media_library.sql
-- The Media Library: files (images, videos, documents, audio) kept per workspace, the folders and tags that organize them, the
-- collections, the variants (thumbnails) and the places a file is used, and the record of an upload. Content Studio and the other
-- features reach a file through the contract in Platform.Core (IMediaAssetCatalog); they never read these tables themselves.
--
-- Idempotent like every script: DbMigrator runs all of them on every start. Once released, never edit this file; add a new
-- numbered script instead. (This script took 016; the email script is now 017_email.sql.)
--
-- Design:
--   * Every table is a tenant table: agency_id NOT NULL -> agencies ON DELETE CASCADE, row-level security enabled and forced, one
--     policy tenant_isolation (USING and WITH CHECK on agency_id = app_agency_id()). The diagram of the brief shows workspace_id on
--     the main tables; that stays, and agency_id is added beside it as on every tenant table.
--   * A child row carries the agency_id of its parent and the foreign key is COMPOSITE (parent_id, agency_id), so a row can never
--     point at another organization's file, folder or tag: foreign key checks skip row-level security, and without this a bug
--     could attach a row of organization A to a file of organization B. The parents therefore have UNIQUE (id, agency_id).
--     workspaces has no such key (an existing table, left alone), so workspace_id is a plain foreign key and the service reads
--     the workspace through its own (row-level-security-filtered) query, as Content Studio does.
--   * What the diagram does not say, decided here: the file's bytes are NOT in the database. media_assets.storage_key names where
--     the file is kept (a folder on disk now; the storage behind IMediaStorage can change). Only the key's shape is checked here
--     (no "..", no leading slash), as a second line of defence behind the service, which makes the key itself and never uses
--     anything the person typed.
--   * A file can be in several folders (media_folder_assets has a two-column key, as in the diagram), so "Copy to a folder" adds
--     a membership and copies no bytes, and "Move" replaces the memberships. A file in no folder is "Uncategorized".
--     Deleting a folder deletes the folder (and its sub-folders); the files stay.
--   * People columns (uploaded_by, created_by) are NULLABLE with ON DELETE SET NULL: removing a team member whose last membership
--     ends deletes their users row, and the organization's files must stay.
--   * A file that something uses cannot be deleted: media_asset_usages and content_post_media point at media_assets with the
--     default NO ACTION rule (checked at the end of the statement, not RESTRICT's immediate check, so that deleting a whole
--     organization, which cascades to the files and to their users in an order of PostgreSQL's choosing, can still finish).
--   * Status and type columns are text + CHECK with lower-case values. The three columns that 014_content_studio.sql left as plain
--     uuid for this table get their foreign keys at the end of this script (as the header of 014 says this script must).
--   * Not in the diagram, kept out on purpose: the uploader's name (users is not readable across rows), a tag colour (the web
--     client derives one from the name), a checksum (a duplicate check would need an index nothing uses yet).

-- ---------------------------------------------------------------------------------------------
-- Trigger function: keep updated_at honest whatever the code path.
-- ---------------------------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION media_set_updated_at()
    RETURNS trigger
    LANGUAGE plpgsql
    SET search_path = public, pg_temp
AS $$
BEGIN
    NEW.updated_at := now();
    RETURN NEW;
END
$$;

-- ---------------------------------------------------------------------------------------------
-- Permissions and their default grants
-- ---------------------------------------------------------------------------------------------
-- medialibrary.view (013) opens the page and reads. These two change things.
INSERT INTO permissions (key, title, description, is_protected) VALUES
    ('medialibrary.create', 'Upload media',
        'Upload files to the Media Library, create folders and put files in them.', false),
    ('medialibrary.delete', 'Delete media',
        'Delete files from the Media Library.', false)
ON CONFLICT (key) DO NOTHING;

-- Eight (model, role) slots by two keys. Uploading: every role that changes data (organization admin, marketing manager,
-- content writer). Deleting: organization admins and marketing managers, and the content writer of an individual creator, who works
-- alone; a content writer in a company or an agency adds files for others, and does not take them away. The Site admin can change
-- any of it per role; the seed never updates a grant, so a restart does not undo that. Seeding raises the permission version once,
-- so everybody signs in again once.
INSERT INTO role_permissions (business_model, role, permission_key, is_granted)
SELECT s.business_model,
       s.role,
       k.key,
       CASE k.key
           WHEN 'medialibrary.create' THEN s.role IN ('organizationadmin', 'marketingmanager', 'contentwriter')
           ELSE s.role IN ('organizationadmin', 'marketingmanager') OR (s.business_model = 'creator' AND s.role = 'contentwriter')
       END
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
        ('medialibrary.create'),
        ('medialibrary.delete')
     ) AS k (key)
ON CONFLICT (business_model, role, permission_key) DO NOTHING;

-- ---------------------------------------------------------------------------------------------
-- media_folders
-- ---------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS media_folders (
    id               uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    agency_id        uuid        NOT NULL REFERENCES agencies (id) ON DELETE CASCADE,
    workspace_id     uuid        NOT NULL REFERENCES workspaces (id) ON DELETE CASCADE,
    parent_folder_id uuid        NULL,
    name             text        NOT NULL,
    created_by       uuid        NULL REFERENCES users (id) ON DELETE SET NULL,
    created_at       timestamptz NOT NULL DEFAULT now(),
    updated_at       timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_media_folders_id_agency UNIQUE (id, agency_id),
    -- A NULL parent is a top-level folder: the key is not checked when one of its columns is NULL.
    CONSTRAINT fk_media_folders_parent FOREIGN KEY (parent_folder_id, agency_id)
        REFERENCES media_folders (id, agency_id) ON DELETE CASCADE,
    CONSTRAINT ck_media_folders_name CHECK (char_length(name) BETWEEN 1 AND 100 AND name = btrim(name) AND name !~ '[[:cntrl:]]'),
    CONSTRAINT ck_media_folders_parent CHECK (parent_folder_id IS DISTINCT FROM id)
);

CREATE INDEX IF NOT EXISTS ix_media_folders_agency ON media_folders (agency_id);
CREATE INDEX IF NOT EXISTS ix_media_folders_workspace ON media_folders (workspace_id);
CREATE INDEX IF NOT EXISTS ix_media_folders_parent ON media_folders (parent_folder_id) WHERE parent_folder_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_media_folders_created_by ON media_folders (created_by) WHERE created_by IS NOT NULL;
-- A name identifies one folder under one parent in a workspace, in any letter case. The zero uuid stands for "no parent", because
-- a unique index treats two NULLs as different.
CREATE UNIQUE INDEX IF NOT EXISTS ux_media_folders_name
    ON media_folders (workspace_id, coalesce(parent_folder_id, '00000000-0000-0000-0000-000000000000'::uuid), lower(name));

-- ---------------------------------------------------------------------------------------------
-- media_assets: one row per file
-- ---------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS media_assets (
    id               uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    agency_id        uuid        NOT NULL REFERENCES agencies (id) ON DELETE CASCADE,
    workspace_id     uuid        NOT NULL REFERENCES workspaces (id) ON DELETE CASCADE,
    uploaded_by      uuid        NULL REFERENCES users (id) ON DELETE SET NULL,
    file_name        text        NOT NULL,
    mime_type        text        NOT NULL,
    media_type       text        NOT NULL,
    file_size_bytes  bigint      NOT NULL,
    storage_key      text        NOT NULL,
    -- What was read from the file itself (an image's width and height). Never what the browser claimed.
    metadata         jsonb       NOT NULL DEFAULT '{}',
    created_at       timestamptz NOT NULL DEFAULT now(),
    updated_at       timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_media_assets_id_agency UNIQUE (id, agency_id),
    CONSTRAINT uq_media_assets_storage_key UNIQUE (storage_key),
    CONSTRAINT ck_media_assets_type CHECK (media_type IN ('image', 'video', 'document', 'other')),
    CONSTRAINT ck_media_assets_file_name CHECK (char_length(file_name) BETWEEN 1 AND 255 AND file_name !~ '[[:cntrl:]]'),
    CONSTRAINT ck_media_assets_mime CHECK (length(mime_type) <= 100 AND mime_type ~ '^[a-z]+/[a-z0-9.+-]+$'),
    CONSTRAINT ck_media_assets_size CHECK (file_size_bytes > 0 AND file_size_bytes <= 2147483648),
    CONSTRAINT ck_media_assets_storage_key CHECK (
        length(storage_key) <= 300 AND storage_key ~ '^[A-Za-z0-9][A-Za-z0-9/_.-]*$' AND storage_key !~ '\.\.'),
    CONSTRAINT ck_media_assets_metadata CHECK (jsonb_typeof(metadata) = 'object')
);

CREATE INDEX IF NOT EXISTS ix_media_assets_agency ON media_assets (agency_id);
-- The list: newest first in a workspace, narrowed by the tab (type).
CREATE INDEX IF NOT EXISTS ix_media_assets_workspace_created ON media_assets (workspace_id, created_at DESC, id);
CREATE INDEX IF NOT EXISTS ix_media_assets_workspace_type ON media_assets (workspace_id, media_type, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_media_assets_uploaded_by ON media_assets (uploaded_by) WHERE uploaded_by IS NOT NULL;

-- ---------------------------------------------------------------------------------------------
-- media_tags and media_asset_tags
-- ---------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS media_tags (
    id           uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    agency_id    uuid        NOT NULL REFERENCES agencies (id) ON DELETE CASCADE,
    workspace_id uuid        NOT NULL REFERENCES workspaces (id) ON DELETE CASCADE,
    name         text        NOT NULL,
    created_at   timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_media_tags_id_agency UNIQUE (id, agency_id),
    CONSTRAINT ck_media_tags_name CHECK (char_length(name) BETWEEN 1 AND 50 AND name = btrim(name) AND name !~ '[[:cntrl:]]')
);

CREATE INDEX IF NOT EXISTS ix_media_tags_agency ON media_tags (agency_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_media_tags_name ON media_tags (workspace_id, lower(name));

CREATE TABLE IF NOT EXISTS media_asset_tags (
    agency_id       uuid        NOT NULL REFERENCES agencies (id) ON DELETE CASCADE,
    media_asset_id  uuid        NOT NULL,
    tag_id          uuid        NOT NULL,
    created_at      timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_media_asset_tags PRIMARY KEY (media_asset_id, tag_id),
    CONSTRAINT fk_media_asset_tags_asset FOREIGN KEY (media_asset_id, agency_id)
        REFERENCES media_assets (id, agency_id) ON DELETE CASCADE,
    CONSTRAINT fk_media_asset_tags_tag FOREIGN KEY (tag_id, agency_id)
        REFERENCES media_tags (id, agency_id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_media_asset_tags_agency ON media_asset_tags (agency_id);
CREATE INDEX IF NOT EXISTS ix_media_asset_tags_tag ON media_asset_tags (tag_id);

-- ---------------------------------------------------------------------------------------------
-- media_folder_assets: which files are in which folder (a file may be in several)
-- ---------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS media_folder_assets (
    agency_id       uuid        NOT NULL REFERENCES agencies (id) ON DELETE CASCADE,
    folder_id       uuid        NOT NULL,
    media_asset_id  uuid        NOT NULL,
    created_at      timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_media_folder_assets PRIMARY KEY (folder_id, media_asset_id),
    CONSTRAINT fk_media_folder_assets_folder FOREIGN KEY (folder_id, agency_id)
        REFERENCES media_folders (id, agency_id) ON DELETE CASCADE,
    CONSTRAINT fk_media_folder_assets_asset FOREIGN KEY (media_asset_id, agency_id)
        REFERENCES media_assets (id, agency_id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_media_folder_assets_agency ON media_folder_assets (agency_id);
CREATE INDEX IF NOT EXISTS ix_media_folder_assets_asset ON media_folder_assets (media_asset_id);

-- ---------------------------------------------------------------------------------------------
-- media_collections and media_collection_assets: hand-picked, ordered sets of files (no page uses them yet)
-- ---------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS media_collections (
    id             uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    agency_id      uuid        NOT NULL REFERENCES agencies (id) ON DELETE CASCADE,
    workspace_id   uuid        NOT NULL REFERENCES workspaces (id) ON DELETE CASCADE,
    name           text        NOT NULL,
    cover_asset_id uuid        NULL,
    created_by     uuid        NULL REFERENCES users (id) ON DELETE SET NULL,
    created_at     timestamptz NOT NULL DEFAULT now(),
    updated_at     timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_media_collections_id_agency UNIQUE (id, agency_id),
    -- Deleting the cover file clears the cover and nothing else (agency_id is part of the key and must stay).
    CONSTRAINT fk_media_collections_cover FOREIGN KEY (cover_asset_id, agency_id)
        REFERENCES media_assets (id, agency_id) ON DELETE SET NULL (cover_asset_id),
    CONSTRAINT ck_media_collections_name CHECK (char_length(name) BETWEEN 1 AND 100 AND name = btrim(name) AND name !~ '[[:cntrl:]]')
);

CREATE INDEX IF NOT EXISTS ix_media_collections_agency ON media_collections (agency_id);
CREATE INDEX IF NOT EXISTS ix_media_collections_workspace ON media_collections (workspace_id);
CREATE INDEX IF NOT EXISTS ix_media_collections_cover ON media_collections (cover_asset_id) WHERE cover_asset_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_media_collections_created_by ON media_collections (created_by) WHERE created_by IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_media_collections_name ON media_collections (workspace_id, lower(name));

CREATE TABLE IF NOT EXISTS media_collection_assets (
    agency_id       uuid        NOT NULL REFERENCES agencies (id) ON DELETE CASCADE,
    collection_id   uuid        NOT NULL,
    media_asset_id  uuid        NOT NULL,
    display_order   integer     NOT NULL DEFAULT 0,
    created_at      timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_media_collection_assets PRIMARY KEY (collection_id, media_asset_id),
    CONSTRAINT fk_media_collection_assets_collection FOREIGN KEY (collection_id, agency_id)
        REFERENCES media_collections (id, agency_id) ON DELETE CASCADE,
    CONSTRAINT fk_media_collection_assets_asset FOREIGN KEY (media_asset_id, agency_id)
        REFERENCES media_assets (id, agency_id) ON DELETE CASCADE,
    CONSTRAINT ck_media_collection_assets_order CHECK (display_order >= 0)
);

CREATE INDEX IF NOT EXISTS ix_media_collection_assets_agency ON media_collection_assets (agency_id);
CREATE INDEX IF NOT EXISTS ix_media_collection_assets_asset ON media_collection_assets (media_asset_id);
CREATE INDEX IF NOT EXISTS ix_media_collection_assets_order ON media_collection_assets (collection_id, display_order);

-- ---------------------------------------------------------------------------------------------
-- media_asset_variants: other renditions of a file (a thumbnail, a poster frame). Nothing makes them yet: producing one needs an
-- image library or a video tool, and none is on the list of allowed packages.
-- ---------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS media_asset_variants (
    id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    agency_id       uuid        NOT NULL REFERENCES agencies (id) ON DELETE CASCADE,
    media_asset_id  uuid        NOT NULL,
    variant_type    text        NOT NULL,
    storage_key     text        NOT NULL,
    created_at      timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT fk_media_asset_variants_asset FOREIGN KEY (media_asset_id, agency_id)
        REFERENCES media_assets (id, agency_id) ON DELETE CASCADE,
    CONSTRAINT uq_media_asset_variants UNIQUE (media_asset_id, variant_type),
    CONSTRAINT uq_media_asset_variants_storage_key UNIQUE (storage_key),
    CONSTRAINT ck_media_asset_variants_type CHECK (variant_type IN ('thumbnail', 'small', 'medium', 'large', 'poster', 'preview')),
    CONSTRAINT ck_media_asset_variants_storage_key CHECK (
        length(storage_key) <= 300 AND storage_key ~ '^[A-Za-z0-9][A-Za-z0-9/_.-]*$' AND storage_key !~ '\.\.')
);

CREATE INDEX IF NOT EXISTS ix_media_asset_variants_agency ON media_asset_variants (agency_id);

-- ---------------------------------------------------------------------------------------------
-- media_asset_usages: where a file is used (module, kind of thing, which one). A file with a usage cannot be deleted.
-- ---------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS media_asset_usages (
    id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    agency_id       uuid        NOT NULL REFERENCES agencies (id) ON DELETE CASCADE,
    workspace_id    uuid        NOT NULL REFERENCES workspaces (id) ON DELETE CASCADE,
    media_asset_id  uuid        NOT NULL,
    module_name     text        NOT NULL,
    entity_type     text        NOT NULL,
    entity_id       uuid        NOT NULL,
    created_at      timestamptz NOT NULL DEFAULT now(),
    -- No ON DELETE rule: a file that is in use is not deleted. The owner of the usage removes it when the thing lets go of the file.
    CONSTRAINT fk_media_asset_usages_asset FOREIGN KEY (media_asset_id, agency_id)
        REFERENCES media_assets (id, agency_id),
    CONSTRAINT uq_media_asset_usages UNIQUE (media_asset_id, module_name, entity_type, entity_id),
    CONSTRAINT ck_media_asset_usages_module CHECK (module_name ~ '^[a-z][a-z0-9_]{0,49}$'),
    CONSTRAINT ck_media_asset_usages_entity_type CHECK (entity_type ~ '^[a-z][a-z0-9_]{0,49}$')
);

CREATE INDEX IF NOT EXISTS ix_media_asset_usages_agency ON media_asset_usages (agency_id);
CREATE INDEX IF NOT EXISTS ix_media_asset_usages_workspace ON media_asset_usages (workspace_id);
CREATE INDEX IF NOT EXISTS ix_media_asset_usages_entity ON media_asset_usages (module_name, entity_type, entity_id);

-- ---------------------------------------------------------------------------------------------
-- media_upload_sessions: the record of an upload, for uploads in pieces and for finding out what became of one. (A file is sent in
-- one request today and the service does not write a row yet.)
-- ---------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS media_upload_sessions (
    id                 uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    agency_id          uuid        NOT NULL REFERENCES agencies (id) ON DELETE CASCADE,
    workspace_id       uuid        NOT NULL REFERENCES workspaces (id) ON DELETE CASCADE,
    created_by         uuid        NULL REFERENCES users (id) ON DELETE SET NULL,
    file_name          text        NOT NULL,
    file_size_bytes    bigint      NULL,
    status             text        NOT NULL DEFAULT 'pending',
    completed_asset_id uuid        NULL,
    error_code         text        NULL,
    created_at         timestamptz NOT NULL DEFAULT now(),
    completed_at       timestamptz NULL,
    CONSTRAINT fk_media_upload_sessions_asset FOREIGN KEY (completed_asset_id, agency_id)
        REFERENCES media_assets (id, agency_id) ON DELETE SET NULL (completed_asset_id),
    CONSTRAINT ck_media_upload_sessions_status CHECK (status IN ('pending', 'uploading', 'completed', 'failed', 'cancelled')),
    CONSTRAINT ck_media_upload_sessions_file_name CHECK (char_length(file_name) BETWEEN 1 AND 255 AND file_name !~ '[[:cntrl:]]'),
    CONSTRAINT ck_media_upload_sessions_size CHECK (file_size_bytes IS NULL OR file_size_bytes >= 0),
    -- Only a completed upload names a file (the file may be deleted later, which clears it, so the reverse is not required).
    CONSTRAINT ck_media_upload_sessions_asset CHECK (completed_asset_id IS NULL OR status = 'completed'),
    CONSTRAINT ck_media_upload_sessions_error CHECK (error_code IS NULL OR length(error_code) <= 100)
);

CREATE INDEX IF NOT EXISTS ix_media_upload_sessions_agency ON media_upload_sessions (agency_id);
CREATE INDEX IF NOT EXISTS ix_media_upload_sessions_workspace ON media_upload_sessions (workspace_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_media_upload_sessions_asset ON media_upload_sessions (completed_asset_id) WHERE completed_asset_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_media_upload_sessions_created_by ON media_upload_sessions (created_by) WHERE created_by IS NOT NULL;

-- ---------------------------------------------------------------------------------------------
-- Row-level security: enabled and forced on every table, one policy: the current organization only.
-- ---------------------------------------------------------------------------------------------
DO $$
DECLARE
    t text;
BEGIN
    FOREACH t IN ARRAY ARRAY[
        'media_folders', 'media_assets', 'media_tags', 'media_asset_tags', 'media_folder_assets', 'media_collections',
        'media_collection_assets', 'media_asset_variants', 'media_asset_usages', 'media_upload_sessions']
    LOOP
        EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY', t);
        EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY', t);
        EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON %I', t);
        EXECUTE format(
            'CREATE POLICY tenant_isolation ON %I USING (agency_id = app_agency_id()) WITH CHECK (agency_id = app_agency_id())', t);
    END LOOP;
END
$$;

-- ---------------------------------------------------------------------------------------------
-- Triggers
-- ---------------------------------------------------------------------------------------------
DROP TRIGGER IF EXISTS trg_media_assets_updated_at ON media_assets;
CREATE TRIGGER trg_media_assets_updated_at
    BEFORE UPDATE ON media_assets
    FOR EACH ROW EXECUTE FUNCTION media_set_updated_at();

DROP TRIGGER IF EXISTS trg_media_folders_updated_at ON media_folders;
CREATE TRIGGER trg_media_folders_updated_at
    BEFORE UPDATE ON media_folders
    FOR EACH ROW EXECUTE FUNCTION media_set_updated_at();

DROP TRIGGER IF EXISTS trg_media_collections_updated_at ON media_collections;
CREATE TRIGGER trg_media_collections_updated_at
    BEFORE UPDATE ON media_collections
    FOR EACH ROW EXECUTE FUNCTION media_set_updated_at();

-- ---------------------------------------------------------------------------------------------
-- The foreign keys 014_content_studio.sql could not have (the table was not there). Composite, so a post of organization A can never
-- use a file of organization B. Added once: a constraint that exists is left alone. These tables are empty until Content Studio
-- can attach media, so adding the keys cannot fail on existing rows.
--   content_post_media.media_asset_id      a file that a post uses cannot be deleted (no ON DELETE rule, see above)
--   content_templates.preview_asset_id     deleting the file clears the preview and nothing else
--   content_ai_generations.output_media_id deleting the file clears the output link and nothing else
-- ---------------------------------------------------------------------------------------------
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_content_post_media_asset' AND conrelid = 'content_post_media'::regclass) THEN
        ALTER TABLE content_post_media ADD CONSTRAINT fk_content_post_media_asset
            FOREIGN KEY (media_asset_id, agency_id) REFERENCES media_assets (id, agency_id);
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_content_templates_preview_asset' AND conrelid = 'content_templates'::regclass) THEN
        ALTER TABLE content_templates ADD CONSTRAINT fk_content_templates_preview_asset
            FOREIGN KEY (preview_asset_id, agency_id) REFERENCES media_assets (id, agency_id) ON DELETE SET NULL (preview_asset_id);
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_content_ai_generations_output_media' AND conrelid = 'content_ai_generations'::regclass) THEN
        ALTER TABLE content_ai_generations ADD CONSTRAINT fk_content_ai_generations_output_media
            FOREIGN KEY (output_media_id, agency_id) REFERENCES media_assets (id, agency_id) ON DELETE SET NULL (output_media_id);
    END IF;
END
$$;
