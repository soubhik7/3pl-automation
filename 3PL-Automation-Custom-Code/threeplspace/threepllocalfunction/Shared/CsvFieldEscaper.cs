using System;

namespace ThreePlLocalFunction.Shared;

public static class CsvFieldEscaper
{
    public static string Escape(string? val)
    {
        if (val == null) return "";
        // Strip newlines in values
        val = val.Replace("\r", "").Replace("\n", "");
        bool needsQuotes = val.Contains(',') || val.Contains('"');
        if (needsQuotes)
        {
            return $"\"{val.Replace("\"", "\"\"")}\"";
        }
        return val;
    }

    public static string EscapeBool(bool? val)
    {
        if (!val.HasValue) return "";
        return val.Value ? "true" : "false";
    }

    public static string EscapeInt(int? val)
    {
        if (!val.HasValue) return "";
        return val.Value.ToString();
    }
}
