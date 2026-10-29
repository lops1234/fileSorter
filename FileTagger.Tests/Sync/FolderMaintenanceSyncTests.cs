using System.IO;
using Xunit;

namespace FileTagger.Tests.Sync
{
    /// <summary>Duplicate folder databases (cloud sync conflicts) and folder cleanup.</summary>
    public class FolderMaintenanceSyncTests : SyncTestBase
    {
        /// <summary>Creates ".filetagger" from machine A and ".filetagger (1)" from a different machine.</summary>
        private (string FromA, string FromOther) CreateConflictingFolderDatabases()
        {
            var fromA = CreateFile("a.jpg");
            A.Db.AddWatchedDirectory(Shared);
            A.Db.AddTagToFile(fromA, "cat");
            A.Push(Shared);
            var stash = Path.Combine(Sandbox.Root, "stash");
            Directory.Move(Path.Combine(Shared, ".filetagger"), stash);

            var other = Sandbox.CreateMachine("other");
            var fromOther = CreateFile("b.jpg");
            other.Db.AddWatchedDirectory(Shared);
            other.Db.AddTagToFile(fromOther, "dog");
            other.Push(Shared);

            Directory.Move(stash, Path.Combine(Shared, ".filetagger (1)"));
            return (fromA, fromOther);
        }

        [Fact]
        public void Pull_MergesDuplicateFolderDatabases()
        {
            var (fromA, fromOther) = CreateConflictingFolderDatabases();

            B.Db.AddWatchedDirectory(Shared);
            var result = B.Pull(Shared);

            Assert.Equal(2, result.DatabasesFound);
            Assert.Equal(new[] { "cat" }, B.TagsOf(fromA));
            Assert.Equal(new[] { "dog" }, B.TagsOf(fromOther));
        }

        [Fact]
        public void MergeAllDuplicateFileTaggerDatabases_DeletesDuplicatesAndKeepsData()
        {
            var (fromA, fromOther) = CreateConflictingFolderDatabases();
            B.Db.AddWatchedDirectory(Shared);

            var result = B.Db.MergeAllDuplicateFileTaggerDatabases();

            Assert.True(result.Success, string.Join("; ", result.Errors));
            Assert.Equal(1, result.DuplicateDatabasesFound);
            Assert.Equal(1, result.DuplicateDatabasesDeleted);
            Assert.False(Directory.Exists(Path.Combine(Shared, ".filetagger (1)")));
            Assert.False(B.Db.HasDuplicateDatabases(Shared));
            Assert.Equal(new[] { "cat" }, B.TagsOf(fromA));
            Assert.Equal(new[] { "dog" }, B.TagsOf(fromOther));
        }

        [Fact]
        public void CleanupFolder_MergesDuplicates_DropsMissingFiles_AndWritesOneCleanDatabase()
        {
            var (fromA, fromOther) = CreateConflictingFolderDatabases();
            var missing = CreateFile("missing.jpg");
            B.Db.AddWatchedDirectory(Shared);
            B.Db.AddTagToFile(missing, "bird");
            File.Delete(missing);

            var result = B.Db.CleanupFolder(Shared);

            Assert.True(result.Success, string.Join("; ", result.Errors));
            Assert.Equal(2, result.DirectoriesDeleted);
            Assert.Equal(1, result.VerificationResult.MissingFilesCount);
            Assert.False(Directory.Exists(Path.Combine(Shared, ".filetagger (1)")));

            var remote = Remote();
            Assert.Equal(new[] { "cat" }, remote.TagsByFile["a.jpg"]);
            Assert.Equal(new[] { "dog" }, remote.TagsByFile["b.jpg"]);
            Assert.False(remote.TagsByFile.ContainsKey("missing.jpg"));
            Assert.Equal(new[] { "cat" }, B.TagsOf(fromA));
            Assert.Equal(new[] { "dog" }, B.TagsOf(fromOther));
        }

        [Fact]
        public void FolderDatabaseExists_AndPathHelpers_ReflectFolderState()
        {
            A.Db.AddWatchedDirectory(Shared);
            Assert.False(A.Db.FolderDatabaseExists(Shared));

            A.Db.AddTagToFile(CreateFile("a.jpg"), "cat");
            A.Push(Shared);

            Assert.True(A.Db.FolderDatabaseExists(Shared));
            Assert.Equal(A.DatabasePath, A.Db.GetDatabasePathForDirectory(Shared));
        }
    }
}
