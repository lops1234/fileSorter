using FileTagger.Windows;
using Xunit;

namespace FileTagger.Tests.Services
{
    public class TagDisplayItemTests
    {
        [Theory]
        [InlineData(1, 1, "cat")]
        [InlineData(3, 3, "cat (all 3)")]
        [InlineData(2, 3, "cat (2/3)")]
        public void ToString_ShowsHowManySelectedFilesHaveTheTag(int fileCount, int totalFiles, string expected)
        {
            var item = new TagDisplayItem { TagName = "cat", FileCount = fileCount, TotalFiles = totalFiles };

            Assert.Equal(expected, item.ToString());
        }
    }
}
