using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FileTagger.Services;
using FileTagger.Tests.Infrastructure;
using Xunit;

namespace FileTagger.Tests.Services
{
    public class TempResultsManagerTests : System.IDisposable
    {
        private readonly TestSandbox _sandbox = new TestSandbox();
        private readonly string _tempDir;
        private readonly TempResultsManager _manager;

        public TempResultsManagerTests()
        {
            _tempDir = Path.Combine(_sandbox.Root, "search-results");
            _manager = new TempResultsManager(_tempDir);
        }

        public void Dispose() => _sandbox.Dispose();

        [Fact]
        public void Constructor_CreatesTempDirectory()
        {
            Assert.True(Directory.Exists(_tempDir));
            Assert.Equal(_tempDir, _manager.GetTempDirectoryPath());
        }

        [Fact]
        public async Task CopySearchResults_CopiesFiles_AndMapsBackToOriginals()
        {
            var source = _sandbox.CreateFolder("source");
            var a = TestSandbox.CreateFile(source, "a.jpg", "A");
            var b = TestSandbox.CreateFile(source, "b.jpg", "B");

            var (copied, errors, cancelled) = await _manager.CopySearchResultsAsync(new List<string> { a, b }, "cat");

            Assert.Equal(2, copied);
            Assert.Empty(errors);
            Assert.False(cancelled);
            Assert.Equal("A", File.ReadAllText(Path.Combine(_tempDir, "a.jpg")));
            Assert.Equal(a, _manager.GetOriginalFilePath(Path.Combine(_tempDir, "a.jpg")));
            Assert.Equal(b, _manager.GetOriginalFilePath(Path.Combine(_tempDir, "b.jpg")));
        }

        [Fact]
        public async Task CopySearchResults_DuplicateNames_GetNumberSuffix()
        {
            var first = TestSandbox.CreateFile(_sandbox.CreateFolder("one"), "a.jpg", "1");
            var second = TestSandbox.CreateFile(_sandbox.CreateFolder("two"), "a.jpg", "2");

            await _manager.CopySearchResultsAsync(new List<string> { first, second });

            Assert.Equal("2", File.ReadAllText(Path.Combine(_tempDir, "a_1.jpg")));
            Assert.Equal(first, _manager.GetOriginalFilePath(Path.Combine(_tempDir, "a.jpg")));
            Assert.Equal(second, _manager.GetOriginalFilePath(Path.Combine(_tempDir, "a_1.jpg")));
        }

        [Fact]
        public async Task CopySearchResults_MissingFile_IsReportedAndSkipped()
        {
            var existing = TestSandbox.CreateFile(_sandbox.CreateFolder("source"), "a.jpg");
            var missing = Path.Combine(_sandbox.Root, "source", "missing.jpg");

            var (copied, errors, _) = await _manager.CopySearchResultsAsync(new List<string> { existing, missing });

            Assert.Equal(1, copied);
            Assert.Single(errors);
        }

        [Fact]
        public async Task HasSearchParametersChanged_FalseOnlyForSameQueryAndFiles()
        {
            var files = new List<string> { TestSandbox.CreateFile(_sandbox.CreateFolder("source"), "a.jpg") };
            Assert.True(_manager.HasSearchParametersChanged("cat", files));

            await _manager.CopySearchResultsAsync(files, "cat");

            Assert.False(_manager.HasSearchParametersChanged("cat", files));
            Assert.True(_manager.HasSearchParametersChanged("dog", files));
            Assert.True(_manager.HasSearchParametersChanged("cat", new List<string>()));
        }

        [Fact]
        public void IsInTempDirectory_OnlyForFilesDirectlyInside()
        {
            Assert.True(_manager.IsInTempDirectory(Path.Combine(_tempDir, "a.jpg")));
            Assert.False(_manager.IsInTempDirectory(Path.Combine(_tempDir, "sub", "a.jpg")));
            Assert.False(_manager.IsInTempDirectory(Path.Combine(_sandbox.Root, "a.jpg")));
        }

        [Fact]
        public async Task PrepareTempDirectory_EmptiesPreviousResults()
        {
            var file = TestSandbox.CreateFile(_sandbox.CreateFolder("source"), "a.jpg");
            await _manager.CopySearchResultsAsync(new List<string> { file });

            var path = _manager.PrepareTempDirectory();

            Assert.Equal(_tempDir, path);
            Assert.True(Directory.Exists(_tempDir));
            Assert.Empty(Directory.GetFiles(_tempDir));
        }

        [Fact]
        public void CreateReadmeFile_WritesQueryAndCount()
        {
            _manager.CreateReadmeFile("cat | dog", 3);

            var text = File.ReadAllText(Path.Combine(_tempDir, "README.txt"));
            Assert.Contains("cat | dog", text);
            Assert.Contains("Files Found: 3", text);
        }

        [Fact]
        public void CleanupTempDirectory_DeletesDirectory()
        {
            _manager.CleanupTempDirectory();

            Assert.False(Directory.Exists(_tempDir));
        }
    }
}
