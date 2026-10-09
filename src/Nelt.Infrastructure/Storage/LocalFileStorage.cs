using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Nelt.Application.Abstractions;

namespace Nelt.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>Absolute path, or relative to the content root. Must be outside wwwroot: files are served through authorization checks.</summary>
    public string RootPath { get; set; } = "App_Data/storage";
}

/// <summary>
/// Disk-backed storage. Writes go to a temp file first and are moved into place, so readers never see partial files.
/// For multiple web nodes, point <see cref="StorageOptions.RootPath"/> at shared storage or swap in a blob implementation.
/// </summary>
public sealed partial class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalFileStorage(IOptions<StorageOptions> options, Microsoft.Extensions.Hosting.IHostEnvironment environment)
    {
        var configured = options.Value.RootPath;
        _root = Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(environment.ContentRootPath, configured));
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(Stream content, string folder, string extension, CancellationToken cancellationToken = default)
    {
        if (!FolderPattern().IsMatch(folder) || !ExtensionPattern().IsMatch(extension))
        {
            throw new ArgumentException("Invalid storage folder or extension.");
        }

        var now = DateTime.UtcNow;
        var key = $"{folder}/{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var target = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        var temp = target + ".uploading";
        try
        {
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await content.CopyToAsync(output, cancellationToken);
            }

            File.Move(temp, target);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }

        return key;
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!KeyPattern().IsMatch(key))
        {
            return Task.FromResult<Stream?>(null);
        }

        var path = Resolve(key);
        if (!File.Exists(path))
        {
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        if (KeyPattern().IsMatch(key))
        {
            TryDelete(Resolve(key));
        }

        return Task.CompletedTask;
    }

    private string Resolve(string key)
    {
        var path = Path.GetFullPath(Path.Combine(_root, key));
        if (!path.StartsWith(_root, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Resolved path escapes the storage root.");
        }

        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best effort: an orphaned file is harmless and can be cleaned up later.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [GeneratedRegex("^[a-z]{3,20}$")]
    private static partial Regex FolderPattern();

    [GeneratedRegex(@"^\.[a-zA-Z0-9]{1,5}$")]
    private static partial Regex ExtensionPattern();

    [GeneratedRegex(@"^[a-z]{3,20}/\d{4}/\d{2}/[0-9a-f]{32}\.[a-z0-9]{1,5}$")]
    private static partial Regex KeyPattern();
}
