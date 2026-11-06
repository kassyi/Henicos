namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>Finds the root of the checkout that holds a directory.</summary>
internal static class RepositoryRoot
{
    /// <summary>Walks up from <paramref name="start"/> to the first directory that has a <c>.git</c> entry (a folder, or a file in a worktree).</summary>
    /// <exception cref="InvalidOperationException">No directory above <paramref name="start"/> is a checkout.</exception>
    public static string Discover(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            var marker = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(marker) || File.Exists(marker))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"'{start}' is not inside a git checkout; pass --root.");
    }
}
