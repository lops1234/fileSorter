using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileTagger.Data;
using FileTagger.Services;

namespace FileTagger.Tests.Infrastructure
{
    /// <summary>
    /// One simulated computer: its own central database. Every access through <see cref="Db"/>
    /// points the DatabaseManager singleton at this machine's database first.
    /// </summary>
    public class Machine
    {
        private bool _initialized;

        public string Name { get; }
        public string DatabasePath { get; }

        public Machine(string name, string databasePath)
        {
            Name = name;
            DatabasePath = databasePath;
        }

        public DatabaseManager Db
        {
            get
            {
                Activate();
                return DatabaseManager.Instance;
            }
        }

        public void Activate()
        {
            CentralDbContext.DatabasePathOverride = DatabasePath;
            DatabaseManager.Instance.InvalidateActiveDirectoryCache();

            if (!_initialized)
            {
                _initialized = true;
                DatabaseManager.Instance.Initialize();
            }
        }

        public T Query<T>(Func<CentralDbContext, T> query)
        {
            Activate();
            using var db = new CentralDbContext();
            return query(db);
        }

        public List<string> TagsOf(string filePath) =>
            Db.GetTagsForFile(filePath).OrderBy(t => t, StringComparer.Ordinal).ToList();

        public List<string> AllTagNames() =>
            Db.GetAllTagNamesForPrompt().OrderBy(t => t, StringComparer.Ordinal).ToList();

        public PushResult Push(string directory)
        {
            var result = Db.PushToFolder(directory);
            Xunit.Assert.True(result.Errors.Count == 0, $"{Name} push failed: {string.Join("; ", result.Errors)}");
            return result;
        }

        public PullResult Pull(string directory)
        {
            var result = Db.PullFromFolder(directory);
            Xunit.Assert.True(result.Errors.Count == 0, $"{Name} pull failed: {string.Join("; ", result.Errors)}");
            return result;
        }

        public override string ToString() => Name;
    }
}
