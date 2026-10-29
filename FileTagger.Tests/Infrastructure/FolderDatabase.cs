using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace FileTagger.Tests.Infrastructure
{
    /// <summary>Read-only snapshot of a folder (remote) database at .filetagger\tags.db.</summary>
    public class FolderDatabase
    {
        public List<string> TagNames { get; } = new();
        public Dictionary<string, HashSet<string>> TagsByFile { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, bool> PushedNewByFile { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> DeletedTagNames { get; } = new();
        public List<(string RelativePath, string TagName)> Tombstones { get; } = new();
        public List<(string OldName, string NewName)> Renames { get; } = new();

        public static string PathFor(string watchedDirectory, string folderName = ".filetagger") =>
            Path.Combine(watchedDirectory, folderName, "tags.db");

        public static FolderDatabase Read(string watchedDirectory, string folderName = ".filetagger")
        {
            var dbPath = PathFor(watchedDirectory, folderName);
            if (!File.Exists(dbPath))
                throw new FileNotFoundException("Folder database not found", dbPath);

            var snapshot = new FolderDatabase();
            using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False");
            connection.Open();

            Read(connection, "SELECT Name FROM LocalTags", r => snapshot.TagNames.Add(r.GetString(0)));
            Read(connection, "SELECT RelativePath, PushedNew FROM LocalFileRecords", r =>
            {
                snapshot.TagsByFile[r.GetString(0)] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                snapshot.PushedNewByFile[r.GetString(0)] = r.GetInt64(1) != 0;
            });
            Read(connection, @"
                SELECT f.RelativePath, t.Name FROM LocalFileTags ft
                JOIN LocalFileRecords f ON f.Id = ft.LocalFileRecordId
                JOIN LocalTags t ON t.Id = ft.LocalTagId", r => snapshot.TagsByFile[r.GetString(0)].Add(r.GetString(1)));
            Read(connection, "SELECT Name FROM LocalDeletedTags", r => snapshot.DeletedTagNames.Add(r.GetString(0)));
            Read(connection, "SELECT RelativePath, TagName FROM LocalFileTagTombstones",
                r => snapshot.Tombstones.Add((r.GetString(0), r.GetString(1))));
            Read(connection, "SELECT OldName, NewName FROM LocalTagRenames",
                r => snapshot.Renames.Add((r.GetString(0), r.GetString(1))));

            return snapshot;
        }

        private static void Read(SqliteConnection connection, string sql, Action<SqliteDataReader> row)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                row(reader);
        }
    }
}
