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

        [Theory]
        [InlineData("(Futa | Futas) Femdom", "( Futa | Futas ) & Femdom")]
        [InlineData("Femdom (Futa | Futas)", "Femdom & ( Futa | Futas )")]
        [InlineData("(a | b) (c | d)", "( a | b ) & ( c | d )")]
        [InlineData("(a | b) -c", "( a | b ) & -c")]
        public void ParseQuery_GroupNextToTagOrGroup_IsJoinedWithAnd(string query, string expected)
        {
            Assert.Equal(expected, TagSearchParser.ParseQuery(query).NormalizedQuery);
        }

        [Theory]
        [InlineData(new[] { "Futa", "Femdom" }, true)]
        [InlineData(new[] { "Futas", "Femdom" }, true)]
        [InlineData(new[] { "Futa" }, false)]
        [InlineData(new[] { "Futas" }, false)]
        [InlineData(new[] { "Femdom" }, false)]
        public void EvaluateQuery_GroupFollowedBySpaceAndTag_RequiresBoth(string[] fileTags, bool expected)
        {
            Assert.Equal(expected, TagSearchParser.EvaluateQuery("(Futa | Futas) Femdom", new List<string>(fileTags)));
            Assert.Equal(expected, TagSearchParser.EvaluateQuery("Femdom (Futa | Futas)", new List<string>(fileTags)));
        }

        [Fact]
        public void EvaluateQuery_TwoAdjacentGroups_RequiresBoth()
        {
            Assert.True(TagSearchParser.EvaluateQuery("(a | b) (c | d)", new List<string> { "b", "c" }));
            Assert.False(TagSearchParser.EvaluateQuery("(a | b) (c | d)", new List<string> { "a", "b" }));
        }

        [Fact]
        public void ParseQuery_ExtractsExclusiveTags()
        {
            var result = TagSearchParser.ParseQuery("*cat *'AI generated' -archived");

            Assert.True(result.IsValid);
            Assert.True(result.IsExclusive);
            Assert.Equal(new[] { "cat", "AI generated" }, result.ExclusiveTags);
            Assert.Equal(new[] { "cat", "AI generated" }, result.RequiredTags);
            Assert.Equal(new[] { "archived" }, result.ExcludedTags);
        }

        [Theory]
        [InlineData("*cat", new[] { "cat" }, true)]
        [InlineData("*cat", new[] { "CAT" }, true)]
        [InlineData("*cat", new[] { "cat", "dog" }, false)]
        [InlineData("*cat", new string[0], false)]
        [InlineData("*cat *dog", new[] { "cat", "dog" }, true)]
        [InlineData("*cat *dog", new[] { "cat" }, false)]
        [InlineData("*cat *dog", new[] { "cat", "dog", "bird" }, false)]
        [InlineData("*cat | *dog", new[] { "cat" }, true)]
        [InlineData("*cat | *dog", new[] { "cat", "dog" }, true)]
        [InlineData("*cat | *dog", new[] { "dog", "bird" }, false)]
        [InlineData("*cat dog", new[] { "cat", "dog" }, true)]
        [InlineData("*cat dog", new[] { "cat", "dog", "bird" }, false)]
        [InlineData("*'AI generated'", new[] { "AI generated" }, true)]
        [InlineData("*'AI generated'", new[] { "AI generated", "photo" }, false)]
        [InlineData("cat", new[] { "cat", "dog" }, true)]
        public void EvaluateQuery_ExclusiveTags_RejectFilesWithOtherTags(string query, string[] fileTags, bool expected)
        {
            Assert.Equal(expected, TagSearchParser.EvaluateQuery(query, new List<string>(fileTags)));
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
