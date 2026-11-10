using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Platform.Helpers.Constants;
using Platform.Helpers.Tenancy;
using Platform.Media.DTOs;
using Platform.Media.Interfaces;
using Platform.Media.Models;
using Platform.Media.Services;
using Platform.Media.Settings;

namespace Platform.Media.Tests.Support;

/// <summary>
/// The services of the Media Library with substitutes for the database and the disk, and a record of what they were asked to do, so
/// a test says only what it is about.
/// </summary>
public sealed class Fixture
{
    public static readonly Guid AgencyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid WorkspaceId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid FolderId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    public static readonly string[] Everything =
        [PermissionKeys.MediaLibraryView, PermissionKeys.MediaLibraryCreate, PermissionKeys.MediaLibraryDelete];

    public IMediaAssetRepository Assets { get; } = Substitute.For<IMediaAssetRepository>();
    public IMediaFolderRepository Folders { get; } = Substitute.For<IMediaFolderRepository>();
    public IMediaLookupRepository Lookups { get; } = Substitute.For<IMediaLookupRepository>();
    public IMediaStorage Storage { get; } = Substitute.For<IMediaStorage>();

    /// <summary>What the storage was given, by key.</summary>
    public Dictionary<string, byte[]> Stored { get; } = [];

    public MediaAsset? Inserted { get; private set; }
    public IReadOnlyList<string>? InsertedTags { get; private set; }
    public Guid? InsertedFolder { get; private set; }

    public Fixture()
    {
        Lookups.WorkspaceExistsAsync(default, default, default, default).ReturnsForAnyArgs(true);
        Folders.ExistsInWorkspaceAsync(default, default, default, default, default).ReturnsForAnyArgs(true);

        Storage.SaveAsync(default!, default!, default).ReturnsForAnyArgs(call => KeepAsync(call.ArgAt<string>(0), call.ArgAt<Stream>(1)));

        Assets.InsertAsync(default, default!, default!, default, default).ReturnsForAnyArgs(call =>
        {
            Inserted = call.ArgAt<MediaAsset>(1);
            InsertedTags = call.ArgAt<IReadOnlyList<string>>(2);
            InsertedFolder = call.ArgAt<Guid?>(3);
            return Task.CompletedTask;
        });

        Assets.GetAsync(default, default, default, default).ReturnsForAnyArgs(call =>
            Task.FromResult<MediaAssetDetail?>(Inserted is null || Inserted.Id != call.ArgAt<Guid>(2)
                ? null
                : new MediaAssetDetail
                {
                    Asset = new MediaAssetRow
                    {
                        Id = Inserted.Id,
                        WorkspaceId = Inserted.WorkspaceId,
                        FileName = Inserted.FileName,
                        MimeType = Inserted.MimeType,
                        MediaType = Inserted.MediaType,
                        FileSizeBytes = Inserted.FileSizeBytes,
                        Width = Inserted.Width,
                        Height = Inserted.Height,
                        CreatedAt = DateTimeOffset.UnixEpoch
                    },
                    Tags = (InsertedTags ?? []).Select(name => new MediaAssetTagRow { AssetId = Inserted.Id, TagId = Guid.NewGuid(), Name = name }).ToList(),
                    FolderIds = InsertedFolder is { } folder ? [folder] : []
                }));
    }

    private async Task<long> KeepAsync(string key, Stream content)
    {
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy);
        Stored[key] = copy.ToArray();
        return copy.Length;
    }

    public static TenantContext Tenant(params string[] permissions)
    {
        var tenant = new TenantContext();
        tenant.Set(AgencyId, UserId, "marketingmanager", permissions);
        return tenant;
    }

    public MediaAssetService AssetService(string[]? permissions = null, int maxMegabytes = 100) =>
        new(Assets, Folders, Lookups, Storage, Options.Create(new MediaOptions { MaxFileMegabytes = maxMegabytes }),
            Tenant(permissions ?? Everything), NullLogger<MediaAssetService>.Instance);

    public MediaFolderService FolderService(string[]? permissions = null) =>
        new(Folders, Lookups, Tenant(permissions ?? Everything), NullLogger<MediaFolderService>.Instance);

    // --- Files to upload -----------------------------------------------------------------------------

    /// <summary>A PNG with a real header of the given size, padded to <paramref name="length"/> bytes.</summary>
    public static byte[] Png(int width = 640, int height = 480, int length = 200)
    {
        var bytes = new byte[length];
        byte[] header = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R'];
        header.CopyTo(bytes, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);
        return bytes;
    }

    public static MediaUploadRequest Upload(
        byte[]? bytes = null, string? fileName = "photo.png", Guid? workspaceId = null, Guid? folderId = null, IReadOnlyList<string>? tags = null)
    {
        bytes ??= Png();
        return new MediaUploadRequest(workspaceId ?? WorkspaceId, folderId, tags, fileName, bytes.Length, new MemoryStream(bytes));
    }
}
