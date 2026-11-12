namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>A minimal C++ tokenizer: identifiers, single-character punctuation, and everything else.</summary>
internal struct CudaLexer(string source)
{
    private int _position;
    private int _start;
    private bool _atLineStart = true;

    public CudaTokenKind Kind { get; private set; }

    public readonly ReadOnlySpan<char> Text => source.AsSpan(_start, _position - _start);

    public readonly bool IsIdentifier(string text) =>
        Kind == CudaTokenKind.Identifier &&
        _position - _start == text.Length &&
        string.CompareOrdinal(source, _start, text, 0, text.Length) == 0;

    public readonly bool IsPunctuation(char c) => Kind == CudaTokenKind.Punctuation && source[_start] == c;

    /// <summary>Moves to the next token. Returns <see langword="false"/> at the end of the source.</summary>
    public bool Next()
    {
        while (SkipTrivia())
        {
        }

        if (_position >= source.Length)
        {
            return false;
        }

        _start = _position;
        _atLineStart = false;
        var c = source[_position];
        if (IsIdentifierStart(c))
        {
            ReadIdentifierOrPrefixedLiteral();
        }
        else if (char.IsDigit(c))
        {
            Kind = CudaTokenKind.Other;
            while (_position < source.Length && IsNumberPart(source[_position]))
            {
                _position++;
            }
        }
        else if (c is '"' or '\'')
        {
            Kind = CudaTokenKind.Other;
            SkipQuoted(c);
        }
        else
        {
            Kind = CudaTokenKind.Punctuation;
            _position++;
        }

        return true;
    }

    /// <summary>
    /// With the current token on <paramref name="open"/>, moves to its matching <paramref name="close"/>.
    /// Returns <see langword="false"/> when the source ends first.
    /// </summary>
    public bool SkipBalanced(char open, char close)
    {
        var depth = 1;
        while (Next())
        {
            if (IsPunctuation(open))
            {
                depth++;
            }
            else if (IsPunctuation(close) && --depth == 0)
            {
                return true;
            }
        }

        return false;
    }

    private void ReadIdentifierOrPrefixedLiteral()
    {
        while (_position < source.Length && IsIdentifierPart(source[_position]))
        {
            _position++;
        }

        Kind = CudaTokenKind.Identifier;
        if (_position >= source.Length || source[_position] is not ('"' or '\'') || !IsLiteralPrefix(Text))
        {
            return;
        }

        // u8"...", L'x', R"delim(...)delim": the prefix belongs to the literal.
        Kind = CudaTokenKind.Other;
        if (source[_position - 1] == 'R' && source[_position] == '"')
        {
            SkipRawString();
        }
        else
        {
            SkipQuoted(source[_position]);
        }
    }

    private static bool IsLiteralPrefix(ReadOnlySpan<char> prefix) =>
        prefix is "L" or "u" or "U" or "u8" or "R" or "LR" or "uR" or "UR" or "u8R";

    private void SkipQuoted(char quote)
    {
        _position++;
        while (_position < source.Length && source[_position] != quote && source[_position] != '\n')
        {
            _position += source[_position] == '\\' ? 2 : 1;
        }

        _position = Math.Min(_position + 1, source.Length);
    }

    private void SkipRawString()
    {
        var open = source.IndexOf('(', _position);
        if (open < 0)
        {
            _position = source.Length;
            return;
        }

        // The literal ends at ')' + the delimiter between the quote and '(' + '"'.
        var delimiter = source.AsSpan(_position + 1, open - _position - 1);
        for (var close = source.IndexOf(')', open); close >= 0; close = source.IndexOf(')', close + 1))
        {
            var rest = source.AsSpan(close + 1);
            if (rest.StartsWith(delimiter) && rest.Length > delimiter.Length && rest[delimiter.Length] == '"')
            {
                _position = close + delimiter.Length + 2;
                return;
            }
        }

        _position = source.Length;
    }

    /// <summary>Skips one whitespace character, comment or preprocessor line. Returns whether it skipped one.</summary>
    private bool SkipTrivia()
    {
        if (_position >= source.Length)
        {
            return false;
        }

        var c = source[_position];
        if (char.IsWhiteSpace(c))
        {
            _atLineStart |= c == '\n';
            _position++;
            return true;
        }

        if (c == '#' && _atLineStart)
        {
            SkipToEndOfLine(honorContinuation: true);
            return true;
        }

        if (c != '/' || _position + 1 >= source.Length)
        {
            return false;
        }

        if (source[_position + 1] == '/')
        {
            SkipToEndOfLine(honorContinuation: false);
            return true;
        }

        if (source[_position + 1] == '*')
        {
            var end = source.IndexOf("*/", _position + 2, StringComparison.Ordinal);
            _position = end < 0 ? source.Length : end + 2;
            return true;
        }

        return false;
    }

    private void SkipToEndOfLine(bool honorContinuation)
    {
        while (_position < source.Length && source[_position] != '\n')
        {
            if (honorContinuation && source[_position] == '\\')
            {
                // A backslash before the line break continues the directive on the next line.
                _position++;
                SkipChar('\r');
                SkipChar('\n');
                continue;
            }

            _position++;
        }
    }

    private void SkipChar(char c)
    {
        if (_position < source.Length && source[_position] == c)
        {
            _position++;
        }
    }

    private static bool IsIdentifierStart(char c) => c == '_' || char.IsLetter(c);

    private static bool IsIdentifierPart(char c) => c == '_' || char.IsLetterOrDigit(c);

    private static bool IsNumberPart(char c) => IsIdentifierPart(c) || c is '.' or '\'';
}
