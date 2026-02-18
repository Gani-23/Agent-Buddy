using System;
using System.IO;
using System.Runtime.InteropServices;

namespace AgentBuddy.Services;

public static class AppPaths
{
    private static readonly Lazy<string> DocumentsDirectoryLazy = new(ResolveDocumentsDirectory);
    private static readonly Lazy<string> BaseDirectoryLazy = new(ResolveBaseDirectory);

    public static string DocumentsDirectory => DocumentsDirectoryLazy.Value;
    public static string BaseDirectory => BaseDirectoryLazy.Value;

    private static string ResolveBaseDirectory()
    {
        return EnsureDirectory(Path.Combine(DocumentsDirectory, "DOPAgent"));
    }

    private static string ResolveDocumentsDirectory()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile) &&
            (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
             RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
             RuntimeInformation.IsOSPlatform(OSPlatform.Linux)))
        {
            var profileDocuments = Path.Combine(profile, "Documents");
            return EnsureDirectory(profileDocuments);
        }

        return EnsureDirectory(AppContext.BaseDirectory);
    }

    private static string EnsureDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            return path;
        }
        catch
        {
            return path;
        }
    }
}
