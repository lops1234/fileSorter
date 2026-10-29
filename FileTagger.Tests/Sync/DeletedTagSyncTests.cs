using System.Linq;
using Xunit;

namespace FileTagger.Tests.Sync
{
    /// <summary>Whole-tag deletions and stashing, propagated through the folder database.</summary>
    public class DeletedTagSyncTests : SyncTestBase
    {
        [Fact]
        public void DeleteTagOnA_RemovesItOnB()
        {
            var a = CreateFile("a.jpg");
            var b = CreateFile("b.jpg");
            StartSharedFolder((a, "cat"), (b, "cat"), (b, "dog"));

            A.Db.DeleteTag("cat");
            A.Push(Shared);
            B.Pull(Shared);

            Assert.Empty(B.TagsOf(a));
            Assert.Equal(new[] { "dog" }, B.TagsOf(b));
            Assert.DoesNotContain("cat", B.AllTagNames());
        }

        [Fact]
        public void DeleteTagOnA_IsListedAsRestorableOnB()
        {
            var a = CreateFile("a.jpg");
            StartSharedFolder((a, "cat"));

            A.Db.DeleteTag("cat");
            A.Push(Shared);
            B.Pull(Shared);

            var deleted = Assert.Single(B.Db.GetDeletedTags());
            Assert.Equal("cat", deleted.Name);
            Assert.Equal(new[] { a }, deleted.FilePaths);

            B.Db.RestoreDeletedTag(deleted.Id);
            Assert.Equal(new[] { "cat" }, B.TagsOf(a));
        }

        [Fact]
        public void DeleteTag_IsWrittenToFolderDatabase_AndClearedLocallyAfterPush()
        {
            StartSharedFolder((CreateFile("a.jpg"), "cat"));

            A.Db.DeleteTag("cat");
            A.Push(Shared);

            Assert.Contains("cat", Remote().DeletedTagNames);
            Assert.Contains(("a.jpg", "cat"), Remote().Tombstones);
            Assert.Empty(A.Db.GetDeletedTags());
        }

        [Fact]
        public void DeleteTagOnA_DoesNotRemoveTagAddedOnBAfterwards()
        {
            var a = CreateFile("a.jpg");
            var b = CreateFile("b.jpg");
            StartSharedFolder((a, "cat"));

            A.Db.DeleteTag("cat");
            A.Push(Shared);
            Infrastructure.TestSandbox.Tick();
            B.Db.AddTagToFile(b, "cat");
            B.Push(Shared);
            A.Pull(Shared);

            Assert.Empty(B.TagsOf(a));
            Assert.Equal(new[] { "cat" }, B.TagsOf(b));
            Assert.Equal(new[] { "cat" }, A.TagsOf(b));
            Assert.Empty(A.TagsOf(a));
        }

        [Fact]
        public void StashAllTagsOnA_RemovesTagsOnB_AndStashSurvivesPushOnA()
        {
            var a = CreateFile("a.jpg");
            var b = CreateFile("b.jpg");
            StartSharedFolder((a, "cat"), (b, "dog"));

            A.Db.StashAllTags();
            A.Push(Shared);
            B.Pull(Shared);

            Assert.Empty(B.TagsOf(a));
            Assert.Empty(B.TagsOf(b));
            Assert.Equal(2, A.Db.GetStashedTagCount());
        }

        [Fact]
        public void RestoreStashedTagsOnA_ThenPush_RestoresThemOnB()
        {
            var a = CreateFile("a.jpg");
            StartSharedFolder((a, "cat"));
            A.Db.StashAllTags();
            A.Push(Shared);
            B.Pull(Shared);
            Infrastructure.TestSandbox.Tick();

            A.Db.RestoreAllStashedTags();
            A.Push(Shared);
            B.Pull(Shared);

            Assert.Equal(new[] { "cat" }, A.TagsOf(a));
            Assert.Equal(new[] { "cat" }, B.TagsOf(a));
        }

        [Fact]
        public void CleanupRemoteDeletedTags_ClearsDeletedTagListInFolderDatabase()
        {
            StartSharedFolder((CreateFile("a.jpg"), "cat"));
            A.Db.DeleteTag("cat");
            A.Push(Shared);
            Assert.NotEmpty(Remote().DeletedTagNames);

            A.Db.CleanupRemoteDeletedTags(Shared);

            Assert.Empty(Remote().DeletedTagNames);
            Assert.NotEmpty(Remote().Tombstones);
        }
    }
}
