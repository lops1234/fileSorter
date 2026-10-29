using System;
using FileTagger.Tests.Infrastructure;

namespace FileTagger.Tests.Local
{
    /// <summary>One machine watching one folder.</summary>
    public abstract class LocalTestBase : IDisposable
    {
        protected TestSandbox Sandbox { get; } = new TestSandbox();
        protected Machine Machine { get; }
        protected string Folder { get; }

        protected LocalTestBase()
        {
            Machine = Sandbox.CreateMachine("local");
            Folder = Sandbox.CreateFolder("watched");
            Machine.Db.AddWatchedDirectory(Folder);
        }

        protected string CreateFile(string relativePath) => TestSandbox.CreateFile(Folder, relativePath);

        public void Dispose() => Sandbox.Dispose();
    }
}
