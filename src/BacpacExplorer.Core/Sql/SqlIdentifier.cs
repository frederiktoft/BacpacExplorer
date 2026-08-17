using BacpacExplorer.Core.Localization;

namespace BacpacExplorer.Core.Sql;

/// <summary>
/// Quotes and parses SQL Server multipart identifiers without treating them as string literals.
/// Centralizing bracket escaping prevents schema and table names from producing invalid or unsafe generated SQL.
/// </summary>
public static class SqlIdentifier
{
    public static string Quote(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        return $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    public static string QuoteMultipart(params ReadOnlySpan<string> parts) =>
        string.Join('.', parts.ToArray().Select(Quote));

    public static IReadOnlyList<string> ParseMultipart(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        var bracketed = false;

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (bracketed)
            {
                if (character == ']' && index + 1 < value.Length && value[index + 1] == ']')
                {
                    current.Append(']');
                    index++;
                }
                else if (character == ']')
                {
                    bracketed = false;
                }
                else
                {
                    current.Append(character);
                }
            }
            else if (character == '[')
            {
                if (current.ToString().Trim().Length > 0)
                    throw new FormatException(CoreText.Get("OpeningBracketPosition"));
                bracketed = true;
            }
            else if (character == '.')
            {
                AddPart(parts, current);
            }
            else
            {
                current.Append(character);
            }
        }

        if (bracketed)
            throw new FormatException(CoreText.Get("UnclosedBracket"));

        AddPart(parts, current);
        return parts;
    }

    private static void AddPart(List<string> parts, System.Text.StringBuilder current)
    {
        var part = current.ToString().Trim();
        if (part.Length == 0)
            throw new FormatException(CoreText.Get("EmptyIdentifierPart"));

        parts.Add(part);
        current.Clear();
    }
}
