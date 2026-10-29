using System.IO;
using System.Linq;
using FileTagger.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FileTagger.Tests.Sync
{
    public class PullTests : SyncTestBase
    {
        [Fact]
        public void AddWatchedDirectory_ImportsExistingFolderDatabase()
        {
            var a = CreateFile("a.jpg");
            var b = CreateFile(Path.Combine("sub", "b.jpg"));

            StartSharedFolder((a, "cat"), (b, "dog"));

            Assert.Equal(new[] { "cat" }, B.TagsOf(a));
            Assert.Equal(new[] { "dog" }, B.TagsOf(b));
        }

        [Fact]
        public void Pull_ImportsChangesPushedAfterWatchingStarted()
        {
            var a = CreateFile("a.jpg");
            StartSharedFolder((a, "cat"));

            A.Db.AddTagToFile(a, "dog");
            A.Push(Shared);
            var result = B.Pull(Shared);

            Assert.True(result.Success);
            Assert.Equal(1, result.DatabasesPulled);
            Assert.Equal(new[] { "cat", "dog" }, B.TagsOf(a));
        }

        [Fact]
        public void Pull_WithoutFolderDatabase_BootstrapsOneByPushing()
        {
            A.Db.AddWatchedDirectory(Shared);
            A.Db.AddTagToFile(CreateFile("a.jpg"), "cat");

            var result = A.Pull(Shared);

            Assert.True(result.WasBootstrapped);
            Assert.Equal(1, result.BootstrapFilesExported);
            Assert.Equal(new[] { "cat" }, Remote().TagsByFile["a.jpg"]);
        }

        [Fact]
        public void Pull_UnwatchedDirectory_ReportsError()
        {
            var result = A.Db.PullFromFolder(Shared);

            Assert.False(result.Success);
            Assert.NotEmpty(result.Errors);
        }

        [Fact]
        public void Pull_IsIdempotent()
        {
            var a = CreateFile("a.jpg");
            StartSharedFolder((a, "cat"));

            B.Pull(Shared);
            B.Pull(Shared);

            Assert.Equal(new[] { "cat" }, B.TagsOf(a));
            Assert.Equal(1, B.Query(db => db.FileTags.Count()));
            Assert.Equal(1, B.Query(db => db.Tags.Count()));
        }

        [Fact]
        public void PullFromAllFolders_PullsEveryWatchedDirectory()
        {
            var second = Sandbox.CreateFolder("second");
            var a = CreateFile("a.jpg");
            var b = TestSandbox.CreateFile(second, "b.jpg");
            A.Db.AddWatchedDirectory(Shared);
            A.Db.AddWatchedDirectory(second);
            A.Db.AddTagToFile(a, "cat");
            A.Db.AddTagToFile(b, "dog");
            A.Db.PushToAllFolders();
            B.Db.AddWatchedDirectory(Shared);
            B.Db.AddWatchedDirectory(second);
            A.Db.AddTagToFile(a, "bird");
            A.Db.AddTagToFile(b, "bird");
            A.Db.PushToAllFolders();

            var results = B.Db.PullFromAllFolders();

            Assert.All(results, r => Assert.True(r.Success, string.Join("; ", r.Errors)));
            Assert.Equal(new[] { "bird", "cat" }, B.TagsOf(a));
            Assert.Equal(new[] { "bird", "dog" }, B.TagsOf(b));
        }

        [Fact]
        public void Pull_FileNotMarkedPushedNew_DoesNotChangeLocalTags()
        {
            var a = CreateFile("a.jpg");
            var other = CreateFile("other.jpg");
            StartSharedFolder((a, "cat"), (other, "x"));
            // A pushes again changing only another file: a.jpg is not PushedNew any more.
            A.Db.AddTagToFile(other, "y");
            A.Push(Shared);
            Assert.False(Remote().PushedNewByFile["a.jpg"]);

            B.Db.AddTagToFile(a, "dog");
            B.Pull(Shared);

            Assert.Equal(new[] { "cat", "dog" }, B.TagsOf(a));
            Assert.Equal(new[] { "x", "y" }, B.TagsOf(other));
        }

        [Fact]
        public void Pull_LegacyFolderDatabaseWithoutSyncColumns_ImportsEverything()
        {
            var a = CreateFile("a.jpg");
            CreateLegacyFolderDatabase(("a.jpg", "cat"), ("a.jpg", "dog"));

            B.Db.AddWatchedDirectory(Shared);
            B.Pull(Shared);

            Assert.Equal(new[] { "cat", "dog" }, B.TagsOf(a));
        }

        [Fact]
        public void Pull_UnreadableFolderDatabase_IsNotOverwritten()
        {
            A.Db.AddWatchedDirectory(Shared);
            A.Db.AddTagToFile(CreateFile("a.jpg"), "cat");
            var dbPath = FolderDatabase.PathFor(Shared);
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            File.WriteAllText(dbPath, "not a sqlite database");

            var result = A.Db.PullFromFolder(Shared);

            Assert.False(result.Success);
            Assert.False(result.WasBootstrapped);
            Assert.Equal("not a sqlite database", File.ReadAllText(dbPath));
        }

        /// <summary>Folder database as written by versions before PushedNew and tombstones existed.</summary>
        private void CreateLegacyFolderDatabase(params (string RelativePath, string Tag)[] associations)
        {
            var dbPath = FolderDatabase.PathFor(Shared);
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
            {
                connection.Open();
                Execute(connection, @"
                    CREATE TABLE LocalTags (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL,
                        Description TEXT NOT NULL, CreatedAt TEXT NOT NULL, LastUsedAt TEXT NOT NULL);
                    CREATE UNIQUE INDEX IX_LocalTags_Name ON LocalTags (Name);
                    CREATE TABLE LocalFileRecords (Id INTEGER PRIMARY KEY AUTOINCREMENT, FileName TEXT NOT NULL,
                        RelativePath TEXT NOT NULL, CreatedAt TEXT NOT NULL, LastModified TEXT NOT NULL, FileSize INTEGER NOT NULL);
                    CREATE UNIQUE INDEX IX_LocalFileRecords_RelativePath ON LocalFileRecords (RelativePath);
                    CREATE TABLE LocalFileTags (Id INTEGER PRIMARY KEY AUTOINCREMENT, LocalFileRecordId INTEGER NOT NULL,
                        LocalTagId INTEGER NOT NULL, CreatedAt TEXT NOT NULL);");

                foreach (var (relativePath, tag) in associations)
                {
                    Execute(connection, $@"
                        INSERT OR IGNORE INTO LocalTags (Name, Description, CreatedAt, LastUsedAt)
                            VALUES ('{tag}', '', '2024-01-01 00:00:00', '2024-01-01 00:00:00');
                        INSERT OR IGNORE INTO LocalFileRecords (FileName, RelativePath, CreatedAt, LastModified, FileSize)
                            VALUES ('{Path.GetFileName(relativePath)}', '{relativePath}', '2024-01-01 00:00:00', '2024-01-01 00:00:00', 1);
                        INSERT INTO LocalFileTags (LocalFileRecordId, LocalTagId, CreatedAt)
                            SELECT f.Id, t.Id, '2024-01-01 00:00:00' FROM LocalFileRecords f, LocalTags t
                            WHERE f.RelativePath = '{relativePath}' AND t.Name = '{tag}';");
                }
            }
        }

        private static void Execute(SqliteConnection connection, string sql)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }
}
