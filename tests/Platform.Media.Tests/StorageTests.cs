using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Platform.Media.Providers;
using Platform.Media.Settings;

namespace Platform.Media.Tests;

/// <summary>The storage on disk, in a folder of its own for each test: nothing a key can say reaches outside it.</summary>
public sealed class StorageTests : IDisposable
{
    private const string Key = "11111111111111111111111111111111/aaaaaaaabbbbccccddddeeeeeeeeeeee.png";
    private readonly string _content = Path.Combine(Path.GetTempPath(), "jmi-media-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _root;

    public StorageTests()
    {
        Directory.CreateDirectory(_content);
        _root = Path.Combine(_content, "files");
    }

    public void Dispose()
    {
        if (Directory.Exists(_content))
        {
            Directory.Delete(_content, recursive: true);
        }
    }

    private LocalDiskMediaStorage Storage(string rootPath = "")
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.ContentRootPath.Returns(_content);
        return new LocalDiskMediaStorage(Options.Create(new MediaOptions { Storage = { RootPath = rootPath.Length == 0 ? _root : rootPath } }), environment);
    }

    private static MemoryStream Bytes(string text) => new(System.Text.Encoding.UTF8.GetBytes(text));

    private static async Task<string> ReadAsync(Stream stream)
    {
        await using (stream)
        {
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }
    }

    [Fact]
    public async Task ASavedFile_IsReadBackWhole_AndSaveSaysHowManyBytesItWrote()
    {
        var storage = Storage();

        var written = await storage.SaveAsync(Key, Bytes("hello media"), CancellationToken.None);

        Assert.Equal(11, written);
        Assert.Equal("hello media", await ReadAsync((await storage.OpenReadAsync(Key, CancellationToken.None))!));
    }

    [Fact]
    public async Task TheFileIsInsideTheRoot_UnderItsOrganizationsFolder()
    {
        await Storage().SaveAsync(Key, Bytes("x"), CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(_root, "11111111111111111111111111111111", "aaaaaaaabbbbccccddddeeeeeeeeeeee.png")));
    }

    [Fact]
    public async Task NothingUnderTheKey_ReadsAsNull_AndDeletingItIsNotAnError()
    {
        var storage = Storage();

        Assert.Null(await storage.OpenReadAsync(Key, CancellationToken.None));
        await storage.DeleteAsync(Key, CancellationToken.None);
    }

    [Fact]
    public async Task ADeletedFile_IsGone()
    {
        var storage = Storage();
        await storage.SaveAsync(Key, Bytes("x"), CancellationToken.None);

        await storage.DeleteAsync(Key, CancellationToken.None);

        Assert.Null(await storage.OpenReadAsync(Key, CancellationToken.None));
    }

    [Fact]
    public async Task AKeyThatIsAlreadyThere_IsNotOverwritten_AndNoTemporaryFileIsLeft()
    {
        var storage = Storage();
        await storage.SaveAsync(Key, Bytes("first"), CancellationToken.None);

        await Assert.ThrowsAsync<IOException>(() => storage.SaveAsync(Key, Bytes("second"), CancellationToken.None));

        Assert.Equal("first", await ReadAsync((await storage.OpenReadAsync(Key, CancellationToken.None))!));
        Assert.Empty(Directory.GetFiles(_root, "*.part-*", SearchOption.AllDirectories));
    }

    private sealed class BrokenStream : Stream
    {
        private bool _first = true;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        // One byte arrives, then the connection is lost.
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_first)
            {
                _first = false;
                buffer.Span[0] = 1;
                return ValueTask.FromResult(1);
            }

            throw new IOException("The connection was lost.");
        }
    }

    [Fact]
    public async Task AnUploadThatBreaksHalfWay_LeavesNoFileAtAll()
    {
        var storage = Storage();

        await Assert.ThrowsAsync<IOException>(() => storage.SaveAsync(Key, new BrokenStream(), CancellationToken.None));

        Assert.Null(await storage.OpenReadAsync(Key, CancellationToken.None));
        var leftovers = Directory.Exists(_root) ? Directory.GetFiles(_root, "*", SearchOption.AllDirectories) : [];
        Assert.Empty(leftovers);
    }

    [Theory]
    [InlineData("../outside.png")]
    [InlineData("11111111111111111111111111111111/../../outside.png")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("11111111111111111111111111111111\\aaaaaaaabbbbccccddddeeeeeeeeeeee.png")]
    [InlineData("")]
    [InlineData("name.png")]
    public async Task AKeyOfAnyOtherShape_IsRefusedByEveryOperation_BeforeTheDiskIsTouched(string key)
    {
        var storage = Storage();

        await Assert.ThrowsAsync<ArgumentException>(() => storage.SaveAsync(key, Bytes("x"), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => storage.OpenReadAsync(key, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => storage.DeleteAsync(key, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(_content, "outside.png")));
    }

    [Fact]
    public void WithNoFolderConfigured_TheFilesGoUnderAppDataMediaInTheContentRoot()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.ContentRootPath.Returns(_content);

        var storage = new LocalDiskMediaStorage(Options.Create(new MediaOptions()), environment);

        Assert.Equal(Path.GetFullPath(Path.Combine(_content, "App_Data", "media")), storage.Root);
    }

    [Fact]
    public void ARelativeFolder_IsReadFromTheContentRoot_AndAnAbsoluteOneIsKept()
    {
        Assert.Equal(Path.Combine(_content, "uploads"), Storage("uploads").Root);
        Assert.Equal(_root, Storage(_root).Root);
    }
}
