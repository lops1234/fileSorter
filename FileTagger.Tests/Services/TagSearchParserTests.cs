using System.Collections.Generic;
using FileTagger.Services;
using Xunit;

namespace FileTagger.Tests.Services
{
    public class TagSearchParserTests
    {
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void ParseQuery_Empty_IsValidWithNoTags(string query)
        {
            var result = TagSearchParser.ParseQuery(query);

            Assert.True(result.IsValid);
            Assert.Empty(result.RequiredTags);
            Assert.Empty(result.ExcludedTags);
        }

        [Fact]
        public void ParseQuery_SpaceSeparatedTags_AreJoinedWithAnd()
        {
            var result = TagSearchParser.ParseQuery("photo work");

            Assert.True(result.IsValid);
            Assert.Equal("photo & work", result.NormalizedQuery);
            Assert.Equal(new[] { "photo", "work" }, result.RequiredTags);
        }

        [Fact]
        public void ParseQuery_ExtractsExcludedAndQuotedTags()
        {
            var result = TagSearchParser.ParseQuery("'AI generated' & -archived \"my tag\"");

            Assert.True(result.IsValid);
            Assert.Equal(new[] { "AI generated", "my tag" }, result.RequiredTags);
            Assert.Equal(new[] { "archived" }, result.ExcludedTags);
        }

        [Theory]
        [InlineData("(a | b")]
        [InlineData("a | b)")]
        [InlineData(")a(")]
        public void ParseQuery_UnbalancedParentheses_IsInvalid(string query)
        {
            var result = TagSearchParser.ParseQuery(query);

            Assert.False(result.IsValid);
            Assert.Contains("parentheses", result.ErrorMessage);
        }

        [Theory]
        [InlineData("cat", new[] { "cat" }, true)]
        [InlineData("cat", new[] { "CAT" }, true)]
        [InlineData("cat", new[] { "dog" }, false)]
        [InlineData("cat dog", new[] { "cat", "dog" }, true)]
        [InlineData("cat dog", new[] { "cat" }, false)]
        [InlineData("cat & dog", new[] { "cat", "dog" }, true)]
        [InlineData("cat | dog", new[] { "dog" }, true)]
        [InlineData("cat | dog", new[] { "bird" }, false)]
        [InlineData("-archived", new[] { "photo" }, true)]
        [InlineData("-archived photo", new[] { "photo", "archived" }, false)]
        [InlineData("(cat | dog) & outdoor", new[] { "dog", "outdoor" }, true)]
        [InlineData("(cat | dog) & outdoor", new[] { "dog" }, false)]
        [InlineData("(urgent | important) & -archived", new[] { "urgent" }, true)]
        [InlineData("(urgent | important) & -archived", new[] { "urgent", "archived" }, false)]
        [InlineData("'AI generated' & photo", new[] { "AI generated", "photo" }, true)]
        [InlineData("'AI generated'", new[] { "AI", "generated" }, false)]
        public void EvaluateQuery_MatchesExpectedFiles(string query, string[] fileTags, bool expected)
        {
            Assert.Equal(expected, TagSearchParser.EvaluateQuery(query, new List<string>(fileTags)));
        }

        [Fact]
        public void EvaluateQuery_AndBindsTighterThanOr()
        {
            // a | (b & c)
            Assert.True(TagSearchParser.EvaluateQuery("a | b c", new List<string> { "a" }));
            Assert.False(TagSearchParser.EvaluateQuery("a | b c", new List<string> { "b" }));
            Assert.True(TagSearchParser.EvaluateQuery("a | b c", new List<string> { "b", "c" }));
        }

        [Fact]
        public void EvaluateQuery_EmptyQuery_MatchesEverything()
        {
            Assert.True(TagSearchParser.EvaluateQuery("", new List<string>()));
        }

        [Fact]
        public void EvaluateQuery_InvalidQuery_MatchesNothing()
        {
            Assert.False(TagSearchParser.EvaluateQuery("(cat", new List<string> { "cat" }));
        }

        [Theory]
        [InlineData("cat", "cat")]
        [InlineData("AI generated", "'AI generated'")]
        [InlineData("", "")]
        public void QuoteTagIfNeeded_QuotesOnlyTagsWithSpaces(string tag, string expected)
        {
            Assert.Equal(expected, TagSearchParser.QuoteTagIfNeeded(tag));
        }
    }
}
