using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Frontier.Platform.Serialization;

/// <summary>
/// RFC 8785 JSON Canonicalization Scheme (JCS). ADR-E2 decision 2: wherever untyped JSON takes part
/// in a hash, cache key, signature or fingerprint, the bytes hashed are its JCS form. The canonical
/// profile fixes the order of a typed contract's own properties; JCS fixes what rides inside them
/// (a <see cref="Frontier.Platform.Workflow.Model.TypedPayload"/>'s payload and facts). ADR-PA30
/// resolves ADR-E2 deferral (b): this is the canonicaliser, and governance audit signing is its
/// first in-repo consumer.
/// </summary>
/// <remarks>
/// Object members are sorted by their names' UTF-16 code units; strings use the ECMAScript
/// <c>JSON.stringify</c> escapes and nothing else; numbers are IEEE-754 doubles written in the
/// ECMAScript <c>Number.prototype.toString</c> form. Whitespace is never emitted. This parses and
/// writes JSON text and never touches <c>JsonSerializerOptions</c>, so the one-profile rule (K10)
/// is unaffected.
/// </remarks>
public static class JsonCanonicalizer
{
    /// <summary>The JCS canonical UTF-8 bytes of the JSON text in <paramref name="utf8Json"/>.</summary>
    /// <exception cref="JsonException">The input is not valid JSON.</exception>
    public static byte[] Canonicalize(ReadOnlySpan<byte> utf8Json)
    {
        var reader = new Utf8JsonReader(utf8Json);
        using var document = JsonDocument.ParseValue(ref reader);
        return Canonicalize(document.RootElement);
    }

    /// <summary>The JCS canonical UTF-8 bytes of <paramref name="element"/>.</summary>
    /// <exception cref="ArgumentException">A number is not finite.</exception>
    public static byte[] Canonicalize(JsonElement element)
    {
        var builder = new StringBuilder();
        Write(element, builder);
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    /// <summary>Writes one value in canonical form.</summary>
    internal static void Write(JsonElement element, StringBuilder builder)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                WriteObject(element, builder);
                break;
            case JsonValueKind.Array:
                WriteArray(element, builder);
                break;
            case JsonValueKind.String:
                WriteString(element.GetString()!, builder);
                break;
            case JsonValueKind.Number:
                builder.Append(FormatNumber(element.GetDouble()));
                break;
            default:
                // True, False and Null: their raw text is already canonical.
                builder.Append(element.GetRawText());
                break;
        }
    }

    private static void WriteObject(JsonElement element, StringBuilder builder)
    {
        builder.Append('{');
        var first = true;
        foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            builder.Append(first ? string.Empty : ",");
            first = false;
            WriteString(property.Name, builder);
            builder.Append(':');
            Write(property.Value, builder);
        }

        builder.Append('}');
    }

    private static void WriteArray(JsonElement element, StringBuilder builder)
    {
        builder.Append('[');
        var first = true;
        foreach (var item in element.EnumerateArray())
        {
            builder.Append(first ? string.Empty : ",");
            first = false;
            Write(item, builder);
        }

        builder.Append(']');
    }

    /// <summary>Writes <paramref name="value"/> quoted, with only the escapes RFC 8785 §3.2.2.2 requires.</summary>
    internal static void WriteString(string value, StringBuilder builder)
    {
        builder.Append('"');
        foreach (var c in value)
        {
            builder.Append(Escape(c));
        }

        builder.Append('"');
    }

    /// <summary>The canonical text for one UTF-16 code unit inside a string.</summary>
    internal static string Escape(char c) => c switch
    {
        '"' => "\\\"",
        '\\' => "\\\\",
        '\b' => "\\b",
        '\f' => "\\f",
        '\n' => "\\n",
        '\r' => "\\r",
        '\t' => "\\t",
        < ' ' => string.Create(CultureInfo.InvariantCulture, $"\\u{(int)c:x4}"),
        _ => c.ToString(),
    };

    /// <summary>
    /// Formats <paramref name="value"/> as ECMAScript <c>Number.prototype.toString</c> does
    /// (RFC 8785 §3.2.2.3), from .NET's shortest round-trip digits.
    /// </summary>
    internal static string FormatNumber(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentException("JCS numbers must be finite (RFC 8785 §3.2.2.3).", nameof(value));
        }

        if (value == 0)
        {
            return "0";
        }

        var (digits, exponent) = ShortestDigits(Math.Abs(value));
        return (value < 0 ? "-" : string.Empty) + Layout(digits, exponent);
    }

    /// <summary>
    /// The shortest round-trip significant digits of a positive finite <paramref name="value"/>, and
    /// n such that the value is 0.d1d2…dk × 10^n (ECMA-262 Number::toString's k and n).
    /// </summary>
    internal static (string Digits, int Exponent) ShortestDigits(double value)
    {
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        var exponentAt = text.IndexOf('E', StringComparison.Ordinal);
        var mantissa = exponentAt < 0 ? text : text[..exponentAt];
        var scientific = exponentAt < 0 ? 0 : int.Parse(text[(exponentAt + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

        var pointAt = mantissa.IndexOf('.', StringComparison.Ordinal);
        var integerLength = pointAt < 0 ? mantissa.Length : pointAt;
        var allDigits = mantissa.Replace(".", string.Empty, StringComparison.Ordinal);
        var leadingZeros = allDigits.Length - allDigits.TrimStart('0').Length;

        var digits = allDigits.Trim('0');
        return (digits, integerLength - leadingZeros + scientific);
    }

    /// <summary>ECMA-262 Number::toString steps 6–10 for digits d1…dk and exponent n.</summary>
    internal static string Layout(string digits, int n)
    {
        var k = digits.Length;

        if (k <= n && n <= 21)
        {
            return digits + new string('0', n - k);
        }

        if (0 < n && n <= 21)
        {
            return digits[..n] + "." + digits[n..];
        }

        if (-6 < n && n <= 0)
        {
            return "0." + new string('0', -n) + digits;
        }

        var e = n - 1;
        var sign = e < 0 ? "-" : "+";
        var fraction = k == 1 ? string.Empty : "." + digits[1..];
        return string.Create(CultureInfo.InvariantCulture, $"{digits[0]}{fraction}e{sign}{Math.Abs(e)}");
    }
}
