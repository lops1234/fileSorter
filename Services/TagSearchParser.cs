using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FileTagger.Services
{
    /// <summary>
    /// Parses and evaluates complex tag search queries
    /// Supports: AND (&), OR (|), grouping with (), exclusion with -, spaces for AND, and quoted tags
    /// Examples: "tag1 tag2", "tag1 & tag2", "tag1 | tag2", "(tag1 | tag2) & tag3", "-excluded tag1", "'tag with spaces'"
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
            // Tokenize first to properly handle quoted strings
            var tokens = TokenizeQuery(query);
            var result = new List<string>();
            
            for (int i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                result.Add(token);
                
                // Add & between tags (but not after operators or before closing parentheses)
                if (i < tokens.Count - 1)
                {
                    var nextToken = tokens[i + 1];
                    if (!IsOperator(token) && !IsOperator(nextToken) && 
                        token != "(" && nextToken != ")" && token != ")" && nextToken != "(")
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

        /// <summary>
        /// Tokenizes a query, respecting quoted strings as single tokens
        /// Supports both single quotes ('tag with spaces') and double quotes ("tag with spaces")
        /// Quoted tokens are preserved WITH their quotes to maintain identity during re-tokenization
        /// </summary>
        private static List<string> TokenizeQuery(string query)
        {
            var tokens = new List<string>();
            var currentToken = new System.Text.StringBuilder();
            char? inQuote = null;
            
            for (int i = 0; i < query.Length; i++)
            {
                char c = query[i];
                
                // Handle quote characters
                if ((c == '\'' || c == '"') && inQuote == null)
                {
                    // Starting a quoted section - include the opening quote
                    inQuote = c;
                    currentToken.Append(c);
                    continue;
                }
                else if (c == inQuote)
                {
                    // Ending a quoted section - include the closing quote
                    currentToken.Append(c);
                    tokens.Add(currentToken.ToString());
                    currentToken.Clear();
                    inQuote = null;
                    continue;
                }
                
                // If inside quotes, add character to current token
                if (inQuote != null)
                {
                    currentToken.Append(c);
                    continue;
                }
                
                // Handle spaces (token separators) outside quotes
                if (c == ' ')
                {
                    if (currentToken.Length > 0)
                    {
                        tokens.Add(currentToken.ToString());
                        currentToken.Clear();
                    }
                    continue;
                }
                
                // Handle operators as separate tokens
                if (c == '&' || c == '|' || c == '(' || c == ')')
                {
                    if (currentToken.Length > 0)
                    {
                        tokens.Add(currentToken.ToString());
                        currentToken.Clear();
                    }
                    tokens.Add(c.ToString());
                    continue;
                }
                
                // Regular character - add to current token
                currentToken.Append(c);
            }
            
            // Add any remaining token
            if (currentToken.Length > 0)
            {
                tokens.Add(currentToken.ToString());
            }
            
            return tokens;
        }

        /// <summary>
        /// Strips quotes from a tag name if present
        /// </summary>
        private static string StripQuotes(string tag)
        {
            if (string.IsNullOrEmpty(tag) || tag.Length < 2)
                return tag;
            
            if ((tag.StartsWith("'") && tag.EndsWith("'")) ||
                (tag.StartsWith("\"") && tag.EndsWith("\"")))
            {
                return tag.Substring(1, tag.Length - 2);
            }
            
            return tag;
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
                    var excludedTag = StripQuotes(token.Substring(1));
                    if (!string.IsNullOrWhiteSpace(excludedTag))
                    {
                        result.ExcludedTags.Add(excludedTag);
                    }
                }
                else
                {
                    var tagName = StripQuotes(token);
                    if (!string.IsNullOrWhiteSpace(tagName))
                    {
                        result.RequiredTags.Add(tagName);
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
                // Exclusion - should not be present (strip quotes from tag name)
                var excludedTag = StripQuotes(token.Substring(1));
                return !fileTags.Any(tag => string.Equals(tag, excludedTag, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                // Regular tag - should be present (strip quotes from tag name)
                var tagName = StripQuotes(token);
                return fileTags.Any(tag => string.Equals(tag, tagName, StringComparison.OrdinalIgnoreCase));
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
• Quotes for tags with spaces: 'AI generated' or ""my tag""
• Examples: 'photo work', 'image | video', '(urgent | important) & -archived', 'AI generated' & photo";
        }

        /// <summary>
        /// Wraps a tag in single quotes if it contains spaces
        /// </summary>
        public static string QuoteTagIfNeeded(string tagName)
        {
            if (string.IsNullOrEmpty(tagName))
                return tagName;
            
            // If the tag contains spaces, wrap it in single quotes
            if (tagName.Contains(' '))
            {
                return $"'{tagName}'";
            }
            
            return tagName;
        }
    }
}
