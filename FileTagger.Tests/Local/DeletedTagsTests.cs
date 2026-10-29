using System.Linq;
using Xunit;

namespace FileTagger.Tests.Local
{
    public class DeletedTagsTests : LocalTestBase
    {
        [Fact]
        public void DeleteTag_RemovesTagFromAllFiles_AndRecordsDeletion()
        {
            var a = CreateFile("a.jpg");
            var b = CreateFile("b.jpg");
            Machine.Db.AddTagToFile(a, "cat");
            Machine.Db.AddTagToFile(b, "cat");
            Machine.Db.AddTagToFile(b, "dog");

            Machine.Db.DeleteTag("cat");

            Assert.Empty(Machine.TagsOf(a));
            Assert.Equal(new[] { "dog" }, Machine.TagsOf(b));
            Assert.DoesNotContain("cat", Machine.AllTagNames());

            var deleted = Assert.Single(Machine.Db.GetDeletedTags());
            Assert.Equal("cat", deleted.Name);
            Assert.Equal(new[] { a, b }, deleted.FilePaths.OrderBy(p => p));
        }

        [Fact]
        public void RestoreDeletedTag_RestoresFileAssociations()
        {
            var a = CreateFile("a.jpg");
            var b = CreateFile("b.jpg");
            Machine.Db.AddTagToFile(a, "cat", "felines");
            Machine.Db.AddTagToFile(b, "cat");
            Machine.Db.DeleteTag("cat");

            Machine.Db.RestoreDeletedTag(Machine.Db.GetDeletedTags().Single().Id);

            Assert.Equal(new[] { "cat" }, Machine.TagsOf(a));
            Assert.Equal(new[] { "cat" }, Machine.TagsOf(b));
            Assert.Equal("felines", Machine.Query(db => db.Tags.Single().Description));
            Assert.Empty(Machine.Db.GetDeletedTags());
        }

        [Fact]
        public void RestoreDeletedTagsByNames_ReportsRestoredAndMissingNames()
        {
            var file = CreateFile("a.jpg");
            Machine.Db.AddTagToFile(file, "cat");
            Machine.Db.DeleteTag("cat");

            var result = Machine.Db.RestoreDeletedTagsByNames(new[] { " CAT ", "nope", "" });

            Assert.Equal(1, result.RestoredCount);
            Assert.Equal(new[] { "nope" }, result.NotFoundNames);
            Assert.Equal(new[] { "cat" }, Machine.TagsOf(file));
        }

        [Fact]
        public void StashAllTags_RemovesEverything_AndRestoreBringsItBack()
        {
            var a = CreateFile("a.jpg");
            var b = CreateFile("b.jpg");
            Machine.Db.AddTagToFile(a, "cat");
            Machine.Db.AddTagToFile(b, "dog");
            Machine.Db.CreateStandaloneTag(Folder, "unused");

            Assert.Equal(3, Machine.Db.StashAllTags());

            Assert.Empty(Machine.AllTagNames());
            Assert.Equal(3, Machine.Db.GetStashedTagCount());
            Assert.Empty(Machine.Db.GetDeletedTags()); // stashed tags are not listed as regular deletions

            Assert.Equal(3, Machine.Db.RestoreAllStashedTags());

            Assert.Equal(new[] { "cat" }, Machine.TagsOf(a));
            Assert.Equal(new[] { "dog" }, Machine.TagsOf(b));
            Assert.Equal(new[] { "cat", "dog", "unused" }, Machine.AllTagNames());
            Assert.Equal(0, Machine.Db.GetStashedTagCount());
        }

        [Fact]
        public void CleanupLocalDeletedTags_ClearsDeletedAndStashedRecords()
        {
            Machine.Db.AddTagToFile(CreateFile("a.jpg"), "cat");
            Machine.Db.AddTagToFile(CreateFile("b.jpg"), "dog");
            Machine.Db.DeleteTag("cat");
            Machine.Db.StashAllTags();

            Machine.Db.CleanupLocalDeletedTags();

            Assert.Empty(Machine.Db.GetDeletedTags());
            Assert.Equal(0, Machine.Db.GetStashedTagCount());
        }
    }
}
