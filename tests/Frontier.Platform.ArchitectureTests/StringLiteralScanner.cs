using System.Text;

namespace Frontier.Platform.ArchitectureTests;

/// <summary>One C# string literal's text (escapes left as written) and the 1-based line it starts on.</summary>
/// <param name="Text">The characters between the delimiters; interpolation holes are omitted.</param>
/// <param name="Line">The 1-based line of the opening quote's prefix.</param>
internal sealed record StringLiteral(string Text, int Line);

/// <summary>
/// A small C# lexer that yields the string literals in a source file and nothing else — comments,
/// identifiers and char literals are skipped — so a source-tree rule about literal values is not
/// tripped by a doc comment (S13.235, a copy of the consumer repository's S13.212 lexer). It understands regular, verbatim (<c>@"…"</c>), raw
/// (<c>"""…"""</c>) and interpolated (<c>$"…"</c>, <c>$@"…"</c>) strings; a string inside an
/// interpolation hole is yielded as its own literal. It is a test-time approximation, not a
/// compiler: escapes are not decoded, and an unterminated literal ends at the end of its line.
/// </summary>
internal sealed class StringLiteralScanner
{
    private readonly string _source;
    private readonly List<StringLiteral> _found = [];
    private int _pos;

    private StringLiteralScanner(string source) => _source = source;

    /// <summary>Every string literal in <paramref name="source"/>, in source order.</summary>
    internal static IReadOnlyList<StringLiteral> Scan(string source)
    {
        var scanner = new StringLiteralScanner(source);
        scanner.ScanCode(stopAtClosingBrace: false);
        return scanner._found;
    }

    /// <summary>Lexes code until the end of input, or — inside an interpolation hole — until the hole's closing brace.</summary>
    private void ScanCode(bool stopAtClosingBrace)
    {
        var depth = 0;
        while (_pos < _source.Length)
        {
            if (stopAtClosingBrace && Peek(0) == '}' && depth == 0)
            {
                _pos++;
                return;
            }

            depth += BraceDelta(Peek(0));
            ScanToken();
        }
    }

    private static int BraceDelta(char c) => c switch { '{' => 1, '}' => -1, _ => 0 };

    /// <summary>Consumes one token: a comment, a char literal, a string literal, or a single code character.</summary>
    private void ScanToken()
    {
        if (Peek(0) == '/' && Peek(1) == '/') SkipPast("\n");
        else if (Peek(0) == '/' && Peek(1) == '*') SkipPast("*/");
        else if (Peek(0) == '\'') SkipCharLiteral();
        else if (StringPrefixLength() is var prefix and >= 0) ReadString(prefix);
        else _pos++;
    }

    /// <summary>The count of <c>$</c>/<c>@</c> characters before a <c>"</c> at the cursor, or -1 when no string starts here.</summary>
    private int StringPrefixLength()
    {
        var length = 0;
        while (Peek(length) is '$' or '@') length++;
        return Peek(length) == '"' ? length : -1;
    }

    private void ReadString(int prefixLength)
    {
        var start = _pos;
        var prefix = _source.Substring(_pos, prefixLength);
        _pos += prefixLength;
        var quotes = CountRun('"');
        var text = quotes >= 3 ? ReadRaw(quotes) : ReadQuoted(prefix.Contains('@', StringComparison.Ordinal), prefix.Contains('$', StringComparison.Ordinal));
        _found.Add(new StringLiteral(text, LineOf(start)));
    }

    /// <summary>Reads a raw literal opened by <paramref name="quotes"/> quotes, closed by the same run.</summary>
    private string ReadRaw(int quotes)
    {
        var close = new string('"', quotes);
        _pos += quotes;
        var end = _source.IndexOf(close, _pos, StringComparison.Ordinal);
        end = end < 0 ? _source.Length : end;
        var text = _source[_pos..end];
        _pos = Math.Min(end + quotes, _source.Length);
        return text;
    }

    /// <summary>Reads a regular or verbatim literal, handing each interpolation hole back to the code lexer.</summary>
    private string ReadQuoted(bool verbatim, bool interpolated)
    {
        var text = new StringBuilder();
        _pos++;
        while (_pos < _source.Length && ReadQuotedChar(text, verbatim, interpolated))
        {
        }

        return text.ToString();
    }

    /// <summary>Consumes one unit of a quoted literal into <paramref name="text"/>; false when the literal has ended.</summary>
    private bool ReadQuotedChar(StringBuilder text, bool verbatim, bool interpolated)
    {
        var c = Peek(0);
        var doubled = Peek(1) == c;
        if (c == '"' && !(verbatim && doubled)) return End(1);
        if (c == '\n' && !verbatim) return End(0);
        if (c == '{' && interpolated && !doubled) return OpenHole();
        var width = (c == '\\' && !verbatim) || (doubled && c is '"' or '{' or '}') ? 2 : 1;
        text.Append(_source, _pos, Math.Min(width, _source.Length - _pos));
        _pos += width;
        return true;
    }

    private bool End(int consumed)
    {
        _pos += consumed;
        return false;
    }

    private bool OpenHole()
    {
        _pos++;
        ScanCode(stopAtClosingBrace: true);
        return true;
    }

    private void SkipCharLiteral()
    {
        _pos++;
        while (_pos < _source.Length && Peek(0) is not '\'' and not '\n')
        {
            _pos += Peek(0) == '\\' ? 2 : 1;
        }

        _pos++;
    }

    private void SkipPast(string terminator)
    {
        var end = _source.IndexOf(terminator, _pos + 2, StringComparison.Ordinal);
        _pos = end < 0 ? _source.Length : end + terminator.Length;
    }

    private int CountRun(char c)
    {
        var count = 0;
        while (Peek(count) == c) count++;
        return count;
    }

    private char Peek(int offset) => _pos + offset < _source.Length ? _source[_pos + offset] : '\0';

    private int LineOf(int index) => _source.AsSpan(0, index).Count('\n') + 1;
}
