using System;
using FileTagger.Tests.Infrastructure;

namespace FileTagger.Tests.Sync
{
    /// <summary>
    /// Two computers (A and B) with separate central databases watching the same folder,
    /// which plays the role of a cloud-synced directory holding the remote .filetagger database.
    /// </summary>
    public abstract class SyncTestBase : IDisposable
    {
        protected TestSandbox Sandbox { get; } = new TestSandbox();
        protected string Shared { get; }
        protected Machine A { get; }
        protected Machine B { get; }

        protected SyncTestBase()
        {
            Shared = Sandbox.CreateFolder("shared");
            A = Sandbox.CreateMachine("A");
            B = Sandbox.CreateMachine("B");
        }

        protected string CreateFile(string relativePath) => TestSandbox.CreateFile(Shared, relativePath);

        /// <summary>A tags the given files, pushes, and B starts watching the folder (which pulls).</summary>
        protected void StartSharedFolder(params (string File, string Tag)[] tags)
        {
            A.Db.AddWatchedDirectory(Shared);
            foreach (var (file, tag) in tags)
                A.Db.AddTagToFile(file, tag);
            A.Push(Shared);

            B.Db.AddWatchedDirectory(Shared);
        }

        protected FolderDatabase Remote() => FolderDatabase.Read(Shared);

        public void Dispose() => Sandbox.Dispose();
    }
}
