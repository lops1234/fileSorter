using System.IO;
using FileTagger.Tests.Infrastructure;
using Xunit;

namespace FileTagger.Tests.Sync
{
    public class PushTests : SyncTestBase
    {
        [Fact]
        public void FirstPush_CreatesFolderDatabaseWithTagsFilesAndAssociations()
        {
            var a = CreateFile("a.jpg");
            var b = CreateFile(Path.Combine("sub", "b.jpg"));
            A.Db.AddWatchedDirectory(Shared);
            A.Db.AddTagToFile(a, "cat");
            A.Db.AddTagToFile(b, "cat");
            A.Db.AddTagToFile(b, "dog");
            A.Db.CreateStandaloneTag(Shared, "unused");

            var result = A.Push(Shared);

            Assert.True(result.FolderDatabaseCreated);
            Assert.Equal(3, result.TagsExported);
            Assert.Equal(2, result.FilesExported);
            Assert.Equal(3, result.AssociationsExported);

            var remote = Remote();
            Assert.Equal(new[] { "cat" }, remote.TagsByFile["a.jpg"]);
            Assert.Equal(new[] { "cat", "dog" }, Sorted(remote.TagsByFile[Path.Combine("sub", "b.jpg")]));
            Assert.Contains("unused", remote.TagNames);
        }

        [Fact]
        public void Push_MarksOnlyFilesWhoseTagsChangedAsPushedNew()
        {
            var a = CreateFile("a.jpg");
            var b = CreateFile("b.jpg");
            A.Db.AddWatchedDirectory(Shared);
            A.Db.AddTagToFile(a, "cat");
            A.Db.AddTagToFile(b, "dog");
            A.Push(Shared);
            Assert.True(Remote().PushedNewByFile["a.jpg"]);
            Assert.True(Remote().PushedNewByFile["b.jpg"]);

            A.Db.AddTagToFile(a, "bird");
            var result = A.Push(Shared);

            Assert.False(result.FolderDatabaseCreated);
            Assert.True(Remote().PushedNewByFile["a.jpg"]);
            Assert.False(Remote().PushedNewByFile["b.jpg"]);
        }

        [Fact]
        public void Push_LeavesNoTemporaryJournalFilesInFolder()
        {
            A.Db.AddWatchedDirectory(Shared);
            A.Db.AddTagToFile(CreateFile("a.jpg"), "cat");

            A.Push(Shared);

            Assert.Equal(new[] { "tags.db" },
                System.Array.ConvertAll(Directory.GetFiles(Path.Combine(Shared, ".filetagger")), Path.GetFileName));
        }

        [Fact]
        public void Push_MissingDirectory_ReportsError()
        {
            A.Db.AddWatchedDirectory(Shared);
            Directory.Delete(Shared, true);

            var result = A.Db.PushToFolder(Shared);

            Assert.False(result.Success);
            Assert.NotEmpty(result.Errors);
        }

        [Fact]
        public void Push_UnwatchedDirectory_ReportsError()
        {
            var result = A.Db.PushToFolder(Shared);

            Assert.False(result.Success);
            Assert.NotEmpty(result.Errors);
        }

        [Fact]
        public void PushToAllFolders_PushesEveryWatchedDirectory()
        {
            var second = Sandbox.CreateFolder("second");
            A.Db.AddWatchedDirectory(Shared);
            A.Db.AddWatchedDirectory(second);
            A.Db.AddTagToFile(CreateFile("a.jpg"), "cat");
            A.Db.AddTagToFile(TestSandbox.CreateFile(second, "b.jpg"), "dog");

            var results = A.Db.PushToAllFolders();

            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.True(r.Success));
            Assert.Equal(new[] { "cat" }, Remote().TagsByFile["a.jpg"]);
            Assert.Equal(new[] { "dog" }, FolderDatabase.Read(second).TagsByFile["b.jpg"]);
        }

        private static string[] Sorted(System.Collections.Generic.IEnumerable<string> values)
        {
            var list = new System.Collections.Generic.List<string>(values);
            list.Sort(System.StringComparer.Ordinal);
            return list.ToArray();
        }
    }
}
