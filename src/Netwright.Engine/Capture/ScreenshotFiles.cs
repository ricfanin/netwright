namespace Netwright.Engine.Capture;

/// <summary>
/// Where screenshots may be saved. Only paths inside the directory the server was started with are
/// allowed, so saving a screenshot can never become an arbitrary file write.
/// </summary>
public static class ScreenshotFiles
{
    private static readonly HashSet<string> DeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",

        // Windows also reserves COM and LPT with the superscript digits ¹ ² ³.
        "COM¹", "COM²", "COM³", "LPT¹", "LPT²", "LPT³",
    };

    /// <summary>Resolves <paramref name="path"/> (relative to <paramref name="directory"/>, or absolute inside it) to a full .png path.</summary>
    public static string Resolve(string? directory, string path)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw NetwrightException.InvalidArgument(
                "Saving screenshots is off: the server has no screenshot directory.",
                "Ask the User to start Netwright with --screenshot-dir <directory>, or take the screenshot without path.");
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw NetwrightException.InvalidArgument("path is empty.");
        }

        var root = Root(directory);
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        string full;
        try
        {
            full = Path.GetFullPath(Path.Combine(root, path.Trim()));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw NetwrightException.InvalidArgument($"'{path}' is not a valid file path.");
        }

        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw NetwrightException.InvalidArgument($"'{path}' is outside the screenshot directory {root}.", "Pass a path relative to that directory.");
        }

        if (!string.Equals(Path.GetExtension(full), ".png", StringComparison.OrdinalIgnoreCase))
        {
            full += ".png";
        }

        // A colon after the root names an alternate data stream, and device names such as NUL or COM1
        // open the device instead of a file.
        var relative = full[prefix.Length..];
        if (relative.Contains(':', StringComparison.Ordinal) || relative.Split(Path.DirectorySeparatorChar).Any(IsDeviceName))
        {
            throw NetwrightException.InvalidArgument($"'{path}' is not a plain file name.", "Use letters, digits, spaces, '-' and '_' for file and folder names.");
        }

        return full;
    }

    /// <summary>Writes the PNG, creating missing directories, and returns its full path. An existing file is replaced.</summary>
    public static string Save(string? directory, string path, byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);
        var full = Resolve(directory, path);
        RefuseLinks(Root(directory!), full, path);
        var folder = Path.GetDirectoryName(full)!;
        var temp = Path.Combine(folder, $"{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(folder);
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
            {
                stream.Write(png);
            }

            // Moving the new file over the old one replaces the directory entry, so a hard link to a file
            // outside the directory is unlinked instead of written through.
            File.Move(temp, full, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DeleteQuietly(temp);
            throw new NetwrightException(
                ErrorCodes.InvalidArgument,
                $"Could not save the screenshot to {full}: {ex.Message}",
                "Pass another path, or ask the User to check the screenshot directory.",
                ex);
        }

        return full;
    }

    private static void DeleteQuietly(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The save already failed; that error is the one to report.
        }
    }

    private static string Root(string directory) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));

    private static bool IsDeviceName(string segment) => DeviceNames.Contains(segment.Split('.')[0].TrimEnd(' '));

    /// <summary>A junction or symbolic link below the root could point outside it, so writing through one is refused.</summary>
    private static void RefuseLinks(string root, string full, string path)
    {
        var file = new FileInfo(full);
        if (file.Exists && file.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw NetwrightException.InvalidArgument($"'{path}' is a link.", "Pass a path to a plain file inside the screenshot directory.");
        }

        for (var current = Path.GetDirectoryName(full); current is not null && current.Length > root.Length; current = Path.GetDirectoryName(current))
        {
            var folder = new DirectoryInfo(current);
            if (folder.Exists && folder.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw NetwrightException.InvalidArgument($"'{path}' goes through the link {current}.", "Pass a path to a plain folder inside the screenshot directory.");
            }
        }
    }
}
