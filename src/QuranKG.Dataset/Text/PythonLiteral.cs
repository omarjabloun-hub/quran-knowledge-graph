using System.Globalization;
using System.Text;

namespace QuranKG.Dataset.Text;

/// <summary>
/// Reads the Python dict literals the Kaggle CSVs store per verse, e.g.
/// <c>{'id': 1.2, 'content': '…', 'translation_eng': "Abraham's …", 'transliteration': '…'}</c>.
/// Values are returned as their source text: strings unescaped, numbers verbatim — the verse
/// id is "ayah.surah" and only its integer part is meaningful, so it must not go through a float.
/// </summary>
public static class PythonLiteral
{
    public static Dictionary<string, string> ParseDict(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var i      = 0;

        Skip(text, ref i);
        Expect(text, ref i, '{');

        while (true)
        {
            Skip(text, ref i);
            if (Peek(text, i) == '}') { i++; break; }

            var key = ReadString(text, ref i);
            Skip(text, ref i);
            Expect(text, ref i, ':');
            Skip(text, ref i);
            var value = Peek(text, i) is '\'' or '"' ? ReadString(text, ref i) : ReadBare(text, ref i);
            result[key] = value;

            Skip(text, ref i);
            var c = Peek(text, i);
            if (c == ',') { i++; continue; }
            if (c == '}') { i++; break; }
            throw new FormatException($"Expected ',' or '}}' at {i} in: {Excerpt(text, i)}");
        }

        return result;
    }

    private static string ReadString(string text, ref int i)
    {
        var quote = Peek(text, i);
        if (quote is not ('\'' or '"')) throw new FormatException($"Expected a string at {i} in: {Excerpt(text, i)}");
        i++;

        var sb = new StringBuilder();
        while (i < text.Length)
        {
            var c = text[i++];
            if (c == quote) return sb.ToString();
            if (c != '\\') { sb.Append(c); continue; }

            if (i >= text.Length) break;
            var e = text[i++];
            switch (e)
            {
                case 'n':  sb.Append('\n'); break;
                case 't':  sb.Append('\t'); break;
                case 'r':  sb.Append('\r'); break;
                case '\\': sb.Append('\\'); break;
                case '\'': sb.Append('\''); break;
                case '"':  sb.Append('"');  break;
                case 'x':  sb.Append((char)int.Parse(text.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); i += 2; break;
                case 'u':  sb.Append((char)int.Parse(text.AsSpan(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); i += 4; break;
                case 'U':  sb.Append(char.ConvertFromUtf32(int.Parse(text.AsSpan(i, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture))); i += 8; break;
                default:   sb.Append('\\').Append(e); break;
            }
        }

        throw new FormatException("Unterminated string literal: " + Excerpt(text, text.Length - 1));
    }

    private static string ReadBare(string text, ref int i)
    {
        var start = i;
        while (i < text.Length && text[i] is not (',' or '}')) i++;
        return text[start..i].Trim();
    }

    private static void Skip(string text, ref int i)
    {
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
    }

    private static char Peek(string text, int i) => i < text.Length ? text[i] : '\0';

    private static void Expect(string text, ref int i, char c)
    {
        if (Peek(text, i) != c) throw new FormatException($"Expected '{c}' at {i} in: {Excerpt(text, i)}");
        i++;
    }

    private static string Excerpt(string text, int i) => text.Substring(Math.Max(0, i - 30), Math.Min(60, text.Length - Math.Max(0, i - 30)));
}
