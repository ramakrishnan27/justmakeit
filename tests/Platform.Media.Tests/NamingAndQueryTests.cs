using Platform.Helpers.Exceptions;
using Platform.Helpers.Validation;
using Platform.Media.DTOs;
using Platform.Media.Models;
using Platform.Media.Services;
using Platform.Media.Settings;

namespace Platform.Media.Tests;

public sealed class NamingTests
{
    [Fact]
    public void StorageKeys_AreMadeFromIdsAndTheExtensionOnly()
    {
        var agency = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var asset = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        var key = StorageKeys.For(agency, asset, "png");

        Assert.Equal("11111111111111111111111111111111/aaaaaaaabbbbccccddddeeeeeeeeeeee.png", key);
        Assert.True(StorageKeys.IsValid(key));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("../etc/passwd")]
    [InlineData("11111111111111111111111111111111/../aaaaaaaabbbbccccddddeeeeeeeeeeee.png")]
    [InlineData("/11111111111111111111111111111111/aaaaaaaabbbbccccddddeeeeeeeeeeee.png")]
    [InlineData("11111111111111111111111111111111\\aaaaaaaabbbbccccddddeeeeeeeeeeee.png")]
    [InlineData("11111111111111111111111111111111/aaaaaaaabbbbccccddddeeeeeeeeeeee")]
    [InlineData("11111111111111111111111111111111/aaaaaaaabbbbccccddddeeeeeeeeeeee.PNG")]
    [InlineData("11111111111111111111111111111111/aaaaaaaabbbbccccddddeeeeeeeeeeee.png\n")]
    [InlineData("1111/aaaa.png")]
    [InlineData("11111111111111111111111111111111/aaaaaaaabbbbccccddddeeeeeeeeeeee.png/extra")]
    public void AnythingElse_IsNotAStorageKey(string? key) => Assert.False(StorageKeys.IsValid(key));

    [Fact]
    public void Tags_AreTrimmed_WithoutTheirHash_DeduplicatedInAnyCase_AndKeepTheirFirstSpelling()
    {
        var errors = new ValidationErrors();

        var tags = MediaNaming.NormalizeTags([" Summer ", "#summer", "SUMMER", "", "  ", "Beach Day", "#Launch"], errors);

        Assert.Equal(["Summer", "Beach Day", "Launch"], tags);
        Assert.False(errors.Any);
    }

    [Fact]
    public void NoTags_IsFine()
    {
        var errors = new ValidationErrors();

        Assert.Empty(MediaNaming.NormalizeTags(null, errors));
        Assert.False(errors.Any);
    }

    [Fact]
    public void ATagThatIsTooLongOrHasAControlCharacter_IsAnErrorOnTheTagsField()
    {
        var errors = new ValidationErrors();

        var tags = MediaNaming.NormalizeTags([new string('x', 51), "bad\ttag", "fine"], errors);

        Assert.Equal(["fine"], tags);
        Assert.Equal(2, errors.ToDictionary()["tags"].Length);
    }

    [Fact]
    public void MoreThanTenTags_IsAnError()
    {
        var errors = new ValidationErrors();

        MediaNaming.NormalizeTags(Enumerable.Range(1, 11).Select(i => "tag" + i), errors);

        Assert.Contains("tags", errors.ToDictionary().Keys);
    }

    [Fact]
    public void TenTags_AreAllowed()
    {
        var errors = new ValidationErrors();

        Assert.Equal(10, MediaNaming.NormalizeTags(Enumerable.Range(1, 10).Select(i => "tag" + i), errors).Count);
        Assert.False(errors.Any);
    }

    [Theory]
    [InlineData("Social Posts", "Social Posts")]
    [InlineData("  Blog images  ", "Blog images")]
    public void AFolderName_IsTrimmed(string raw, string expected)
    {
        var errors = new ValidationErrors();

        Assert.Equal(expected, MediaNaming.NormalizeFolderName(raw, errors));
        Assert.False(errors.Any);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("two\nlines")]
    public void AnEmptyOrMultiLineFolderName_IsAnErrorOnTheNameField(string? raw)
    {
        var errors = new ValidationErrors();

        Assert.Null(MediaNaming.NormalizeFolderName(raw, errors));
        Assert.Contains("name", errors.ToDictionary().Keys);
    }

    [Fact]
    public void AFolderNameOfAHundredCharactersIsFine_AndOneMoreIsNot()
    {
        var errors = new ValidationErrors();
        Assert.NotNull(MediaNaming.NormalizeFolderName(new string('f', 100), errors));
        Assert.False(errors.Any);

        Assert.Null(MediaNaming.NormalizeFolderName(new string('f', 101), errors));
        Assert.True(errors.Any);
    }
}

public sealed class ListQueryTests
{
    private static readonly Guid Workspace = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static Dictionary<string, string[]> Problems(MediaListQuery query) => query.Validate().ToDictionary().ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public void AQueryWithAWorkspace_IsValid_AndDefaultsToTheNewestFirst()
    {
        var query = new MediaListQuery(WorkspaceId: Workspace);

        Assert.Empty(Problems(query));
        var criteria = query.ToCriteria();
        Assert.Equal(Workspace, criteria.WorkspaceId);
        Assert.Equal(MediaSort.Newest, criteria.Sort);
        Assert.Equal(1, criteria.Page);
        Assert.Equal(12, criteria.PageSize);
        Assert.Null(criteria.Type);
        Assert.Null(criteria.Search);
        Assert.Null(criteria.MinSizeBytes);
        Assert.Null(criteria.MaxSizeBytes);
    }

    [Fact]
    public void TheWorkspace_IsRequired()
    {
        Assert.Contains("workspaceId", Problems(new MediaListQuery()).Keys);
        Assert.Contains("workspaceId", Problems(new MediaListQuery(WorkspaceId: Guid.Empty)).Keys);
    }

    [Theory]
    [InlineData("image", true)]
    [InlineData("video", true)]
    [InlineData("document", true)]
    [InlineData("other", true)]
    [InlineData("Image", false)]
    [InlineData("audio", false)]
    [InlineData("0", false)]
    public void TheTypeIsOneOfTheFourTabs_InLowerCase(string type, bool valid) =>
        Assert.Equal(valid, !Problems(new MediaListQuery(Workspace, Type: type)).ContainsKey("type"));

    [Fact]
    public void AFolderAndUncategorized_CannotBeAskedForTogether() =>
        Assert.Contains("folderId", Problems(new MediaListQuery(Workspace, FolderId: Guid.NewGuid(), Uncategorized: true)).Keys);

    [Theory]
    [InlineData("small", true)]
    [InlineData("medium", true)]
    [InlineData("large", true)]
    [InlineData("huge", false)]
    public void TheSizeIsOneOfThree(string size, bool valid) =>
        Assert.Equal(valid, !Problems(new MediaListQuery(Workspace, Size: size)).ContainsKey("size"));

    [Theory]
    [InlineData("newest", true)]
    [InlineData("oldest", true)]
    [InlineData("name", true)]
    [InlineData("size", true)]
    [InlineData("random", false)]
    [InlineData("name; DROP TABLE media_assets", false)]
    public void TheSortIsOneOfFour_SoNoTextOfTheCallersReachesTheOrderBy(string sort, bool valid) =>
        Assert.Equal(valid, !Problems(new MediaListQuery(Workspace, Sort: sort)).ContainsKey("sort"));

    [Theory]
    [InlineData(0, 12, false)]
    [InlineData(1, 0, false)]
    [InlineData(1, 61, false)]
    [InlineData(1, 60, true)]
    [InlineData(5, 1, true)]
    public void PagingHasLimits(int page, int pageSize, bool valid) =>
        Assert.Equal(valid, Problems(new MediaListQuery(Workspace, Page: page, PageSize: pageSize)).Count == 0);

    [Fact]
    public void TheSearchIsLimitedTo100Characters()
    {
        Assert.Empty(Problems(new MediaListQuery(Workspace, Q: new string('q', 100))));
        Assert.Contains("q", Problems(new MediaListQuery(Workspace, Q: new string('q', 101))).Keys);
    }

    [Fact]
    public void TheSizesBecomeByteRanges_AndASearchIsTrimmed()
    {
        const long mb = 1024 * 1024;

        var small = new MediaListQuery(Workspace, Size: "small").ToCriteria();
        Assert.Null(small.MinSizeBytes);
        Assert.Equal(mb, small.MaxSizeBytes);

        var medium = new MediaListQuery(Workspace, Size: "medium").ToCriteria();
        Assert.Equal(mb, medium.MinSizeBytes);
        Assert.Equal(10 * mb, medium.MaxSizeBytes);

        var large = new MediaListQuery(Workspace, Size: "large").ToCriteria();
        Assert.Equal(10 * mb, large.MinSizeBytes);
        Assert.Null(large.MaxSizeBytes);

        Assert.Equal("beach", new MediaListQuery(Workspace, Q: "  beach ").ToCriteria().Search);
        Assert.Null(new MediaListQuery(Workspace, Q: "   ").ToCriteria().Search);
    }

    [Fact]
    public void TheSortAndTypeAreCarriedOver()
    {
        var criteria = new MediaListQuery(Workspace, Type: "video", Sort: "size", TagId: Guid.Parse("44444444-4444-4444-4444-444444444444")).ToCriteria();

        Assert.Equal(MediaType.Video, criteria.Type);
        Assert.Equal(MediaSort.Size, criteria.Sort);
        Assert.Equal(Guid.Parse("44444444-4444-4444-4444-444444444444"), criteria.TagId);
    }
}

public sealed class OptionsValidatorTests
{
    private static readonly MediaOptionsValidator Validator = new();

    [Fact]
    public void TheDefaults_AreValid() => Assert.True(Validator.Validate(null, new MediaOptions()).Succeeded);

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1025)]
    public void ALargestFileOutsideOneAndAThousandMegabytes_IsRefused(int megabytes)
    {
        var result = Validator.Validate(null, new MediaOptions { MaxFileMegabytes = megabytes });

        Assert.True(result.Failed);
        Assert.Contains("Media:MaxFileMegabytes", string.Join(' ', result.Failures!));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1024)]
    public void TheLimitsThemselvesAreFine(int megabytes) =>
        Assert.True(Validator.Validate(null, new MediaOptions { MaxFileMegabytes = megabytes }).Succeeded);

    [Fact]
    public void ABlankStorageFolder_IsRefused_AndNoneMeansTheDefault()
    {
        Assert.True(Validator.Validate(null, new MediaOptions { Storage = { RootPath = "   " } }).Failed);
        Assert.True(Validator.Validate(null, new MediaOptions { Storage = { RootPath = string.Empty } }).Succeeded);
        Assert.True(Validator.Validate(null, new MediaOptions { Storage = { RootPath = "D:\\files\\media" } }).Succeeded);
    }

    [Fact]
    public void TheLimitInBytes_IsTheLimitInMegabytes() =>
        Assert.Equal(100L * 1024 * 1024, new MediaOptions().MaxFileBytes);

    [Fact]
    public void ThrowIfAny_OnAFailedQuery_IsAValidationError()
    {
        var exception = Assert.Throws<BadRequestException>(() => new MediaListQuery().Validate().ThrowIfAny());

        Assert.Equal(400, exception.StatusCode);
    }
}
