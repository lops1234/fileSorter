using System.IO;
using System.Linq;
using Xunit;

namespace FileTagger.Tests.Local
{
    public class FileQueryTests : LocalTestBase
    {
        [Fact]
        public void GetAllFilesWithTags_ReturnsOnlyTaggedFilesWithFullPaths()
        {
            var tagged = CreateFile("a.jpg");
            CreateFile("b.jpg");
            Machine.Db.AddTagToFile(tagged, "cat");
            Machine.Db.AddTagToFile(tagged, "dog");

            var file = Assert.Single(Machine.Db.GetAllFilesWithTags());

            Assert.Equal(tagged, file.FullPath);
            Assert.Equal("a.jpg", file.FileName);
            Assert.Equal(new[] { "cat", "dog" }, file.Tags.OrderBy(t => t));
        }

        [Fact]
        public void GetAllFilesWithTags_ExcludesFilesWhoseTagsWereAllRemoved()
        {
            var file = CreateFile("a.jpg");
            Machine.Db.AddTagToFile(file, "cat");
            Machine.Db.RemoveTagFromFile(file, "cat");

            Assert.Empty(Machine.Db.GetAllFilesWithTags());
        }

        [Fact]
        public void GetUntaggedFiles_ReturnsFilesOnDiskWithoutTags()
        {
            var tagged = CreateFile("a.jpg");
            var untagged = CreateFile(Path.Combine("sub", "b.jpg"));
            Machine.Db.AddTagToFile(tagged, "cat");

            var files = Machine.Db.GetUntaggedFiles();

            Assert.Equal(new[] { untagged }, files.Select(f => f.FullPath));
        }

        [Fact]
        public void GetAllFilesInWatchedDirectories_ReturnsTaggedAndUntagged()
        {
            var tagged = CreateFile("a.jpg");
            var untagged = CreateFile("b.jpg");
            Machine.Db.AddTagToFile(tagged, "cat");

            var files = Machine.Db.GetAllFilesInWatchedDirectories();

            Assert.Equal(new[] { tagged, untagged }, files.Select(f => f.FullPath).OrderBy(p => p));
            Assert.Equal(new[] { "cat" }, files.Single(f => f.FullPath == tagged).Tags);
            Assert.Empty(files.Single(f => f.FullPath == untagged).Tags);
        }

        [Fact]
        public void VerifyAndCleanupTaggedFiles_RemovesRecordsOfMissingFiles()
        {
            var kept = CreateFile("a.jpg");
            var missing = CreateFile("b.jpg");
            Machine.Db.AddTagToFile(kept, "cat");
            Machine.Db.AddTagToFile(missing, "dog");
            File.Delete(missing);

            var result = Machine.Db.VerifyAndCleanupTaggedFiles();

            Assert.True(result.Success);
            Assert.Equal(2, result.TotalFilesChecked);
            Assert.Equal(1, result.MissingFilesCount);
            Assert.Equal(new[] { missing }, result.MissingFiles);
            Assert.Equal(new[] { "dog" }, result.AffectedTags);
            Assert.Equal(new[] { "cat" }, Machine.TagsOf(kept));
            Assert.Equal(1, Machine.Query(db => db.FileRecords.Count()));
        }

        [Fact]
        public void VerifyAndCleanupFilesForDirectory_OnlyTouchesThatDirectory()
        {
            var second = Sandbox.CreateFolder("second");
            Machine.Db.AddWatchedDirectory(second);
            var missingHere = CreateFile("a.jpg");
            var missingThere = Infrastructure.TestSandbox.CreateFile(second, "b.jpg");
            Machine.Db.AddTagToFile(missingHere, "cat");
            Machine.Db.AddTagToFile(missingThere, "cat");
            File.Delete(missingHere);
            File.Delete(missingThere);

            var result = Machine.Db.VerifyAndCleanupFilesForDirectory(Folder);

            Assert.Equal(1, result.MissingFilesCount);
            Assert.Equal(1, Machine.Query(db => db.FileRecords.Count()));
        }

        [Fact]
        public void VerifyAndCleanupFilesForDirectory_UnknownDirectory_ReportsError()
        {
            var result = Machine.Db.VerifyAndCleanupFilesForDirectory(Path.Combine(Sandbox.Root, "unknown"));

            Assert.False(result.Success);
            Assert.NotEmpty(result.Errors);
        }
    }
}
