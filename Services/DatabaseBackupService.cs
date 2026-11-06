using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AgentBuddy.Services;

public sealed record DatabaseBackupInfo
{
    public string FileName { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public long SizeBytes { get; init; }
    public string SizeDisplay => $"{SizeBytes / 1024.0 / 1024.0:F2} MB";
}

public sealed class DatabaseBackupService
{
    private readonly DatabaseService _databaseService;

    public DatabaseBackupService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    private string GetBackupsDirectory()
    {
        var dbPath = _databaseService.DatabasePath;
        var baseDir = Path.GetDirectoryName(dbPath) ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "DOPAgent");
        var backupDir = Path.Combine(baseDir, "backups");
        Directory.CreateDirectory(backupDir);
        return backupDir;
    }

    /// <summary>
    /// Creates an automatic safety snapshot before critical operations (master list sync, lot creation)
    /// </summary>
    public async Task<(bool Success, string BackupPath, string Message)> CreateAutoSnapshotAsync(string operationName = "operation")
    {
        try
        {
            var dbPath = _databaseService.DatabasePath;
            if (!File.Exists(dbPath))
            {
                return (false, string.Empty, "Database file does not exist to snapshot.");
            }

            var backupDir = GetBackupsDirectory();
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var cleanOp = string.Concat(operationName.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
            var targetFileName = $"dop_agent_{cleanOp}_{timestamp}.db";
            var targetPath = Path.Combine(backupDir, targetFileName);

            // Copy file asynchronously
            await Task.Run(() => File.Copy(dbPath, targetPath, overwrite: true));

            // Clean up old auto snapshots keeping only the last 15
            CleanOldBackups(backupDir, maxRetained: 15);

            return (true, targetPath, $"Snapshot saved: {targetFileName}");
        }
        catch (Exception ex)
        {
            return (false, string.Empty, $"Snapshot failed: {ex.Message}");
        }
    }

    public List<DatabaseBackupInfo> GetAvailableBackups()
    {
        try
        {
            var backupDir = GetBackupsDirectory();
            if (!Directory.Exists(backupDir)) return new List<DatabaseBackupInfo>();

            return Directory.GetFiles(backupDir, "dop_agent_*.db")
                .Select(file => new FileInfo(file))
                .OrderByDescending(f => f.CreationTime)
                .Select(f => new DatabaseBackupInfo
                {
                    FileName = f.Name,
                    FullPath = f.FullName,
                    CreatedAt = f.CreationTime,
                    SizeBytes = f.Length,
                })
                .ToList();
        }
        catch
        {
            return new List<DatabaseBackupInfo>();
        }
    }

    private static void CleanOldBackups(string backupDir, int maxRetained = 15)
    {
        try
        {
            var files = Directory.GetFiles(backupDir, "dop_agent_*.db")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTime)
                .Skip(maxRetained)
                .ToList();

            foreach (var oldFile in files)
            {
                try { oldFile.Delete(); } catch { }
            }
        }
        catch { }
    }
}
