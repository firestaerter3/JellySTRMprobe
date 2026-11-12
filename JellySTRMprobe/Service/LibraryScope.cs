using System;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace JellySTRMprobe.Service;

/// <summary>
/// Decides whether an item belongs to the libraries selected in the plugin configuration.
/// </summary>
public static class LibraryScope
{
    /// <summary>
    /// Returns whether the item's path is a STRM file.
    /// </summary>
    /// <param name="item">The item to check.</param>
    /// <returns><c>true</c> when the item's path ends in <c>.strm</c>.</returns>
    public static bool IsStrm(BaseItem? item)
    {
        return item?.Path != null && item.Path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns whether the item lies in one of the selected libraries.
    /// </summary>
    /// <param name="libraryManager">The library manager used to find the item's libraries.</param>
    /// <param name="item">The item to check.</param>
    /// <param name="selectedLibraryIds">The selected library IDs. Empty means every library.</param>
    /// <returns><c>true</c> when no library is selected or the item is in a selected library.</returns>
    public static bool IsInSelectedLibraries(ILibraryManager libraryManager, BaseItem item, Guid[]? selectedLibraryIds)
    {
        if (selectedLibraryIds == null || selectedLibraryIds.Length == 0)
        {
            return true;
        }

        var folders = libraryManager.GetCollectionFolders(item);
        return folders.Any(folder => selectedLibraryIds.Contains(folder.Id));
    }
}
