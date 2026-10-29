using FileTagger.Tests.Infrastructure;
using Xunit;

namespace FileTagger.Tests.Sync
{
    /// <summary>Adding and removing tags on files, merged between two machines.</summary>
    public class TagChangeSyncTests : SyncTestBase
    {
        [Fact]
        public void TagAddedOnA_AppearsOnB()
        {
            var a = CreateFile("a.jpg");
            StartSharedFolder((a, "cat"));

            A.Db.AddTagToFile(a, "dog");
            A.Push(Shared);
            B.Pull(Shared);

            Assert.Equal(new[] { "cat", "dog" }, B.TagsOf(a));
        }

        [Fact]
        public void NewFileTaggedOnA_AppearsOnB()
        {
            StartSharedFolder((CreateFile("a.jpg"), "cat"));
            var b = CreateFile("b.jpg");

            A.Db.AddTagToFile(b, "dog");
            A.Push(Shared);
            B.Pull(Shared);

            Assert.Equal(new[] { "dog" }, B.TagsOf(b));
        }

        [Fact]
        public void TagRemovedOnA_IsRemovedOnB()
        {
            var a = CreateFile("a.jpg");
            StartSharedFolder((a, "cat"), (a, "dog"));

            A.Db.RemoveTagFromFile(a, "dog");
            A.Push(Shared);
            B.Pull(Shared);

            Assert.Equal(new[] { "cat" }, B.TagsOf(a));
        }

        [Fact]
        public void SeveralTagsRemovedOnA_AreRemovedOnB()
        {
            var a = CreateFile("a.jpg");
            var b = CreateFile("b.jpg");
            StartSharedFolder((a, "cat"), (a, "dog"), (a, "bird"), (b, "cat"), (b, "dog"));

            foreach (var file in new[] { a, b })
            foreach (var tag in new[] { "cat", "dog" })
                A.Db.RemoveTagFromFile(file, tag);
            A.Push(Shared);
            B.Pull(Shared);

            Assert.Equal(new[] { "bird" }, B.TagsOf(a));
            Assert.Empty(B.TagsOf(b));
        }

        [Fact]
        public void RemovedTag_IsNotResurrectedByOtherMachinesStaleCopy()
        {
            var a = CreateFile("a.jpg");
            StartSharedFolder((a, "cat"), (a, "dog"));

            // A removes dog; B (still holding dog) pushes something unrelated afterwards.
            A.Db.RemoveTagFromFile(a, "dog");
            A.Push(Shared);
            B.Db.AddTagToFile(CreateFile("b.jpg"), "bird");
            B.Push(Shared);
            A.Pull(Shared);

            Assert.Equal(new[] { "cat" }, A.TagsOf(a));
            Assert.Equal(new[] { "cat" }, B.TagsOf(a));
        }

        [Fact]
        public void RemoveOnBothMachines_StaysRemoved()
        {
            var a = CreateFile("a.jpg");
            StartSharedFolder((a, "cat"), (a, "dog"));

            A.Db.RemoveTagFromFile(a, "dog");
            B.Db.RemoveTagFromFile(a, "dog");
            A.Push(Shared);
            B.Push(Shared);
            A.Pull(Shared);

            Assert.Equal(new[] { "cat" }, A.TagsOf(a));
            Assert.Equal(new[] { "cat" }, B.TagsOf(a));
        }

        [Fact]
        public void ConcurrentAdditionsOnBothMachines_AreMerged()
        {
            var a = CreateFile("a.jpg");
            StartSharedFolder((a, "cat"));

            A.Db.AddTagToFile(a, "dog");
            B.Db.AddTagToFile(a, "bird");
            A.Push(Shared);
            B.Push(Shared);
            A.Pull(Shared);

            Assert.Equal(new[] { "bird", "cat", "dog" }, A.TagsOf(a));
            Assert.Equal(new[] { "bird", "cat", "dog" }, B.TagsOf(a));
        }

        [Fact]
        public void ConcurrentAddOnA_AndRemoveOnB_BothApply()
        {
            var a = CreateFile("a.jpg");
            StartSharedFolder((a, "cat"), (a, "dog"));

            A.Db.AddTagToFile(a, "bird");
            B.Db.RemoveTagFromFile(a, "cat");
            A.Push(Shared);
            B.Push(Shared);
            A.Pull(Shared);

            Assert.Equal(new[] { "bird", "dog" }, A.TagsOf(a));
            Assert.Equal(new[] { "bird", "dog" }, B.TagsOf(a));
        }

        [Fact]
        public void RetagAfterRemoval_WinsOverOlderRemoval()
        {
            var a = CreateFile("a.jpg");
            StartSharedFolder((a, "cat"), (a, "dog"));

            A.Db.RemoveTagFromFile(a, "dog");
            A.Push(Shared);
            TestSandbox.Tick();
            // B removes and re-adds dog later than A's removal: the newer tagging wins.
            B.Db.RemoveTagFromFile(a, "dog");
            TestSandbox.Tick();
            B.Db.AddTagToFile(a, "dog");
            B.Push(Shared);
            A.Pull(Shared);

            Assert.Equal(new[] { "cat", "dog" }, A.TagsOf(a));
            Assert.Equal(new[] { "cat", "dog" }, B.TagsOf(a));
        }

        [Fact]
        public void RemovalAfterRetag_WinsOverOlderTagging()
        {
            var a = CreateFile("a.jpg");
            StartSharedFolder((a, "cat"));

            B.Db.AddTagToFile(a, "dog");
            B.Push(Shared);
            A.Pull(Shared);
            TestSandbox.Tick();
            A.Db.RemoveTagFromFile(a, "dog");
            A.Push(Shared);
            B.Pull(Shared);

            Assert.Equal(new[] { "cat" }, A.TagsOf(a));
            Assert.Equal(new[] { "cat" }, B.TagsOf(a));
        }

        [Fact]
        public void ThreeMachines_ConvergeThroughSharedFolder()
        {
            var a = CreateFile("a.jpg");
            StartSharedFolder((a, "cat"));
            var c = Sandbox.CreateMachine("C");
            c.Db.AddWatchedDirectory(Shared);

            A.Db.AddTagToFile(a, "dog");
            A.Push(Shared);
            B.Db.RemoveTagFromFile(a, "cat");
            B.Push(Shared);
            c.Db.AddTagToFile(a, "bird");
            c.Push(Shared);
            A.Pull(Shared);
            B.Pull(Shared);

            Assert.Equal(new[] { "bird", "dog" }, A.TagsOf(a));
            Assert.Equal(new[] { "bird", "dog" }, B.TagsOf(a));
            Assert.Equal(new[] { "bird", "dog" }, c.TagsOf(a));
        }
    }
}
