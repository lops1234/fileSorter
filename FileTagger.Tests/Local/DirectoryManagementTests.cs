using System.IO;
using System.Linq;
using FileTagger.Tests.Infrastructure;
using Xunit;

namespace FileTagger.Tests.Local
{
    public class DirectoryManagementTests : LocalTestBase
    {
        [Fact]
        public void AddWatchedDirectory_IsListedAsActive()
        {
            Assert.Contains(Folder, Machine.Db.GetAllActiveDirectories());
        }

        [Fact]
        public void AddWatchedDirectory_Twice_DoesNotDuplicate()
        {
            Machine.Db.AddWatchedDirectory(Folder);

            Assert.Single(Machine.Db.GetAllActiveDirectories());
        }

        [Fact]
        public void RemoveWatchedDirectory_DeactivatesIt()
        {
            Machine.Db.RemoveWatchedDirectory(Folder);

            Assert.Empty(Machine.Db.GetAllActiveDirectories());
        }

        [Fact]
        public void RemoveWatchedDirectory_ThenReAdd_KeepsTags()
        {
            var file = CreateFile("a.jpg");
            Machine.Db.AddTagToFile(file, "cat");

            Machine.Db.RemoveWatchedDirectory(Folder);
            Assert.Empty(Machine.TagsOf(file));

            Machine.Db.AddWatchedDirectory(Folder);
            Assert.Equal(new[] { "cat" }, Machine.TagsOf(file));
        }

        [Fact]
        public void GetWatchedDirectoryForFile_ReturnsDeepestWatchedDirectory()
        {
            var nested = Path.Combine(Folder, "nested");
            Directory.CreateDirectory(nested);
            Machine.Db.AddWatchedDirectory(nested);

            var file = Path.Combine(nested, "deep", "a.jpg");

            Assert.Equal(nested, Machine.Db.GetWatchedDirectoryForFile(file));
            Assert.Equal(new[] { nested, Folder }, Machine.Db.GetApplicableDirectoriesForFile(file));
        }

        [Fact]
        public void GetWatchedDirectoryForFile_OutsideWatchedFolders_ReturnsNull()
        {
            var outside = Path.Combine(Sandbox.Root, "elsewhere", "a.jpg");

            Assert.Null(Machine.Db.GetWatchedDirectoryForFile(outside));
        }

        [Fact]
        public void DiscoverAndImportExistingDatabases_FindsFolderDatabasesUnderRoot()
        {
            var other = Sandbox.CreateFolder(Path.Combine("discover-root", "photos"));
            var file = TestSandbox.CreateFile(other, "a.jpg");
            var creator = Sandbox.CreateMachine("creator");
            creator.Db.AddWatchedDirectory(other);
            creator.Db.AddTagToFile(file, "cat");
            creator.Push(other);

            var discovered = Machine.Db.DiscoverAndImportExistingDatabases(Path.Combine(Sandbox.Root, "discover-root"));

            Assert.Equal(new[] { other }, discovered);
            Assert.Contains(other, Machine.Db.GetAllActiveDirectories());
            Assert.Equal(new[] { "cat" }, Machine.TagsOf(file));
        }

        [Fact]
        public void DiscoverAndImportExistingDatabases_SkipsAlreadyWatchedDirectories()
        {
            var file = CreateFile("a.jpg");
            Machine.Db.AddTagToFile(file, "cat");
            Machine.Push(Folder);

            Assert.Empty(Machine.Db.DiscoverAndImportExistingDatabases(Sandbox.Root));
            Assert.Single(Machine.Db.GetAllActiveDirectories());
        }
    }
}
