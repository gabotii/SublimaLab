using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SublimaLab.Infrastructure.Storage;

namespace SublimaLab.UnitTests.Infrastructure;

public sealed class LocalFileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sublimalab-storage-" + Guid.NewGuid().ToString("N"));
    private readonly LocalFileStorage _storage;

    public LocalFileStorageTests()
    {
        _storage = new LocalFileStorage(Options.Create(new LocalStorageOptions { RootPath = _root }), new TestEnvironment(_root), TimeProvider.System);
    }

    [Fact]
    public async Task Saves_reads_and_deletes_files_under_generated_keys()
    {
        var key = await _storage.SaveAsync(new MemoryStream([1, 2, 3]), ".png", CancellationToken.None);

        Assert.Matches(@"^\d{4}/\d{2}/[a-f0-9]{32}\.png$", key);
        await using (var stream = await _storage.OpenReadAsync(key, CancellationToken.None))
        {
            Assert.NotNull(stream);
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy);
            Assert.Equal([1, 2, 3], copy.ToArray());
        }

        await _storage.DeleteAsync(key, CancellationToken.None);
        Assert.Null(await _storage.OpenReadAsync(key, CancellationToken.None));
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\Windows\\win.ini")]
    [InlineData("2026/10/../../../secret.png")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("2026/10/abc.exe")]
    public async Task Rejects_keys_that_do_not_match_the_generated_shape(string key)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _storage.OpenReadAsync(key, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => _storage.DeleteAsync(key, CancellationToken.None));
    }

    [Theory]
    [InlineData(".exe")]
    [InlineData(".svg")]
    [InlineData(".html")]
    public async Task Rejects_disallowed_extensions(string extension) =>
        await Assert.ThrowsAsync<ArgumentException>(() => _storage.SaveAsync(new MemoryStream([1]), extension, CancellationToken.None));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class TestEnvironment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
