using System.IO;
using System.Linq;
using FileTagger.Tests.Infrastructure;
using Xunit;

namespace FileTagger.Tests.Local
{
    public class TagOperationsTests : LocalTestBase
    {
        [Fact]
        public void AddTagToFile_TagIsReturnedForFile()
        {
            var file = CreateFile("a.jpg");

            Machine.Db.AddTagToFile(file, "cat");

            Assert.Equal(new[] { "cat" }, Machine.TagsOf(file));
        }

        [Fact]
        public void AddTagToFile_SameTagTwice_IsStoredOnce()
        {
            var file = CreateFile("a.jpg");

            Machine.Db.AddTagToFile(file, "cat");
            Machine.Db.AddTagToFile(file, "CAT");

            Assert.Equal(new[] { "cat" }, Machine.TagsOf(file));
            Assert.Equal(1, Machine.Query(db => db.Tags.Count()));
        }

        [Fact]
        public void AddTagToFile_InSubfolder_StoresRelativePath()
        {
            var file = CreateFile(Path.Combine("sub", "a.jpg"));

            Machine.Db.AddTagToFile(file, "cat");

            Assert.Equal(Path.Combine("sub", "a.jpg"), Machine.Query(db => db.FileRecords.Single().RelativePath));
            Assert.Equal(new[] { "cat" }, Machine.TagsOf(file));
        }

        [Fact]
        public void AddTagToFile_OutsideWatchedFolder_IsIgnored()
        {
            var outside = TestSandbox.CreateFile(Sandbox.CreateFolder("elsewhere"), "a.jpg");

            Machine.Db.AddTagToFile(outside, "cat");

            Assert.Empty(Machine.TagsOf(outside));
            Assert.Equal(0, Machine.Query(db => db.FileRecords.Count()));
        }

        [Fact]
        public void RemoveTagFromFile_RemovesOnlyThatTag_AndRecordsTombstone()
        {
            var file = CreateFile("a.jpg");
            Machine.Db.AddTagToFile(file, "cat");
            Machine.Db.AddTagToFile(file, "dog");

            Machine.Db.RemoveTagFromFile(file, "cat");

            Assert.Equal(new[] { "dog" }, Machine.TagsOf(file));
            Assert.True(Machine.Query(db => db.FileTagTombstones.Any(t => t.TagName == "cat" && t.RelativePath == "a.jpg")));
        }

        [Fact]
        public void RemoveTagFromFile_KeepsTagOnOtherFiles()
        {
            var a = CreateFile("a.jpg");
            var b = CreateFile("b.jpg");
            Machine.Db.AddTagToFile(a, "cat");
            Machine.Db.AddTagToFile(b, "cat");

            Machine.Db.RemoveTagFromFile(a, "cat");

            Assert.Empty(Machine.TagsOf(a));
            Assert.Equal(new[] { "cat" }, Machine.TagsOf(b));
        }

        [Fact]
        public void RemoveSeveralTags_FromSeveralFiles()
        {
            var a = CreateFile("a.jpg");
            var b = CreateFile("b.jpg");
            foreach (var file in new[] { a, b })
            foreach (var tag in new[] { "cat", "dog", "bird" })
                Machine.Db.AddTagToFile(file, tag);

            foreach (var file in new[] { a, b })
            foreach (var tag in new[] { "cat", "bird" })
                Machine.Db.RemoveTagFromFile(file, tag);

            Assert.Equal(new[] { "dog" }, Machine.TagsOf(a));
            Assert.Equal(new[] { "dog" }, Machine.TagsOf(b));
        }

        [Fact]
        public void GetManageTagsSnapshot_ReturnsTagsPerFileAndAllTagNames()
        {
            var a = CreateFile("a.jpg");
            var b = CreateFile("b.jpg");
            var untagged = CreateFile("c.jpg");
            Machine.Db.AddTagToFile(a, "cat");
            Machine.Db.AddTagToFile(b, "dog");
            Machine.Db.CreateStandaloneTag(Folder, "unused");

            var snapshot = Machine.Db.GetManageTagsSnapshot(new[] { a, b, untagged });

            Assert.Equal(new[] { "cat" }, snapshot.TagsByFile[a]);
            Assert.Equal(new[] { "dog" }, snapshot.TagsByFile[b]);
            Assert.Empty(snapshot.TagsByFile[untagged]);
            Assert.Equal(new[] { "cat", "dog", "unused" }, snapshot.AllTagNames);
        }

        [Fact]
        public void GetAllAvailableTags_CountsUsageAcrossDirectories_AndHidesUnusedTags()
        {
            var second = Sandbox.CreateFolder("second");
            Machine.Db.AddWatchedDirectory(second);
            Machine.Db.AddTagToFile(CreateFile("a.jpg"), "cat");
            Machine.Db.AddTagToFile(CreateFile("b.jpg"), "cat");
            Machine.Db.AddTagToFile(TestSandbox.CreateFile(second, "c.jpg"), "Cat");
            Machine.Db.CreateStandaloneTag(Folder, "unused");

            var tags = Machine.Db.GetAllAvailableTags();

            var cat = Assert.Single(tags);
            Assert.Equal(3, cat.TotalUsageCount);
            Assert.Equal(2, cat.SourceDirectories.Count);
        }

        [Fact]
        public void CreateStandaloneTag_IsAvailableWithoutFiles()
        {
            Machine.Db.CreateStandaloneTag(Folder, "unused", "description");

            Assert.Contains("unused", Machine.AllTagNames());
            Assert.Equal("description", Machine.Query(db => db.Tags.Single().Description));
        }

        [Fact]
        public void CreateStandaloneTag_ExistingTag_UpdatesDescriptionOnly()
        {
            var file = CreateFile("a.jpg");
            Machine.Db.AddTagToFile(file, "cat");

            Machine.Db.CreateStandaloneTag(Folder, "cat", "felines");

            Assert.Equal(1, Machine.Query(db => db.Tags.Count()));
            Assert.Equal("felines", Machine.Query(db => db.Tags.Single().Description));
            Assert.Equal(new[] { "cat" }, Machine.TagsOf(file));
        }

        [Fact]
        public void CreateStandaloneTagInAllDirectories_CreatesTagPerDirectory()
        {
            Machine.Db.AddWatchedDirectory(Sandbox.CreateFolder("second"));

            Machine.Db.CreateStandaloneTagInAllDirectories("shared");

            Assert.Equal(2, Machine.Query(db => db.Tags.Count(t => t.Name == "shared")));
        }

        [Fact]
        public void UpdateTagDescription_UpdatesEveryDirectory()
        {
            var second = Sandbox.CreateFolder("second");
            Machine.Db.AddWatchedDirectory(second);
            Machine.Db.CreateStandaloneTagInAllDirectories("cat");

            Machine.Db.UpdateTagDescription("CAT", "felines");

            Assert.All(Machine.Query(db => db.Tags.ToList()), t => Assert.Equal("felines", t.Description));
        }
    }

    public class TagRenameTests : LocalTestBase
    {
        [Fact]
        public void UpdateTagName_RenamesTagOnAllFiles()
        {
            var a = CreateFile("a.jpg");
            var b = CreateFile("b.jpg");
            Machine.Db.AddTagToFile(a, "cat");
            Machine.Db.AddTagToFile(b, "cat");
            Machine.Db.AddTagToFile(b, "dog");

            Machine.Db.UpdateTagName("cat", "kitty");

            Assert.Equal(new[] { "kitty" }, Machine.TagsOf(a));
            Assert.Equal(new[] { "dog", "kitty" }, Machine.TagsOf(b));
            Assert.DoesNotContain("cat", Machine.AllTagNames());
        }

        [Fact]
        public void UpdateTagName_KeepsDescription()
        {
            Machine.Db.AddTagToFile(CreateFile("a.jpg"), "cat", "felines");

            Machine.Db.UpdateTagName("cat", "kitty");

            Assert.Equal("felines", Machine.Query(db => db.Tags.Single(t => t.Name == "kitty").Description));
        }

        [Fact]
        public void UpdateTagName_RecordsRenameAndTombstonesForSync()
        {
            Machine.Db.AddTagToFile(CreateFile("a.jpg"), "cat");

            Machine.Db.UpdateTagName("cat", "kitty");

            var rename = Machine.Query(db => db.TagRenames.Single());
            Assert.Equal(("cat", "kitty"), (rename.OldName, rename.NewName));
            Assert.True(Machine.Query(db => db.FileTagTombstones.Any(t => t.TagName == "cat" && t.RelativePath == "a.jpg")));
        }

        [Fact]
        public void UpdateTagName_CaseOnlyChange_IsApplied()
        {
            var file = CreateFile("a.jpg");
            Machine.Db.AddTagToFile(file, "cat");

            Machine.Db.UpdateTagName("cat", "Cat");

            Assert.Equal(new[] { "Cat" }, Machine.TagsOf(file));
        }

        [Fact]
        public void UpdateTagName_StandaloneTag_IsRenamed()
        {
            Machine.Db.CreateStandaloneTag(Folder, "box");

            Machine.Db.UpdateTagName("box", "crate");

            Assert.Equal(new[] { "crate" }, Machine.AllTagNames());
        }

        [Fact]
        public void UpdateTagName_AcrossDirectories_RenamesEverywhere()
        {
            var second = Sandbox.CreateFolder("second");
            Machine.Db.AddWatchedDirectory(second);
            var a = CreateFile("a.jpg");
            var b = TestSandbox.CreateFile(second, "b.jpg");
            Machine.Db.AddTagToFile(a, "cat");
            Machine.Db.AddTagToFile(b, "cat");

            Machine.Db.UpdateTagName("cat", "kitty");

            Assert.Equal(new[] { "kitty" }, Machine.TagsOf(a));
            Assert.Equal(new[] { "kitty" }, Machine.TagsOf(b));
            Assert.Equal(2, Machine.Query(db => db.TagRenames.Count()));
        }

        [Fact]
        public void UpdateTagName_Chain_EndsAtLastName()
        {
            var file = CreateFile("a.jpg");
            Machine.Db.AddTagToFile(file, "a");

            Machine.Db.UpdateTagName("a", "b");
            TestSandbox.Tick();
            Machine.Db.UpdateTagName("b", "c");

            Assert.Equal(new[] { "c" }, Machine.TagsOf(file));
            Assert.Equal(new[] { "c" }, Machine.AllTagNames());
        }

        [Fact]
        public void UpdateTagName_ThenOldNameReused_NewAssociationKeepsOldName()
        {
            var a = CreateFile("a.jpg");
            var b = CreateFile("b.jpg");
            Machine.Db.AddTagToFile(a, "cat");
            Machine.Db.UpdateTagName("cat", "kitty");
            TestSandbox.Tick();

            Machine.Db.AddTagToFile(b, "cat");
            // A later rename of something else re-applies all recorded renames; it must not touch b.
            Machine.Db.UpdateTagName("kitty", "kitten");

            Assert.Equal(new[] { "kitten" }, Machine.TagsOf(a));
            Assert.Equal(new[] { "cat" }, Machine.TagsOf(b));
        }

        [Fact]
        public void UpdateTagName_SameName_DoesNothing()
        {
            Machine.Db.AddTagToFile(CreateFile("a.jpg"), "cat");

            Machine.Db.UpdateTagName("cat", "cat");

            Assert.Equal(0, Machine.Query(db => db.TagRenames.Count()));
        }
    }
}
