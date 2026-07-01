using System.Collections.Generic;
using System.Text;

namespace ThreePlLocalFunction.Shared;

// ============================================================================
// CsvLineSplitter — RFC 4180 CSV line splitter
// ----------------------------------------------------------------------------
// WHY THIS EXISTS:
//   Both the Solace CSV parser and the MuleSoft CSV parser need to split a
//   comma-separated line into fields while respecting quoted fields that may
//   themselves contain commas or escaped double-quotes ("") — e.g. the
//   Solace EncryptedPassword column looks like "![+ycEERpRxxm5RfqLRNxBXw==]".
//   A naive line.Split(',') breaks on any such value. Written once here so
//   both domains parse CSV identically instead of maintaining two subtly
//   different splitters.
// HOW TO USE:
//   var fields = CsvLineSplitter.Split(csvLine); // returns one string per column, unquoted.
// IMPORTANT NOTES:
//   Stateless and format-agnostic (no Solace/MuleSoft column knowledge) —
//   safe to share across domains without coupling them.
// ============================================================================
/// <summary>RFC 4180 CSV line splitter — handles quoted fields and escaped double-quotes ("").</summary>
public static class CsvLineSplitter
{
    public static List<string> Split(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++; // consume escaped quote
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
        }

        fields.Add(current.ToString()); // last field has no trailing comma
        return fields;
    }
}
