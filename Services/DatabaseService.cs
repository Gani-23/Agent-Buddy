using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AgentBuddy.Models;
using Microsoft.Data.Sqlite;

namespace AgentBuddy.Services;

/// <summary>
/// Service for interacting with the SQLite database
/// </summary>
public class DatabaseService
{
    private const string DefaultMobileSyncApiUrl = "https://rd-base.vercel.app";
    private const string MobileSyncApiUrlKey = "mobile_sync_api_url";
    private const string MobileSyncApiKeyKey = "mobile_sync_api_key";
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly object _schemaLock = new();
    private bool _schemaEnsured;

    public DatabaseService()
    {
        var dopAgentFolder = AppPaths.BaseDirectory;
        Directory.CreateDirectory(dopAgentFolder);
        _dbPath = Path.Combine(dopAgentFolder, "dop_agent.db");
        _connectionString = $"Data Source={_dbPath}";
        EnsureAnalyticsSchema();
    }

    /// <summary>
    /// Check if database file exists
    /// </summary>
    public bool DatabaseExists()
    {
        return File.Exists(_dbPath);
    }

    /// <summary>
    /// Get database file path
    /// </summary>
    public string GetDatabasePath() => _dbPath;

    /// <summary>
    /// Test database connection
    /// </summary>
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            EnsureAnalyticsSchema();
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Get all active RD accounts
    /// </summary>
    public async Task<List<RDAccount>> GetAllActiveAccountsAsync()
    {
        EnsureAnalyticsSchema();
        var accounts = new List<RDAccount>();

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, account_no, account_name, COALESCE(aslaas_no, ''), denomination, month_paid_upto,
                   next_installment_date, first_seen, last_updated, is_active,
                   COALESCE(amount, 0), COALESCE(month_paid_upto_num, 0),
                   COALESCE(next_due_date_iso, ''), COALESCE(total_deposit, 0),
                   COALESCE(status, '')
            FROM rd_accounts
            WHERE is_active = 1
            ORDER BY account_no";

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            accounts.Add(MapAccount(reader));
        }

        return accounts;
    }

    /// <summary>
    /// Get all accounts (including inactive)
    /// </summary>
    public async Task<List<RDAccount>> GetAllAccountsAsync()
    {
        EnsureAnalyticsSchema();
        var accounts = new List<RDAccount>();

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, account_no, account_name, COALESCE(aslaas_no, ''), denomination, month_paid_upto,
                   next_installment_date, first_seen, last_updated, is_active,
                   COALESCE(amount, 0), COALESCE(month_paid_upto_num, 0),
                   COALESCE(next_due_date_iso, ''), COALESCE(total_deposit, 0),
                   COALESCE(status, '')
            FROM rd_accounts
            ORDER BY account_no";

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            accounts.Add(MapAccount(reader));
        }

        return accounts;
    }

    /// <summary>
    /// Get account by account number
    /// </summary>
    public async Task<RDAccount?> GetAccountByNumberAsync(string accountNo)
    {
        EnsureAnalyticsSchema();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, account_no, account_name, COALESCE(aslaas_no, ''), denomination, month_paid_upto,
                   next_installment_date, first_seen, last_updated, is_active,
                   COALESCE(amount, 0), COALESCE(month_paid_upto_num, 0),
                   COALESCE(next_due_date_iso, ''), COALESCE(total_deposit, 0),
                   COALESCE(status, '')
            FROM rd_accounts
            WHERE account_no = @accountNo";
        command.Parameters.AddWithValue("@accountNo", accountNo);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapAccount(reader);
        }

        return null;
    }

    /// <summary>
    /// Search accounts by query (account number or name)
    /// </summary>
    public async Task<List<RDAccount>> SearchAccountsAsync(string query)
    {
        EnsureAnalyticsSchema();
        var accounts = new List<RDAccount>();

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, account_no, account_name, COALESCE(aslaas_no, ''), denomination, month_paid_upto,
                   next_installment_date, first_seen, last_updated, is_active,
                   COALESCE(amount, 0), COALESCE(month_paid_upto_num, 0),
                   COALESCE(next_due_date_iso, ''), COALESCE(total_deposit, 0),
                   COALESCE(status, '')
            FROM rd_accounts
            WHERE is_active = 1
              AND (account_no LIKE @query OR account_name LIKE @query)
            ORDER BY account_no
            LIMIT 100";
        command.Parameters.AddWithValue("@query", $"%{query}%");

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            accounts.Add(MapAccount(reader));
        }

        return accounts;
    }

    /// <summary>
    /// Get accounts due within specified days
    /// </summary>
    public async Task<List<RDAccount>> GetAccountsDueWithinDaysAsync(int days)
    {
        var allAccounts = await GetAllActiveAccountsAsync();
        return allAccounts.Where(a => a.IsDueWithinDays(days)).ToList();
    }

    /// <summary>
    /// Get total account statistics
    /// </summary>
    public async Task<(int totalActive, int totalInactive, decimal totalAmount)> GetAccountStatisticsAsync()
    {
        EnsureAnalyticsSchema();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT
                SUM(CASE WHEN is_active = 1 THEN 1 ELSE 0 END) as active_count,
                SUM(CASE WHEN is_active = 0 THEN 1 ELSE 0 END) as inactive_count,
                SUM(CASE WHEN is_active = 1 THEN COALESCE(amount, 0) ELSE 0 END) as active_amount
            FROM rd_accounts";

        int totalActive = 0;
        int totalInactive = 0;
        decimal totalAmount = 0;

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            totalActive = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0));
            totalInactive = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1));
            totalAmount = reader.IsDBNull(2) ? 0 : Convert.ToDecimal(reader.GetValue(2));
        }

        return (totalActive, totalInactive, totalAmount);
    }

    public async Task SaveAslaasUpdatesAsync(IEnumerable<AslaasUpdateItem> updates)
    {
        EnsureAnalyticsSchema();
        var normalized = updates?
            .Where(item => !string.IsNullOrWhiteSpace(item.AccountNo))
            .Select(item => (
                AccountNo: item.AccountNo.Trim(),
                AslaasNo: string.IsNullOrWhiteSpace(item.AslaasNo) ? "APPLIED" : item.AslaasNo.Trim().ToUpperInvariant()
            ))
            .ToList() ?? new List<(string AccountNo, string AslaasNo)>();

        if (normalized.Count == 0)
        {
            return;
        }

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        foreach (var item in normalized)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
                UPDATE rd_accounts
                SET aslaas_no = @aslaasNo,
                    last_updated = CURRENT_TIMESTAMP
                WHERE account_no = @accountNo";
            command.Parameters.AddWithValue("@aslaasNo", item.AslaasNo);
            command.Parameters.AddWithValue("@accountNo", item.AccountNo);
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    /// <summary>
    /// Get last update time from update_history
    /// </summary>
    public async Task<DateTime?> GetLastUpdateTimeAsync()
    {
        EnsureAnalyticsSchema();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        if (!await TableExistsAsync(connection, "update_history"))
        {
            return null;
        }

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT update_time
            FROM update_history
            ORDER BY id DESC
            LIMIT 1";

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var updateValue = GetStringOrEmpty(reader, 0);
            if (DateTime.TryParse(updateValue, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    /// <summary>
    /// Get monthly revenue data from imported reference history (if available)
    /// </summary>
    public async Task<List<MonthlyRevenue>> GetMonthlyRevenueDataAsync(int months = 24)
    {
        EnsureAnalyticsSchema();
        var revenues = new List<MonthlyRevenue>();

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        if (!await TableExistsAsync(connection, "refrence_data"))
        {
            return revenues;
        }

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT substr(refrence_date, 1, 7) as ym,
                   SUM(COALESCE(commision, 0)) as commission_sum,
                   SUM(COALESCE(rebate, 0)) as rebate_sum,
                   SUM(COALESCE(default_fee, 0)) as default_fee_sum,
                   SUM(CASE WHEN COALESCE(default_fee, 0) > 0 THEN 1 ELSE 0 END) as default_count
            FROM refrence_data
            WHERE refrence_date IS NOT NULL
              AND trim(refrence_date) <> ''
            GROUP BY ym
            ORDER BY ym DESC
            LIMIT @months";
        command.Parameters.AddWithValue("@months", months);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var ym = GetStringOrEmpty(reader, 0);
            if (string.IsNullOrWhiteSpace(ym))
            {
                continue;
            }

            var parts = ym.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 ||
                !int.TryParse(parts[0], out var year) ||
                !int.TryParse(parts[1], out var month) ||
                month < 1 || month > 12)
            {
                continue;
            }

            var commission = reader.IsDBNull(1) ? 0 : Convert.ToDecimal(reader.GetValue(1));
            var rebate = reader.IsDBNull(2) ? 0 : Convert.ToDecimal(reader.GetValue(2));
            var defaultFee = reader.IsDBNull(3) ? 0 : Convert.ToDecimal(reader.GetValue(3));
            var defaultCount = reader.IsDBNull(4) ? 0 : Convert.ToInt32(reader.GetValue(4));

            revenues.Add(new MonthlyRevenue
            {
                Year = year,
                Month = month,
                MonthName = new DateTime(year, month, 1).ToString("MMM", CultureInfo.InvariantCulture),
                Commission = commission,
                Rebate = rebate + defaultFee,
                DefaultCount = defaultCount
            });
        }

        revenues.Reverse();
        return revenues;
    }

    /// <summary>
    /// Get credentials (agent ID only, password is encrypted)
    /// </summary>
    public async Task<string?> GetAgentIdAsync()
    {
        EnsureAnalyticsSchema();
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            if (!await TableExistsAsync(connection, "credentials"))
            {
                return null;
            }

            var command = connection.CreateCommand();
            command.CommandText = "SELECT agent_id FROM credentials WHERE id = 1";

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return GetStringOrEmpty(reader, 0);
            }
        }
        catch
        {
            // Ignore - table may not exist yet
        }

        return null;
    }

    /// <summary>
    /// Get saved credential status.
    /// </summary>
    public async Task<(string? agentId, bool hasPassword)> GetCredentialStatusAsync()
    {
        EnsureAnalyticsSchema();
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            if (!await TableExistsAsync(connection, "credentials"))
            {
                return (null, false);
            }

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT agent_id, COALESCE(encrypted_password, '')
                FROM credentials
                WHERE id = 1";

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var agentId = GetStringOrEmpty(reader, 0);
                var encryptedPassword = GetStringOrEmpty(reader, 1);
                return (string.IsNullOrWhiteSpace(agentId) ? null : agentId, !string.IsNullOrWhiteSpace(encryptedPassword));
            }
        }
        catch
        {
            // Ignore and return default
        }

        return (null, false);
    }

    /// <summary>
    /// Save or update portal credentials (compatible with Python script format).
    /// </summary>
    public async Task SaveCredentialsAsync(string agentId, string password)
    {
        EnsureAnalyticsSchema();

        var normalizedAgentId = (agentId ?? string.Empty).Trim();
        var normalizedPassword = password ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedAgentId))
        {
            throw new InvalidOperationException("Agent ID is required.");
        }

        if (string.IsNullOrWhiteSpace(normalizedPassword))
        {
            throw new InvalidOperationException("Password is required.");
        }

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using (var createCredentials = connection.CreateCommand())
        {
            createCredentials.CommandText = @"
                CREATE TABLE IF NOT EXISTS credentials (
                    id INTEGER PRIMARY KEY,
                    agent_id TEXT NOT NULL,
                    password_hash TEXT NOT NULL,
                    encrypted_password TEXT,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                )";
            await createCredentials.ExecuteNonQueryAsync();
        }

        var passwordHash = ComputeSha256(normalizedPassword);
        var encryptedPassword = Convert.ToBase64String(Encoding.UTF8.GetBytes(normalizedPassword));

        using var upsert = connection.CreateCommand();
        upsert.CommandText = @"
            INSERT INTO credentials (id, agent_id, password_hash, encrypted_password, created_at, updated_at)
            VALUES (1, @agentId, @passwordHash, @encryptedPassword, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
            ON CONFLICT(id) DO UPDATE SET
                agent_id = excluded.agent_id,
                password_hash = excluded.password_hash,
                encrypted_password = excluded.encrypted_password,
                updated_at = CURRENT_TIMESTAMP";

        upsert.Parameters.AddWithValue("@agentId", normalizedAgentId);
        upsert.Parameters.AddWithValue("@passwordHash", passwordHash);
        upsert.Parameters.AddWithValue("@encryptedPassword", encryptedPassword);
        await upsert.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Get generic app setting value.
    /// </summary>
    public async Task<string?> GetAppSettingAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        EnsureAnalyticsSchema();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT value
            FROM app_settings
            WHERE key = @key
            LIMIT 1";
        command.Parameters.AddWithValue("@key", key.Trim());

        var result = await command.ExecuteScalarAsync();
        return result?.ToString();
    }

    /// <summary>
    /// Save generic app setting value.
    /// </summary>
    public async Task SaveAppSettingAsync(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        EnsureAnalyticsSchema();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO app_settings (key, value, updated_at)
            VALUES (@key, @value, CURRENT_TIMESTAMP)
            ON CONFLICT(key) DO UPDATE SET
                value = excluded.value,
                updated_at = CURRENT_TIMESTAMP";
        command.Parameters.AddWithValue("@key", key.Trim());
        command.Parameters.AddWithValue("@value", value ?? string.Empty);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Get mobile sync API settings from local database.
    /// </summary>
    public async Task<(string apiUrl, string apiKey)> GetMobileSyncSettingsAsync()
    {
        var apiUrl = (await GetAppSettingAsync(MobileSyncApiUrlKey) ?? string.Empty).Trim();
        var apiKey = (await GetAppSettingAsync(MobileSyncApiKeyKey) ?? string.Empty).Trim();

        // Migrate old localhost defaults to cloud URL.
        var isLegacyLocalhost = apiUrl.Equals("http://127.0.0.1:8080", StringComparison.OrdinalIgnoreCase) ||
                                apiUrl.Equals("http://localhost:8080", StringComparison.OrdinalIgnoreCase) ||
                                apiUrl.Equals("https://127.0.0.1:8080", StringComparison.OrdinalIgnoreCase) ||
                                apiUrl.Equals("https://localhost:8080", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(apiUrl) || isLegacyLocalhost)
        {
            apiUrl = DefaultMobileSyncApiUrl;
        }

        return (apiUrl, apiKey);
    }

    /// <summary>
    /// Save mobile sync API settings to local database.
    /// </summary>
    public async Task SaveMobileSyncSettingsAsync(string apiUrl, string apiKey)
    {
        await SaveAppSettingAsync(MobileSyncApiUrlKey, apiUrl?.Trim());
        await SaveAppSettingAsync(MobileSyncApiKeyKey, apiKey?.Trim());
    }

    private RDAccount MapAccount(SqliteDataReader reader)
    {
        return new RDAccount
        {
            Id = reader.GetInt32(0),
            AccountNo = GetStringOrEmpty(reader, 1),
            AccountName = GetStringOrEmpty(reader, 2),
            AslaasNo = GetStringOrEmpty(reader, 3),
            Denomination = GetStringOrEmpty(reader, 4),
            MonthPaidUpto = GetStringOrEmpty(reader, 5),
            NextInstallmentDate = GetStringOrEmpty(reader, 6),
            FirstSeen = ParseDateTimeOrNow(GetStringOrEmpty(reader, 7)),
            LastUpdated = ParseDateTimeOrNow(GetStringOrEmpty(reader, 8)),
            IsActive = !reader.IsDBNull(9) && Convert.ToInt32(reader.GetValue(9)) == 1,
            Amount = reader.IsDBNull(10) ? 0 : Convert.ToDecimal(reader.GetValue(10)),
            MonthPaidUptoNumber = reader.IsDBNull(11) ? 0 : Convert.ToInt32(reader.GetValue(11)),
            NextDueDateIso = GetStringOrEmpty(reader, 12),
            TotalDeposit = reader.IsDBNull(13) ? 0 : Convert.ToDecimal(reader.GetValue(13)),
            Status = GetStringOrEmpty(reader, 14)
        };
    }

    private static string GetStringOrEmpty(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
    }

    private static DateTime ParseDateTimeOrNow(string value)
    {
        return DateTime.TryParse(value, out var parsed) ? parsed : DateTime.Now;
    }

    private async Task<bool> TableExistsAsync(SqliteConnection connection, string tableName)
    {
        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT 1
            FROM sqlite_master
            WHERE type = 'table' AND name = @tableName
            LIMIT 1";
        command.Parameters.AddWithValue("@tableName", tableName);

        var result = await command.ExecuteScalarAsync();
        return result != null;
    }

    private void EnsureAnalyticsSchema()
    {
        lock (_schemaLock)
        {
            if (_schemaEnsured)
            {
                return;
            }

            var directory = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using (var createRdAccounts = connection.CreateCommand())
            {
                createRdAccounts.CommandText = @"
                    CREATE TABLE IF NOT EXISTS rd_accounts (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        account_no TEXT UNIQUE NOT NULL,
                        account_name TEXT,
                        aslaas_no TEXT DEFAULT '',
                        denomination TEXT,
                        month_paid_upto TEXT,
                        next_installment_date TEXT,
                        amount INTEGER DEFAULT 0,
                        month_paid_upto_num INTEGER DEFAULT 0,
                        next_due_date_iso TEXT,
                        total_deposit INTEGER DEFAULT 0,
                        status TEXT DEFAULT 'inactive',
                        first_seen TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                        last_updated TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                        is_active INTEGER DEFAULT 1
                    )";
                createRdAccounts.ExecuteNonQuery();
            }

            using (var createUpdateHistory = connection.CreateCommand())
            {
                createUpdateHistory.CommandText = @"
                    CREATE TABLE IF NOT EXISTS update_history (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        update_time TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                        total_accounts INTEGER,
                        new_accounts INTEGER,
                        updated_accounts INTEGER,
                        removed_accounts INTEGER,
                        active_amount INTEGER DEFAULT 0,
                        due_within_30_days INTEGER DEFAULT 0,
                        status TEXT
                    )";
                createUpdateHistory.ExecuteNonQuery();
            }

            using (var createReferenceData = connection.CreateCommand())
            {
                createReferenceData.CommandText = @"
                    CREATE TABLE IF NOT EXISTS refrence_data (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        refrence_date DATE,
                        refrence_id TEXT UNIQUE,
                        monthly_amount INTEGER,
                        advanced_amount INTEGER,
                        no_of_accounts INTEGER,
                        default_fee FLOAT,
                        rebate FLOAT,
                        total FLOAT,
                        tds INTEGER,
                        commision INTEGER,
                        balance_to_pay INTEGER,
                        lot_type TEXT,
                        imported_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                    )";
                createReferenceData.ExecuteNonQuery();
            }

            using (var createCredentials = connection.CreateCommand())
            {
                createCredentials.CommandText = @"
                    CREATE TABLE IF NOT EXISTS credentials (
                        id INTEGER PRIMARY KEY,
                        agent_id TEXT NOT NULL,
                        password_hash TEXT NOT NULL,
                        encrypted_password TEXT,
                        created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                        updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                    )";
                createCredentials.ExecuteNonQuery();
            }

            using (var createAppSettings = connection.CreateCommand())
            {
                createAppSettings.CommandText = @"
                    CREATE TABLE IF NOT EXISTS app_settings (
                        key TEXT PRIMARY KEY,
                        value TEXT,
                        updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                    )";
                createAppSettings.ExecuteNonQuery();
            }

            EnsureColumn(connection, "credentials", "encrypted_password", "TEXT");
            EnsureColumn(connection, "rd_accounts", "amount", "INTEGER DEFAULT 0");
            EnsureColumn(connection, "rd_accounts", "month_paid_upto_num", "INTEGER DEFAULT 0");
            EnsureColumn(connection, "rd_accounts", "next_due_date_iso", "TEXT");
            EnsureColumn(connection, "rd_accounts", "total_deposit", "INTEGER DEFAULT 0");
            EnsureColumn(connection, "rd_accounts", "status", "TEXT DEFAULT 'inactive'");
            EnsureColumn(connection, "rd_accounts", "aslaas_no", "TEXT DEFAULT ''");
            EnsureColumn(connection, "update_history", "active_amount", "INTEGER DEFAULT 0");
            EnsureColumn(connection, "update_history", "due_within_30_days", "INTEGER DEFAULT 0");

            _schemaEnsured = true;
        }
    }

    private static void EnsureColumn(SqliteConnection connection, string tableName, string columnName, string definition)
    {
        if (!TableExists(connection, tableName))
        {
            return;
        }

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var pragmaCommand = connection.CreateCommand())
        {
            pragmaCommand.CommandText = $"PRAGMA table_info({tableName})";
            using var reader = pragmaCommand.ExecuteReader();
            while (reader.Read())
            {
                columns.Add(reader.GetString(1));
            }
        }

        if (columns.Contains(columnName))
        {
            return;
        }

        using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition}";
        alterCommand.ExecuteNonQuery();
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT 1
            FROM sqlite_master
            WHERE type = 'table' AND name = @tableName
            LIMIT 1";
        command.Parameters.AddWithValue("@tableName", tableName);
        return command.ExecuteScalar() != null;
    }

    private static string ComputeSha256(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        var hashBytes = SHA256.HashData(bytes);
        var builder = new StringBuilder(hashBytes.Length * 2);
        foreach (var hashByte in hashBytes)
        {
            builder.Append(hashByte.ToString("x2", CultureInfo.InvariantCulture));
        }
        return builder.ToString();
    }
}
