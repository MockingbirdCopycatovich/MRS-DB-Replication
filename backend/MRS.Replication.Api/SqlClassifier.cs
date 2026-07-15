namespace MRS.Replication.Api;

public enum SqlKind
{
    Read,
    Write
}

/// <summary>Pure leading-keyword classifier — deliberately simple, no SQL parser needed for the demo dummy schema.</summary>
public static class SqlClassifier
{
    private static readonly HashSet<string> ReadKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "WITH", "SHOW", "EXPLAIN", "TABLE"
    };

    public static SqlKind Classify(string sql)
    {
        var firstWord = ExtractFirstWord(sql);
        return ReadKeywords.Contains(firstWord) ? SqlKind.Read : SqlKind.Write;
    }

    private static string ExtractFirstWord(string sql)
    {
        var trimmed = sql.AsSpan().TrimStart();
        var end = 0;
        while (end < trimmed.Length && !char.IsWhiteSpace(trimmed[end]) && trimmed[end] != '(' && trimmed[end] != ';')
        {
            end++;
        }
        return trimmed[..end].ToString();
    }
}
