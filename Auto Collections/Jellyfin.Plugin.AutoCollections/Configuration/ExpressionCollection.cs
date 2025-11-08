using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.AutoCollections.Configuration
{
    public enum CriteriaType
    {
        Title = 0,
        Genre = 1,
        Studio = 2,
        Actor = 3,
        Director = 4,
        Movie = 5,
        Show = 6,
        Tag = 7,
        ParentalRating = 8,
        CommunityRating = 9,
        CriticsRating = 10,
        ProductionLocation = 11,
        AudioLanguage = 12,
        Subtitle = 13,
        Year = 14,
        CustomRating = 15,
        ReleaseDate = 16,
        AddedDate = 17,
        EpisodeAirDate = 18,
        Unplayed = 19,
        Watched = 20,
        Filename = 21
    }

    public enum TokenType
    {
        Criteria,
        String,
        And,
        Or,
        Not,
        OpenParen,
        CloseParen,
        EOF
    }

    public class Token
    {
        public TokenType Type { get; set; }
        public string Value { get; set; }
        public CriteriaType? CriteriaType { get; set; }

        public Token(TokenType type, string value = null)
        {
            Type = type;
            Value = value;
        }

        public override string ToString()
        {
            if (Type == TokenType.String)
                return $"\"{Value}\"";
            else if (Type == TokenType.Criteria)
                return CriteriaType.ToString().ToUpper();
            else
                return Type.ToString().ToUpper();
        }
    }

    public abstract class ExpressionNode
    {
        public abstract bool Evaluate(Func<CriteriaType, string, bool> criteriaMatchFunc);
        public abstract override string ToString();
    }

    public class CriteriaNode : ExpressionNode
    {
        public CriteriaType CriteriaType { get; set; }
        public string Value { get; set; }

        public CriteriaNode(CriteriaType criteriaType, string value)
        {
            CriteriaType = criteriaType;
            Value = value;
        }

        public override bool Evaluate(Func<CriteriaType, string, bool> criteriaMatchFunc)
        {
            return criteriaMatchFunc(CriteriaType, Value);
        }

        public override string ToString()
        {
            if (CriteriaType == CriteriaType.Movie || CriteriaType == CriteriaType.Show ||
                CriteriaType == CriteriaType.Unplayed || CriteriaType == CriteriaType.Watched)
            {
                return $"{CriteriaType.ToString().ToUpper()}";
            }
            return $"{CriteriaType.ToString().ToUpper()} \"{Value}\"";
        }
    }

    public class AndNode : ExpressionNode
    {
        public ExpressionNode Left { get; set; }
        public ExpressionNode Right { get; set; }

        public AndNode(ExpressionNode left, ExpressionNode right)
        {
            Left = left;
            Right = right;
        }

        public override bool Evaluate(Func<CriteriaType, string, bool> criteriaMatchFunc)
        {
            return Left.Evaluate(criteriaMatchFunc) && Right.Evaluate(criteriaMatchFunc);
        }

        public override string ToString()
        {
            return $"({Left} AND {Right})";
        }
    }

    public class OrNode : ExpressionNode
    {
        public ExpressionNode Left { get; set; }
        public ExpressionNode Right { get; set; }

        public OrNode(ExpressionNode left, ExpressionNode right)
        {
            Left = left;
            Right = right;
        }

        public override bool Evaluate(Func<CriteriaType, string, bool> criteriaMatchFunc)
        {
            return Left.Evaluate(criteriaMatchFunc) || Right.Evaluate(criteriaMatchFunc);
        }

        public override string ToString()
        {
            return $"({Left} OR {Right})";
        }
    }

    public class NotNode : ExpressionNode
    {
        public ExpressionNode Child { get; set; }

        public NotNode(ExpressionNode child)
        {
            Child = child;
        }

        public override bool Evaluate(Func<CriteriaType, string, bool> criteriaMatchFunc)
        {
            return !Child.Evaluate(criteriaMatchFunc);
        }

        public override string ToString()
        {
            return $"NOT {Child}";
        }
    }    public class ExpressionCollection
    {
        public string CollectionName { get; set; }
        public string Expression { get; set; }
        public bool CaseSensitive { get; set; }

        [System.Xml.Serialization.XmlIgnore]
        public ExpressionNode ParsedExpression { get; set; }

        [System.Xml.Serialization.XmlIgnore]
        public List<string> ParseErrors { get; set; }

        public ExpressionCollection()
        {
            CollectionName = "Auto Collection";
            Expression = string.Empty;
            CaseSensitive = false;
            ParseErrors = new List<string>();
        }

        public ExpressionCollection(string collectionName, string expression, bool caseSensitive = false)
        {
            CollectionName = collectionName;
            Expression = expression;
            CaseSensitive = caseSensitive;
            ParseErrors = new List<string>();

            ParseExpression();
        }

        public bool ParseExpression()
        {
            ParseErrors.Clear();
            if (string.IsNullOrWhiteSpace(Expression))
            {
                ParseErrors.Add("Expression cannot be empty");
                return false;
            }

            try
            {
                var tokens = TokenizeExpression(Expression);
                var position = 0;
                ParsedExpression = ParseExpressionTree(tokens, ref position);
                return true;
            }
            catch (Exception ex)
            {
                ParseErrors.Add($"Error parsing expression: {ex.Message}");
                return false;
            }
        }

        private List<Token> TokenizeExpression(string expression)
        {
            List<Token> tokens = new();
            int position = 0;

            while (position < expression.Length)
            {
                while (position < expression.Length && char.IsWhiteSpace(expression[position]))
                    position++;

                if (position >= expression.Length)
                    break;

                if (TryMatchOperator(expression, ref position, "AND", out var andToken))
                {
                    tokens.Add(andToken);
                    continue;
                }

                if (TryMatchOperator(expression, ref position, "OR", out var orToken))
                {
                    tokens.Add(orToken);
                    continue;
                }

                if (TryMatchOperator(expression, ref position, "NOT", out var notToken))
                {
                    tokens.Add(notToken);
                    continue;
                }

                if (TryMatchCriteria(expression, ref position, "TITLE", out var titleToken))
                {
                    tokens.Add(titleToken);
                    continue;
                }

                if (TryMatchCriteria(expression, ref position, "GENRE", out var genreToken))
                {
                    tokens.Add(genreToken);
                    continue;
                }

                if (TryMatchCriteria(expression, ref position, "STUDIO", out var studioToken))
                {
                    tokens.Add(studioToken);
                    continue;
                }

                if (TryMatchCriteria(expression, ref position, "ACTOR", out var actorToken))
                {
                    tokens.Add(actorToken);
                    continue;
                }

                if (TryMatchCriteria(expression, ref position, "DIRECTOR", out var directorToken))
                {
                    tokens.Add(directorToken);
                    continue;
                }

                if (TryMatchCriteria(expression, ref position, "MOVIE", out var movieToken))
                {
                    tokens.Add(movieToken);
                    continue;
                }
                if (TryMatchCriteria(expression, ref position, "SHOW", out var showToken))
                {
                    tokens.Add(showToken);
                    continue;
                }

                if (TryMatchCriteria(expression, ref position, "TAG", out var tagToken))
                {
                    tokens.Add(tagToken);
                    continue;
                }

                if (TryMatchCriteria(expression, ref position, "PARENTALRATING", out var parentalRatingToken))
                {
                    tokens.Add(parentalRatingToken);
                    continue;
                }

                if (TryMatchCriteria(expression, ref position, "PARENTAL", out var parentalToken))
                {
                    tokens.Add(parentalToken);
                    continue;
                }

                if (TryMatchCriteria(expression, ref position, "RATING", out var ratingToken))
                {
                    tokens.Add(ratingToken);
                    continue;
                }

                if (TryMatchCriteria(expression, ref position, "COMMUNITYRATING", out var commRatingToken))
                {
                    tokens.Add(commRatingToken);
                    continue;
                }

                if (TryMatchCriteria(expression, ref position, "USERRATING", out var userRatingToken))
                {
                    tokens.Add(userRatingToken);
                    continue;
                }

                if (TryMatchCriteria(expression, ref position, "CRITICSRATING", out var criticsRatingToken))
                {
                    tokens.Add(criticsRatingToken);
                    continue;
                }

                if (TryMatchCriteria(expression, ref position, "CRITICS", out var criticsToken))
                {
                    tokens.Add(criticsToken);
                    continue;
                }

                if (TryMatchCriteria(expression, ref position, "PRODUCTIONLOCATION", out var prodLocationToken))
                {
                    tokens.Add(prodLocationToken);
                    continue;
                }

                if (TryMatchCriteria(expression, ref position, "LOCATION", out var locationToken))
                {
                    tokens.Add(locationToken);
                    continue;
                }

                if (TryMatchCriteria(expression, ref position, "COUNTRY", out var countryToken))
                {
                    tokens.Add(countryToken);
                    continue;
                }

                if (TryMatchCriteria(expression, ref position, "LANG", out var langToken))
                {
                    tokens.Add(langToken);
                    continue;
                }
                if (TryMatchCriteria(expression, ref position, "SUB", out var subToken))
                {
                    tokens.Add(subToken);
                    continue;
                }

                if (TryMatchCriteria(expression, ref position, "YEAR", out var yearToken))
                {
                    tokens.Add(yearToken);
                    continue;
                }

                if (TryMatchCriteria(expression, ref position, "CUSTOMRATING", out var customRatingToken))
                {
                    tokens.Add(customRatingToken);
                    continue;
                }
                if (TryMatchCriteria(expression, ref position, "CUSTOM", out var customToken))
                {
                    tokens.Add(customToken);
                    continue;
                }

                if (TryMatchCriteria(expression, ref position, "RELEASEDATE", out var releaseDateToken))
                {
                    tokens.Add(releaseDateToken);
                    continue;
                }
                if (TryMatchCriteria(expression, ref position, "RELEASE", out var releaseToken))
                {
                    tokens.Add(releaseToken);
                    continue;
                }
                if (TryMatchCriteria(expression, ref position, "ADDEDDATE", out var addedDateToken))
                {
                    tokens.Add(addedDateToken);
                    continue;
                }
                if (TryMatchCriteria(expression, ref position, "ADDED", out var addedToken))
                {
                    tokens.Add(addedToken);
                    continue;
                }
                if (TryMatchCriteria(expression, ref position, "EPISODEAIRDATE", out var episodeAirDateToken))
                {
                    tokens.Add(episodeAirDateToken);
                    continue;
                }
                if (TryMatchCriteria(expression, ref position, "EPISODEAIR", out var episodeAirToken))
                {
                    tokens.Add(episodeAirToken);
                    continue;
                }
                if (TryMatchCriteria(expression, ref position, "LASTAIR", out var lastAirToken))
                {
                    tokens.Add(lastAirToken);
                    continue;
                }

                if (TryMatchCriteria(expression, ref position, "UNPLAYED", out var unplayedToken))
                {
                    tokens.Add(unplayedToken);
                    continue;
                }
                if (TryMatchCriteria(expression, ref position, "UNWATCHED", out var unwatchedToken))
                {
                    tokens.Add(unwatchedToken);
                    continue;
                }
                if (TryMatchCriteria(expression, ref position, "WATCHED", out var watchedToken))
                {
                    tokens.Add(watchedToken);
                    continue;
                }

                if (TryMatchCriteria(expression, ref position, "FILENAME", out var filenameToken))
                {
                    tokens.Add(filenameToken);
                    continue;
                }

                if (expression[position] == '(')
                {
                    tokens.Add(new Token(TokenType.OpenParen));
                    position++;
                    continue;
                }

                if (expression[position] == ')')
                {
                    tokens.Add(new Token(TokenType.CloseParen));
                    position++;
                    continue;
                }

                if (expression[position] == '"')
                {
                    position++;
                    int startPos = position;

                    while (position < expression.Length && expression[position] != '"')
                        position++;

                    if (position >= expression.Length)
                        throw new Exception("Unterminated string literal");

                    string value = expression.Substring(startPos, position - startPos);
                    tokens.Add(new Token(TokenType.String, value));

                    position++;
                    continue;
                }

                throw new Exception($"Unexpected character at position {position}: {expression[position]}");
            }

            tokens.Add(new Token(TokenType.EOF));
            return tokens;
        }

        private bool TryMatchOperator(string expression, ref int position, string op, out Token token)
        {
            token = null;

            if (position + op.Length <= expression.Length &&
                expression.Substring(position, op.Length).Equals(op, StringComparison.OrdinalIgnoreCase))
            {
                if (position + op.Length == expression.Length ||
                    char.IsWhiteSpace(expression[position + op.Length]) ||
                    expression[position + op.Length] == '(' ||
                    expression[position + op.Length] == ')')
                {
                    position += op.Length;

                    switch (op.ToUpper())
                    {
                        case "AND":
                            token = new Token(TokenType.And);
                            break;
                        case "OR":
                            token = new Token(TokenType.Or);
                            break;
                        case "NOT":
                            token = new Token(TokenType.Not);
                            break;
                    }

                    return true;
                }
            }

            return false;
        }
        private bool TryMatchCriteria(string expression, ref int position, string criteria, out Token token)
        {
            token = null;

            if (position + criteria.Length <= expression.Length &&
                expression.Substring(position, criteria.Length).Equals(criteria, StringComparison.OrdinalIgnoreCase))
            {
                if (position + criteria.Length == expression.Length ||
                    char.IsWhiteSpace(expression[position + criteria.Length]) ||
                    expression[position + criteria.Length] == '(' ||
                    expression[position + criteria.Length] == ')')
                {
                    position += criteria.Length;

                    token = new Token(TokenType.Criteria);
                    switch (criteria.ToUpper())
                    {
                        case "TITLE":
                            token.CriteriaType = Configuration.CriteriaType.Title;
                            break;
                        case "GENRE":
                            token.CriteriaType = Configuration.CriteriaType.Genre;
                            break;
                        case "STUDIO":
                            token.CriteriaType = Configuration.CriteriaType.Studio;
                            break;
                        case "ACTOR":
                            token.CriteriaType = Configuration.CriteriaType.Actor;
                            break;
                        case "TAG":
                            token.CriteriaType = Configuration.CriteriaType.Tag;
                            break;
                        case "PARENTALRATING":
                        case "PARENTAL":
                        case "RATING":
                            token.CriteriaType = Configuration.CriteriaType.ParentalRating;
                            break;
                        case "COMMUNITYRATING":
                        case "USERRATING":
                            token.CriteriaType = Configuration.CriteriaType.CommunityRating;
                            break;
                        case "CRITICSRATING":
                        case "CRITICS":
                            token.CriteriaType = Configuration.CriteriaType.CriticsRating;
                            break;
                        case "PRODUCTIONLOCATION":
                        case "LOCATION":
                        case "COUNTRY":
                            token.CriteriaType = Configuration.CriteriaType.ProductionLocation;
                            break;
                        case "DIRECTOR":
                            token.CriteriaType = Configuration.CriteriaType.Director;
                            break;
                        case "MOVIE":
                            token.CriteriaType = Configuration.CriteriaType.Movie;
                            break;
                        case "SHOW":
                            token.CriteriaType = Configuration.CriteriaType.Show;
                            break;
                        case "LANG":
                            token.CriteriaType = Configuration.CriteriaType.AudioLanguage;
                            break;
                        case "SUB":
                            token.CriteriaType = Configuration.CriteriaType.Subtitle;
                            break;
                        case "YEAR":
                            token.CriteriaType = Configuration.CriteriaType.Year;
                            break;
                        case "CUSTOMRATING":
                        case "CUSTOM":
                            token.CriteriaType = Configuration.CriteriaType.CustomRating;
                            break;
                        case "FILENAME":
                            token.CriteriaType = Configuration.CriteriaType.Filename;
                            break;
                        case "RELEASEDATE":
                        case "RELEASE":
                            token.CriteriaType = Configuration.CriteriaType.ReleaseDate;
                            break;
                        case "ADDEDDATE":
                        case "ADDED":
                            token.CriteriaType = Configuration.CriteriaType.AddedDate;
                            break;
                        case "EPISODEAIRDATE":
                        case "EPISODEAIR":
                        case "LASTAIR":
                            token.CriteriaType = Configuration.CriteriaType.EpisodeAirDate;
                            break;
                        case "UNPLAYED":
                        case "UNWATCHED":
                            token.CriteriaType = Configuration.CriteriaType.Unplayed;
                            break;
                        case "WATCHED":
                            token.CriteriaType = Configuration.CriteriaType.Watched;
                            break;
                    }

                    return true;
                }
            }

            return false;
        }

        private ExpressionNode ParseExpressionTree(List<Token> tokens, ref int position)
        {
            return ParseOrExpression(tokens, ref position);
        }

        private ExpressionNode ParseOrExpression(List<Token> tokens, ref int position)
        {
            var left = ParseAndExpression(tokens, ref position);

            while (position < tokens.Count && tokens[position].Type == TokenType.Or)
            {
                position++;
                var right = ParseAndExpression(tokens, ref position);
                left = new OrNode(left, right);
            }

            return left;
        }

        private ExpressionNode ParseAndExpression(List<Token> tokens, ref int position)
        {
            var left = ParseNotExpression(tokens, ref position);

            while (position < tokens.Count && tokens[position].Type == TokenType.And)
            {
                position++;
                var right = ParseNotExpression(tokens, ref position);
                left = new AndNode(left, right);
            }

            return left;
        }

        private ExpressionNode ParseNotExpression(List<Token> tokens, ref int position)
        {
            if (position < tokens.Count && tokens[position].Type == TokenType.Not)
            {
                position++;
                var child = ParsePrimaryExpression(tokens, ref position);
                return new NotNode(child);
            }

            return ParsePrimaryExpression(tokens, ref position);
        }

        private ExpressionNode ParsePrimaryExpression(List<Token> tokens, ref int position)
        {
            if (position >= tokens.Count)
                throw new Exception("Unexpected end of expression");

            if (tokens[position].Type == TokenType.OpenParen)
            {
                position++;
                var node = ParseExpressionTree(tokens, ref position);

                if (position >= tokens.Count || tokens[position].Type != TokenType.CloseParen)
                    throw new Exception("Missing closing parenthesis");

                position++;
                return node;
            }

            if (tokens[position].Type == TokenType.Criteria)
            {
                var criteriaToken = tokens[position++];

                if (criteriaToken.CriteriaType == CriteriaType.Movie || criteriaToken.CriteriaType == CriteriaType.Show ||
                    criteriaToken.CriteriaType == CriteriaType.Unplayed || criteriaToken.CriteriaType == CriteriaType.Watched)
                {
                    return new CriteriaNode(criteriaToken.CriteriaType.Value, string.Empty);
                }

                if (position >= tokens.Count || tokens[position].Type != TokenType.String)
                    throw new Exception($"Expected string after {criteriaToken}");

                var stringToken = tokens[position++];
                return new CriteriaNode(criteriaToken.CriteriaType.Value, stringToken.Value);
            }

            throw new Exception($"Unexpected token: {tokens[position].Type}");
        }
    }
}