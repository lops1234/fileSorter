using System.Linq;
using System.Windows;
using Xunit;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace FileTagger.Tests.Services
{
    public class ShellDropTargetTests
    {
        [Fact]
        public void ReadFilePaths_ReturnsEverySelectedFile_WellPastExplorerVerbLimits()
        {
            var paths = Enumerable.Range(1, 1000)
                .Select(i => $@"C:\Some Folder\sub dir\file {i:D4} with spaces.jpg")
                .ToArray();
            var data = new DataObject(DataFormats.FileDrop, paths);

            var result = ShellDropTarget.ReadFilePaths((ComTypes.IDataObject)data);

            Assert.Equal(paths, result);
        }

        [Theory]
        [InlineData("-Embedding", true)]
        [InlineData("/Embedding", true)]
        [InlineData("--manage-tags", false)]
        public void IsEmbeddingLaunch_DetectsComServerStart(string arg, bool expected)
        {
            Assert.Equal(expected, ShellDropTarget.IsEmbeddingLaunch(new[] { arg }));
        }
    }
}
