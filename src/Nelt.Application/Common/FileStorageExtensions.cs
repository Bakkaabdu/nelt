using Nelt.Application.Abstractions;

namespace Nelt.Application.Common;

internal static class FileStorageExtensions
{
    /// <summary>
    /// Removes a file that was uploaded for a change the database then rejected, so failed saves do not leave
    /// orphaned files behind. Never throws: the original error is the one that matters.
    /// </summary>
    public static async Task DeleteQuietlyAsync(this IFileStorage storage, string? key)
    {
        if (key is null)
        {
            return;
        }

        try
        {
            await storage.DeleteAsync(key, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Best effort only.
        }
    }
}
