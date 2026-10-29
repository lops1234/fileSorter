using System.Linq;
using FileTagger.Tests.Infrastructure;
using Xunit;

namespace FileTagger.Tests.Sync
{
    /// <summary>Tag renames propagated through the folder database.</summary>
    public class RenameSyncTests : SyncTestBase
    {
        [Fact]
        public void RenameOnA_RenamesTagOnB()
        {
            var a = CreateFile("a.jpg");
            var b = CreateFile("b.jpg");
            StartSharedFolder((a, "cat"), (b, "cat"), (b, "dog"));

            A.Db.UpdateTagName("cat", "kitty");
            A.Push(Shared);
            B.Pull(Shared);

            Assert.Equal(new[] { "kitty" }, B.TagsOf(a));
            Assert.Equal(new[] { "dog", "kitty" }, B.TagsOf(b));
            Assert.DoesNotContain("cat", B.AllTagNames());
        }

        [Fact]
        public void RenameOnA_IsWrittenToFolderDatabase()
        {
            StartSharedFolder((CreateFile("a.jpg"), "cat"));

            A.Db.UpdateTagName("cat", "kitty");
            A.Push(Shared);

            var remote = Remote();
            Assert.Contains(("cat", "kitty"), remote.Renames);
            Assert.Equal(new[] { "kitty" }, remote.TagsByFile["a.jpg"]);
            Assert.DoesNotContain("cat", remote.TagNames);
        }

        [Fact]
        public void RenameOnA_KeepsDescriptionOnB()
        {
            var a = CreateFile("a.jpg");
            A.Db.AddWatchedDirectory(Shared);
            A.Db.AddTagToFile(a, "cat", "felines");
            A.Push(Shared);
            B.Db.AddWatchedDirectory(Shared);

            A.Db.UpdateTagName("cat", "kitty");
            A.Push(Shared);
            B.Pull(Shared);

            Assert.Equal("felines", B.Query(db => db.Tags.Single(t => t.Name == "kitty").Description));
        }

        [Fact]
        public void RenameOnA_SurvivesPushFromBWithStaleData()
        {
            var a = CreateFile("a.jpg");
            StartSharedFolder((a, "cat"));

            A.Db.UpdateTagName("cat", "kitty");
            A.Push(Shared);
            // B has not pulled yet and pushes its old view; its pre-push pull applies the rename.
            B.Db.AddTagToFile(CreateFile("b.jpg"), "dog");
            B.Push(Shared);
            A.Pull(Shared);

            Assert.Equal(new[] { "kitty" }, A.TagsOf(a));
            Assert.Equal(new[] { "kitty" }, B.TagsOf(a));
            Assert.DoesNotContain("cat", A.AllTagNames());
            Assert.Equal(new[] { "kitty" }, Remote().TagsByFile["a.jpg"]);
        }

        [Fact]
        public void RenameOnA_CoversFileTaggedOnBThatAHadNotSeen()
        {
            var a = CreateFile("a.jpg");
            var b = CreateFile("b.jpg");
            StartSharedFolder((a, "cat"));

            // B tags another file with the old name and pushes; A renames without pulling first.
            B.Db.AddTagToFile(b, "cat");
            B.Push(Shared);
            TestSandbox.Tick();
            A.Db.UpdateTagName("cat", "kitty");
            A.Push(Shared);
            B.Pull(Shared);

            Assert.Equal(new[] { "kitty" }, A.TagsOf(b));
            Assert.Equal(new[] { "kitty" }, B.TagsOf(a));
            Assert.Equal(new[] { "kitty" }, B.TagsOf(b));
            Assert.DoesNotContain("cat", A.AllTagNames());
            Assert.DoesNotContain("cat", B.AllTagNames());
        }

        [Fact]
        public void RenameOnA_CoversFileTaggedOnBBeforeTheRenameButPushedAfter()
        {
            var a = CreateFile("a.jpg");
            var b = CreateFile("b.jpg");
            StartSharedFolder((a, "cat"));

            B.Db.AddTagToFile(b, "cat");
            TestSandbox.Tick();
            A.Db.UpdateTagName("cat", "kitty");
            A.Push(Shared);
            B.Push(Shared);
            A.Pull(Shared);

            Assert.Equal(new[] { "kitty" }, A.TagsOf(b));
            Assert.Equal(new[] { "kitty" }, B.TagsOf(b));
        }

        [Fact]
        public void TagWithOldNameAddedAfterRename_KeepsOldName()
        {
            var a = CreateFile("a.jpg");
            var c = CreateFile("c.jpg");
            StartSharedFolder((a, "cat"));

            A.Db.UpdateTagName("cat", "kitty");
            A.Push(Shared);
            TestSandbox.Tick();
            // B deliberately uses "cat" after the rename happened.
            B.Db.AddTagToFile(c, "cat");
            B.Push(Shared);
            A.Pull(Shared);

            Assert.Equal(new[] { "kitty" }, B.TagsOf(a));
            Assert.Equal(new[] { "cat" }, B.TagsOf(c));
            Assert.Equal(new[] { "cat" }, A.TagsOf(c));
        }

        [Fact]
        public void CaseOnlyRenameOnA_IsAppliedOnB()
        {
            var a = CreateFile("a.jpg");
            StartSharedFolder((a, "cat"));

            A.Db.UpdateTagName("cat", "Cat");
            A.Push(Shared);
            B.Pull(Shared);

            Assert.Equal(new[] { "Cat" }, B.TagsOf(a));
            Assert.Equal(new[] { "Cat" }, B.AllTagNames());
        }

        [Fact]
        public void RenameChainOnA_EndsAtLastNameOnB()
        {
            var a = CreateFile("a.jpg");
            StartSharedFolder((a, "a"));

            A.Db.UpdateTagName("a", "b");
            TestSandbox.Tick();
            A.Db.UpdateTagName("b", "c");
            A.Push(Shared);
            B.Pull(Shared);

            Assert.Equal(new[] { "c" }, B.TagsOf(a));
            Assert.Equal(new[] { "c" }, B.AllTagNames());
        }

        [Fact]
        public void RenameChainPushedInBetween_EndsAtLastNameOnB()
        {
            var a = CreateFile("a.jpg");
            StartSharedFolder((a, "a"));

            A.Db.UpdateTagName("a", "b");
            A.Push(Shared);
            TestSandbox.Tick();
            A.Db.UpdateTagName("b", "c");
            A.Push(Shared);
            B.Pull(Shared);

            Assert.Equal(new[] { "c" }, B.TagsOf(a));
            Assert.Equal(new[] { "c" }, B.AllTagNames());
        }

        [Fact]
        public void RenameBackAndForth_EndsAtLatestName()
        {
            var a = CreateFile("a.jpg");
            StartSharedFolder((a, "cat"));

            A.Db.UpdateTagName("cat", "kitty");
            A.Push(Shared);
            B.Pull(Shared);
            TestSandbox.Tick();
            B.Db.UpdateTagName("kitty", "cat");
            B.Push(Shared);
            A.Pull(Shared);

            Assert.Equal(new[] { "cat" }, A.TagsOf(a));
            Assert.Equal(new[] { "cat" }, B.TagsOf(a));
            Assert.Equal(new[] { "cat" }, A.AllTagNames());
        }

        [Fact]
        public void RenameIntoNameThatExistsOnB_MergesTags()
        {
            var a = CreateFile("a.jpg");
            var b = CreateFile("b.jpg");
            StartSharedFolder((a, "cat"));
            // B independently has a "kitty" tag on another file that A doesn't know about yet.
            B.Db.AddTagToFile(b, "kitty");

            A.Db.UpdateTagName("cat", "kitty");
            A.Push(Shared);
            B.Pull(Shared);

            Assert.Equal(new[] { "kitty" }, B.TagsOf(a));
            Assert.Equal(new[] { "kitty" }, B.TagsOf(b));
            Assert.Equal(new[] { "kitty" }, B.AllTagNames());
        }

        [Fact]
        public void RenameOfStandaloneTag_RenamesSameTagOnB()
        {
            A.Db.AddWatchedDirectory(Shared);
            A.Db.CreateStandaloneTag(Shared, "box");
            A.Push(Shared);
            B.Db.AddWatchedDirectory(Shared);
            B.Db.CreateStandaloneTag(Shared, "box");
            TestSandbox.Tick();

            A.Db.UpdateTagName("box", "crate");
            A.Push(Shared);
            B.Pull(Shared);

            Assert.Equal(new[] { "crate" }, B.AllTagNames());
        }

        [Fact]
        public void RenameThenRepeatedSyncs_IsStable()
        {
            var a = CreateFile("a.jpg");
            StartSharedFolder((a, "cat"), (a, "dog"));

            A.Db.UpdateTagName("cat", "kitty");
            for (var i = 0; i < 2; i++)
            {
                A.Push(Shared);
                B.Push(Shared);
                A.Pull(Shared);
                B.Pull(Shared);
            }

            Assert.Equal(new[] { "dog", "kitty" }, A.TagsOf(a));
            Assert.Equal(new[] { "dog", "kitty" }, B.TagsOf(a));
            Assert.Equal(2, B.Query(db => db.FileTags.Count()));
            Assert.Equal(2, B.Query(db => db.Tags.Count()));
        }

        [Fact]
        public void RenameOnA_AndRemovalOnB_BothApply()
        {
            var a = CreateFile("a.jpg");
            var b = CreateFile("b.jpg");
            StartSharedFolder((a, "cat"), (b, "cat"));

            A.Db.UpdateTagName("cat", "kitty");
            TestSandbox.Tick();
            B.Db.RemoveTagFromFile(b, "cat");
            A.Push(Shared);
            B.Push(Shared);
            A.Pull(Shared);

            Assert.Equal(new[] { "kitty" }, A.TagsOf(a));
            Assert.Equal(new[] { "kitty" }, B.TagsOf(a));
            Assert.Empty(B.TagsOf(b));
        }
    }
}
