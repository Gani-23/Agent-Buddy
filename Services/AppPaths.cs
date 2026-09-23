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
        try
        {
            var config = GlobalConfig.Load();
            if (!string.IsNullOrWhiteSpace(config.BaseDirectoryOverride))
            {
                return EnsureDirectory(config.BaseDirectoryOverride);
            }
        }
        catch { }

        return EnsureDirectory(Path.Combine(DocumentsDirectory, "DOPAgent"));
    }

    private static string ResolveDocumentsDirectory()
    {
        // 1. Mobile platforms (Android & iOS): always use personal/app data sandbox directory
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS() || OperatingSystem.IsTvOS() || OperatingSystem.IsWatchOS())
        {
            try
            {
                var personal = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
                if (!string.IsNullOrWhiteSpace(personal))
                {
                    return EnsureDirectory(personal);
                }
            }
            catch { }

            try
            {
                var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrWhiteSpace(localApp))
                {
                    return EnsureDirectory(localApp);
                }
            }
            catch { }
        }

        // 2. Desktop OS (Windows, macOS, Desktop Linux): use ~/Documents
        try
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(profile) &&
                (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
                 RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
                 (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && !OperatingSystem.IsAndroid())))
            {
                var profileDocuments = Path.Combine(profile, "Documents");
                return EnsureDirectory(profileDocuments);
            }
        }
        catch { }

        try
        {
            var myDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrWhiteSpace(myDocs))
            {
                return EnsureDirectory(myDocs);
            }
        }
        catch { }

        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(local))
            {
                return EnsureDirectory(local);
            }
        }
        catch { }

        return EnsureDirectory(Path.Combine(Path.GetTempPath(), "AgentBuddy"));
    }

    private static string EnsureDirectory(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && !Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            return path;
        }
        catch
        {
            return path;
        }
    }
}
