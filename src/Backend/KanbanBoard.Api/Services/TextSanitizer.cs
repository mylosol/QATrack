using System.Text;

namespace KanbanBoard.Api.Services;

/// <summary>
/// Normalizes untrusted text before it is persisted. This is defense in depth:
/// output encoding and markdown sanitizing still happen in the browser, but we
/// never store control characters (log/terminal injection, invisible text) or
/// unbounded strings.
/// </summary>
public static class TextSanitizer
{
    /// <summary>
    /// Trims, strips all control characters (including newlines) and enforces
    /// <paramref name="maxLength"/>. Returns null for null/blank input.
    /// </summary>
    public static string? SingleLine(string? value, int maxLength)
    {
        if (value is null)
        {
            return null;
        }

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (!char.IsControl(ch))
            {
                sb.Append(ch);
            }
        }

        var cleaned = sb.ToString().Trim();
        if (cleaned.Length == 0)
        {
            return null;
        }

        return cleaned.Length > maxLength ? cleaned[..maxLength].TrimEnd() : cleaned;
    }

    /// <summary>
    /// Normalizes line endings to <c>\n</c>, keeps tabs/newlines, strips other
    /// control characters and enforces <paramref name="maxLength"/>. Returns
    /// null for null/blank input.
    /// </summary>
    public static string? MultiLine(string? value, int maxLength)
    {
        if (value is null)
        {
            return null;
        }

        var normalized = value.Replace("\r\n", "\n").Replace('\r', '\n');
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (ch == '\n' || ch == '\t' || !char.IsControl(ch))
            {
                sb.Append(ch);
            }
        }

        var cleaned = sb.ToString();
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return null;
        }

        return cleaned.Length > maxLength ? cleaned[..maxLength] : cleaned;
    }
}
