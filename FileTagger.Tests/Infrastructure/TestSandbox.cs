using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using FileTagger.Data;
using FileTagger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

// DatabaseManager is a process-wide singleton bound to one central database at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace FileTagger.Tests.Infrastructure
{
    internal static class SandboxGuard
    {
        /// <summary>Never let a test touch the real %APPDATA%\FileTagger database.</summary>
        public static readonly string FallbackDatabasePath =
            Path.Combine(Path.GetTempPath(), "FileTaggerTests", "unassigned-machine.db");

        [ModuleInitializer]
        internal static void Initialize()
        {
            CentralDbContext.DatabasePathOverride = FallbackDatabasePath;
        }
    }

    /// <summary>
    /// A temporary root folder per test containing simulated machines (one central DB each)
    /// and watched folders shared between them, like a cloud-synced directory.
    /// </summary>
    public class TestSandbox : IDisposable
    {
        public string Root { get; }

        public TestSandbox()
        {
            Root = Path.Combine(Path.GetTempPath(), "FileTaggerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public Machine CreateMachine(string name) =>
            new Machine(name, Path.Combine(Root, "machines", name, "filetagger.db"));

        public string CreateFolder(string relativePath)
        {
            var path = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(path);
            return path;
        }

        public static string CreateFile(string directory, string relativePath, string content = "x")
        {
            var path = Path.Combine(directory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        /// <summary>Guarantees later timestamps compare strictly greater than earlier ones.</summary>
        public static void Tick() => Thread.Sleep(20);

        public void Dispose()
        {
            CentralDbContext.DatabasePathOverride = SandboxGuard.FallbackDatabasePath;
            DatabaseManager.Instance.InvalidateActiveDirectoryCache();
            SqliteConnection.ClearAllPools();

            try { Directory.Delete(Root, true); } catch { }
        }
    }
}
