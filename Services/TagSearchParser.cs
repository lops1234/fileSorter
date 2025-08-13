using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FileTagger.Services
{
    /// <summary>
    /// Parses and evaluates complex tag search queries
    /// Supports: AND (&), OR (|), grouping with (), exclusion with -, and spaces for AND
    /// Examples: "tag1 tag2", "tag1 & tag2", "tag1 | tag2", "(tag1 | tag2) & tag3", "-excluded tag1"
    /// </summary>
    public class TagSearchParser
    {
        public class SearchResult
        {
            public bool IsValid { get; set; }
            public string ErrorMessage { get; set; } = string.Empty;
            public List<string> RequiredTags { get; set; } = new List<string>();
            public List<string> ExcludedTags { get; set; } = new List<string>();
            public string NormalizedQuery { get; set; } = string.Empty;
        }

        /// <summary>
        /// Parses a search query and returns structured information
        /// </summary>
        public static SearchResult ParseQuery(string query)
        {
            var result = new SearchResult();
            
            if (string.IsNullOrWhiteSpace(query))
            {
                result.IsValid = true;
                return result;
            }

            try
            {
                // Normalize the query
                var normalizedQuery = NormalizeQuery(query.Trim());
                result.NormalizedQuery = normalizedQuery;

                // Extract all tags from the query
                var tokens = TokenizeQuery(normalizedQuery);
                
                // Validate parentheses balance
                if (!AreParenthesesBalanced(tokens))
                {
                    result.ErrorMessage = "Unbalanced parentheses in search query";
                    return result;
                }

                // Extract required and excluded tags
                ExtractTags(tokens, result);
                
                result.IsValid = true;
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Invalid search query: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// Evaluates if a file's tags match the search query
        /// </summary>
        public static bool EvaluateQuery(string query, List<string> fileTags)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;

            var parseResult = ParseQuery(query);
            if (!parseResult.IsValid)
                return false;

            // Quick check for excluded tags
            if (parseResult.ExcludedTags.Any(excludedTag => 
                fileTags.Any(fileTag => string.Equals(fileTag, excludedTag, StringComparison.OrdinalIgnoreCase))))
            {
                return false;
            }

            // If no specific logic, evaluate the full expression
            return EvaluateExpression(parseResult.NormalizedQuery, fileTags);
        }

        private static string NormalizeQuery(string query)
        {
            // Replace spaces with & (AND) operator, but preserve spaces within parentheses and around operators
            var normalized = query;
            
            // First, protect existing operators and parentheses
            normalized = Regex.Replace(normalized, @"\s*&\s*", " & ");
            normalized = Regex.Replace(normalized, @"\s*\|\s*", " | ");
            normalized = Regex.Replace(normalized, @"\s*\(\s*", " ( ");
            normalized = Regex.Replace(normalized, @"\s*\)\s*", " ) ");
            
            // Split by spaces and rejoin with & where appropriate
            var parts = normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new List<string>();
            
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                result.Add(part);
                
                // Add & between tags (but not after operators or before closing parentheses)
                if (i < parts.Length - 1)
                {
                    var nextPart = parts[i + 1];
                    if (!IsOperator(part) && !IsOperator(nextPart) && 
                        part != "(" && nextPart != ")" && part != ")" && nextPart != "(")
                    {
                        result.Add("&");
                    }
                }
            }
            
            return string.Join(" ", result);
        }

        private static bool IsOperator(string token)
        {
            return token == "&" || token == "|" || token == "(" || token == ")";
        }

        private static List<string> TokenizeQuery(string query)
        {
            return query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        private static bool AreParenthesesBalanced(List<string> tokens)
        {
            int balance = 0;
            foreach (var token in tokens)
            {
                if (token == "(") balance++;
                else if (token == ")") balance--;
                if (balance < 0) return false;
            }
            return balance == 0;
        }

        private static void ExtractTags(List<string> tokens, SearchResult result)
        {
            foreach (var token in tokens)
            {
                if (IsOperator(token)) continue;

                if (token.StartsWith("-"))
                {
                    var excludedTag = token.Substring(1);
                    if (!string.IsNullOrWhiteSpace(excludedTag))
                    {
                        result.ExcludedTags.Add(excludedTag);
                    }
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        result.RequiredTags.Add(token);
                    }
                }
            }
        }

        private static bool EvaluateExpression(string expression, List<string> fileTags)
        {
            try
            {
                var tokens = TokenizeQuery(expression);
                return EvaluateTokens(tokens, fileTags);
            }
            catch
            {
                return false;
            }
        }

        private static bool EvaluateTokens(List<string> tokens, List<string> fileTags)
        {
            // Simple recursive descent parser for boolean expressions
            var index = 0;
            return EvaluateOrExpression(tokens, ref index, fileTags);
        }

        private static bool EvaluateOrExpression(List<string> tokens, ref int index, List<string> fileTags)
        {
            bool result = EvaluateAndExpression(tokens, ref index, fileTags);

            while (index < tokens.Count && tokens[index] == "|")
            {
                index++; // skip |
                bool right = EvaluateAndExpression(tokens, ref index, fileTags);
                result = result || right;
            }

            return result;
        }

        private static bool EvaluateAndExpression(List<string> tokens, ref int index, List<string> fileTags)
        {
            bool result = EvaluatePrimary(tokens, ref index, fileTags);

            while (index < tokens.Count && tokens[index] == "&")
            {
                index++; // skip &
                bool right = EvaluatePrimary(tokens, ref index, fileTags);
                result = result && right;
            }

            return result;
        }

        private static bool EvaluatePrimary(List<string> tokens, ref int index, List<string> fileTags)
        {
            if (index >= tokens.Count)
                return false;

            var token = tokens[index++];

            if (token == "(")
            {
                bool result = EvaluateOrExpression(tokens, ref index, fileTags);
                if (index < tokens.Count && tokens[index] == ")")
                    index++; // skip )
                return result;
            }
            else if (token.StartsWith("-"))
            {
                // Exclusion - should not be present
                var excludedTag = token.Substring(1);
                return !fileTags.Any(tag => string.Equals(tag, excludedTag, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                // Regular tag - should be present
                return fileTags.Any(tag => string.Equals(tag, token, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>
        /// Formats a search query example for display
        /// </summary>
        public static string GetSearchSyntaxHelp()
        {
            return @"Search Syntax:
• Space or & = AND: 'tag1 tag2' or 'tag1 & tag2'
• | = OR: 'tag1 | tag2'
• () = Grouping: '(tag1 | tag2) & tag3'
• - = Exclude: '-unwanted tag1'
• Examples: 'photo work', 'image | video', '(urgent | important) & -archived'";
        }
    }
}
