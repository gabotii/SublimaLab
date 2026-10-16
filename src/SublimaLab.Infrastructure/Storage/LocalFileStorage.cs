using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SublimaLab.Application.Abstractions;

namespace SublimaLab.Infrastructure.Storage;

public sealed class LocalStorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Directory for uploaded files. Relative paths are resolved against the content root.</summary>
    public string RootPath { get; set; } = "App_Data/uploads";
}

/// <summary>
/// Stores files on the local disk, outside the web root. Keys have a fixed, server-generated
/// shape (<c>yyyy/MM/{guid}.ext</c>) and are re-validated on every access, and the resolved path
/// must stay inside the root, so path traversal is impossible even with a tampered key.
/// Replace with a blob-storage implementation for multi-instance deployments.
/// </summary>
public sealed partial class LocalFileStorage : IFileStorage
{
    private static readonly HashSet<string> AllowedExtensions = [".png", ".jpg", ".webp"];

    private readonly string _root;
    private readonly TimeProvider _clock;

    public LocalFileStorage(IOptions<LocalStorageOptions> options, IHostEnvironment environment, TimeProvider clock)
    {
        _root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.RootPath));
        _clock = clock;
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken)
    {
        extension = extension.ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            throw new ArgumentException($"Extension '{extension}' is not allowed.", nameof(extension));
        }

        var now = _clock.GetUtcNow();
        var key = $"{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{extension}";
        var path = ResolvePath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(file, cancellationToken);
        return key;
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        var path = ResolvePath(key);
        Stream? stream = File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)
            : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        var path = ResolvePath(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    internal string ResolvePath(string key)
    {
        if (!KeyPattern().IsMatch(key))
        {
            throw new ArgumentException("Invalid storage key.", nameof(key));
        }

        var fullPath = Path.GetFullPath(Path.Combine(_root, key));
        if (!fullPath.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("Invalid storage key.", nameof(key));
        }

        return fullPath;
    }

    [GeneratedRegex(@"^\d{4}/\d{2}/[a-f0-9]{32}\.(png|jpg|webp)$")]
    private static partial Regex KeyPattern();
}
