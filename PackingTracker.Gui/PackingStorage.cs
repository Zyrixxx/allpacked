using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;

namespace PackingTracker.Gui;

public static class PackingStorage
{
    private const string DatabaseFileName = "all-packed.db";
    private const string LastProfileKey = "last_profile";
    private const string LegacyMigrationKey = "legacy_text_migration_completed";
    private const string ProfileFilePrefix = "packing-list-";
    private const string ProfileFileSuffix = ".txt";
    private const string LastProfileFileName = "last-profile.txt";
    private const string DefaultProfileName = "Default";
    private const string DefaultCategoryName = "Miscellaneous";
    private const char EscapeCharacter = '\\';
    private const char FieldSeparator = '|';

    private static readonly object InitializationLock = new();
    private static bool isInitialized;

    public static string GetDatabasePath()
    {
        string storageDirectory = GetStorageDirectory();
        Directory.CreateDirectory(storageDirectory);
        return Path.Combine(storageDirectory, DatabaseFileName);
    }

    public static List<string> GetProfiles()
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT name
            FROM profiles
            ORDER BY name COLLATE NOCASE;
            """;

        var profiles = new List<string>();

        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            profiles.Add(reader.GetString(0));
        }

        return profiles;
    }

    public static string? LoadLastProfile()
    {
        using SqliteConnection connection = OpenConnection();
        return GetAppStateValue(connection, LastProfileKey);
    }

    public static void SaveLastProfile(string profileName)
    {
        using SqliteConnection connection = OpenConnection();
        SaveAppStateValue(connection, LastProfileKey, CleanProfileName(profileName));
    }

    public static bool DeleteProfile(string profileName)
    {
        string cleanedProfileName = CleanProfileName(profileName);

        using SqliteConnection connection = OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();

        using SqliteCommand deleteProfileCommand = connection.CreateCommand();
        deleteProfileCommand.Transaction = transaction;
        deleteProfileCommand.CommandText = "DELETE FROM profiles WHERE name = $name;";
        deleteProfileCommand.Parameters.AddWithValue("$name", cleanedProfileName);
        int deletedProfileCount = deleteProfileCommand.ExecuteNonQuery();

        string? lastProfile = GetAppStateValue(connection, LastProfileKey, transaction);

        if (string.Equals(lastProfile, cleanedProfileName, StringComparison.OrdinalIgnoreCase))
        {
            DeleteAppStateValue(connection, LastProfileKey, transaction);
        }

        transaction.Commit();
        return deletedProfileCount > 0;
    }

    public static bool ProfileExists(string profileName)
    {
        using SqliteConnection connection = OpenConnection();
        return ProfileExists(connection, CleanProfileName(profileName));
    }

    public static void SaveItems(string profileName, List<PackingItem> items)
    {
        string cleanedProfileName = CleanProfileName(profileName);
        string timestamp = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        using SqliteConnection connection = OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();

        UpsertProfile(connection, cleanedProfileName, timestamp, transaction);
        DeleteItems(connection, cleanedProfileName, transaction);

        for (int i = 0; i < items.Count; i++)
        {
            SaveItem(connection, cleanedProfileName, items[i], i, transaction);
        }

        SaveAppStateValue(connection, LastProfileKey, cleanedProfileName, transaction);
        transaction.Commit();
    }

    public static List<PackingItem> LoadItems(string profileName)
    {
        string cleanedProfileName = CleanProfileName(profileName);

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT category, name, quantity, is_packed
            FROM packing_items
            WHERE profile_name = $profileName
            ORDER BY sort_order, id;
            """;
        command.Parameters.AddWithValue("$profileName", cleanedProfileName);

        var items = new List<PackingItem>();

        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            items.Add(new PackingItem
            {
                Category = reader.GetString(0),
                Name = reader.GetString(1),
                Quantity = reader.GetInt32(2),
                IsPacked = reader.GetInt32(3) == 1
            });
        }

        return items;
    }

    public static string CleanProfileName(string profileName)
    {
        string cleaned = profileName.Trim();

        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return DefaultProfileName;
        }

        foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
        {
            cleaned = cleaned.Replace(invalidCharacter, '-');
        }

        return cleaned.Replace(FieldSeparator, '-');
    }

    private static SqliteConnection OpenConnection()
    {
        EnsureDatabaseReady();
        SqliteConnection connection = CreateConnection();
        connection.Open();
        EnableForeignKeys(connection);
        return connection;
    }

    private static SqliteConnection CreateConnection()
    {
        var connectionStringBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = GetDatabasePath()
        };

        return new SqliteConnection(connectionStringBuilder.ToString());
    }

    private static void EnsureDatabaseReady()
    {
        if (isInitialized)
        {
            return;
        }

        lock (InitializationLock)
        {
            if (isInitialized)
            {
                return;
            }

            using SqliteConnection connection = CreateConnection();
            connection.Open();
            EnableForeignKeys(connection);
            CreateSchema(connection);
            MigrateLegacyTextStorage(connection);

            isInitialized = true;
        }
    }

    private static void CreateSchema(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS profiles (
                name TEXT PRIMARY KEY COLLATE NOCASE,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS packing_items (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                profile_name TEXT NOT NULL COLLATE NOCASE,
                sort_order INTEGER NOT NULL,
                category TEXT NOT NULL,
                name TEXT NOT NULL,
                quantity INTEGER NOT NULL CHECK (quantity > 0),
                is_packed INTEGER NOT NULL CHECK (is_packed IN (0, 1)),
                FOREIGN KEY (profile_name) REFERENCES profiles(name) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS ix_packing_items_profile_sort
            ON packing_items(profile_name, sort_order, id);

            CREATE TABLE IF NOT EXISTS app_state (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private static void MigrateLegacyTextStorage(SqliteConnection connection)
    {
        if (string.Equals(GetAppStateValue(connection, LegacyMigrationKey), "true", StringComparison.Ordinal))
        {
            return;
        }

        using SqliteTransaction transaction = connection.BeginTransaction();

        foreach (string filePath in Directory.GetFiles(
            GetStorageDirectory(),
            $"{ProfileFilePrefix}*{ProfileFileSuffix}"))
        {
            string profileName = CleanProfileName(GetProfileNameFromFilePath(filePath));

            if (string.IsNullOrWhiteSpace(profileName) || ProfileExists(connection, profileName, transaction))
            {
                continue;
            }

            List<PackingItem> items = LoadLegacyItems(filePath);
            string timestamp = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

            UpsertProfile(connection, profileName, timestamp, transaction);

            for (int i = 0; i < items.Count; i++)
            {
                SaveItem(connection, profileName, items[i], i, transaction);
            }
        }

        string? lastProfile = LoadLegacyLastProfile();

        if (!string.IsNullOrWhiteSpace(lastProfile))
        {
            SaveAppStateValue(connection, LastProfileKey, lastProfile, transaction);
        }

        SaveAppStateValue(connection, LegacyMigrationKey, "true", transaction);
        transaction.Commit();
    }

    private static List<PackingItem> LoadLegacyItems(string filePath)
    {
        var items = new List<PackingItem>();

        foreach (string line in File.ReadAllLines(filePath))
        {
            if (TryCreateItemFromLegacyLine(line, out PackingItem item))
            {
                items.Add(item);
            }
        }

        return items;
    }

    private static string? LoadLegacyLastProfile()
    {
        string filePath = Path.Combine(GetStorageDirectory(), LastProfileFileName);

        if (!File.Exists(filePath))
        {
            return null;
        }

        string profileName = CleanProfileName(File.ReadAllText(filePath));
        return string.IsNullOrWhiteSpace(profileName) ? null : profileName;
    }

    private static bool TryCreateItemFromLegacyLine(string line, out PackingItem item)
    {
        item = new PackingItem();

        List<string> parts = SplitStorageLine(line);

        if (parts.Count < 4)
        {
            return false;
        }

        string category = parts[0].Trim();
        string name = string.Join(FieldSeparator, parts.GetRange(1, parts.Count - 3)).Trim();
        string quantityText = parts[^2];
        string isPackedText = parts[^1];

        if (!int.TryParse(quantityText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int quantity) ||
            quantity <= 0)
        {
            return false;
        }

        if (!bool.TryParse(isPackedText, out bool isPacked))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        item = new PackingItem
        {
            Category = string.IsNullOrWhiteSpace(category) ? DefaultCategoryName : category,
            Name = name,
            Quantity = quantity,
            IsPacked = isPacked
        };

        return true;
    }

    private static string GetProfileNameFromFilePath(string filePath)
    {
        string fileName = Path.GetFileName(filePath);

        if (!fileName.StartsWith(ProfileFilePrefix, StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(ProfileFileSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        int profileNameLength = fileName.Length - ProfileFilePrefix.Length - ProfileFileSuffix.Length;
        return fileName.Substring(ProfileFilePrefix.Length, profileNameLength);
    }

    private static List<string> SplitStorageLine(string line)
    {
        var parts = new List<string>();
        var currentPart = new StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char currentCharacter = line[i];

            if (currentCharacter == EscapeCharacter &&
                i + 1 < line.Length &&
                (line[i + 1] == FieldSeparator || line[i + 1] == EscapeCharacter))
            {
                currentPart.Append(line[i + 1]);
                i++;
                continue;
            }

            if (currentCharacter == FieldSeparator)
            {
                parts.Add(currentPart.ToString());
                currentPart.Clear();
                continue;
            }

            currentPart.Append(currentCharacter);
        }

        parts.Add(currentPart.ToString());
        return parts;
    }

    private static void UpsertProfile(
        SqliteConnection connection,
        string profileName,
        string timestamp,
        SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO profiles (name, created_at, updated_at)
            VALUES ($name, $createdAt, $updatedAt)
            ON CONFLICT(name) DO UPDATE SET updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$name", profileName);
        command.Parameters.AddWithValue("$createdAt", timestamp);
        command.Parameters.AddWithValue("$updatedAt", timestamp);
        command.ExecuteNonQuery();
    }

    private static void DeleteItems(
        SqliteConnection connection,
        string profileName,
        SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM packing_items WHERE profile_name = $profileName;";
        command.Parameters.AddWithValue("$profileName", profileName);
        command.ExecuteNonQuery();
    }

    private static void SaveItem(
        SqliteConnection connection,
        string profileName,
        PackingItem item,
        int sortOrder,
        SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO packing_items (profile_name, sort_order, category, name, quantity, is_packed)
            VALUES ($profileName, $sortOrder, $category, $name, $quantity, $isPacked);
            """;
        command.Parameters.AddWithValue("$profileName", profileName);
        command.Parameters.AddWithValue("$sortOrder", sortOrder);
        command.Parameters.AddWithValue("$category", CleanItemCategory(item.Category));
        command.Parameters.AddWithValue("$name", item.Name.Trim());
        command.Parameters.AddWithValue("$quantity", item.Quantity);
        command.Parameters.AddWithValue("$isPacked", item.IsPacked ? 1 : 0);
        command.ExecuteNonQuery();
    }

    private static string CleanItemCategory(string category)
    {
        string cleanedCategory = category.Trim();
        return string.IsNullOrWhiteSpace(cleanedCategory) ? DefaultCategoryName : cleanedCategory;
    }

    private static bool ProfileExists(
        SqliteConnection connection,
        string profileName,
        SqliteTransaction? transaction = null)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM profiles WHERE name = $name LIMIT 1;";
        command.Parameters.AddWithValue("$name", profileName);
        return command.ExecuteScalar() is not null;
    }

    private static string? GetAppStateValue(
        SqliteConnection connection,
        string key,
        SqliteTransaction? transaction = null)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT value FROM app_state WHERE key = $key LIMIT 1;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    private static void SaveAppStateValue(
        SqliteConnection connection,
        string key,
        string value,
        SqliteTransaction? transaction = null)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO app_state (key, value)
            VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static void DeleteAppStateValue(
        SqliteConnection connection,
        string key,
        SqliteTransaction? transaction = null)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM app_state WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        command.ExecuteNonQuery();
    }

    private static void EnableForeignKeys(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        command.ExecuteNonQuery();
    }

    private static string GetStorageDirectory()
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PackingTracker.sln")))
            {
                return Path.Combine(directory.FullName, "data");
            }

            directory = directory.Parent;
        }

        string applicationDataDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        if (!string.IsNullOrWhiteSpace(applicationDataDirectory))
        {
            return Path.Combine(applicationDataDirectory, "All Packed");
        }

        return Path.Combine(AppContext.BaseDirectory, "data");
    }
}
