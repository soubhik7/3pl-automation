using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SolaceConfigGenerator.Models;

namespace SolaceConfigGenerator.Services;

/// <summary>
/// Parses the onboarding CSV into SolaceOnboardingRecord instances.
///
/// Expected columns (order matters, header row required):
///   Brand, Env, SystemName, ThreePLCode, EncryptedPassword,
///   DespatchStockTopics, SourcingStockTopics, ReturnStockTopics, StockMovementTopics
///
/// Topic cells contain pipe-delimited ( | ) topic strings.
/// Cells with commas or pipes must be quoted per RFC 4180.
/// </summary>
public sealed class CsvParser
{
    private const char TopicDelimiter = '|';
    private const int ExpectedColumnCount = 9;

    public IReadOnlyList<SolaceOnboardingRecord> Parse(string csvContent)
    {
        var lines = csvContent
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length < 2)
            throw new ArgumentException("CSV must contain a header row and at least one data row.");

        // Skip header row (index 0)
        return lines.Skip(1).Select(ParseDataRow).ToList();
    }

    private static SolaceOnboardingRecord ParseDataRow(string line, int rowIndex)
    {
        var fields = SplitCsvLine(line);

        if (fields.Count < ExpectedColumnCount)
            throw new FormatException(
                $"Row {rowIndex + 2} has {fields.Count} column(s); expected {ExpectedColumnCount}. " +
                $"Row content: {line}");

        return new SolaceOnboardingRecord(
            Brand:                 fields[0].Trim(),
            Env:                   fields[1].Trim(),
            SystemName:            fields[2].Trim(),
            ThreePLCode:           fields[3].Trim(),
            EncryptedPassword:     fields[4],           // preserve exactly — already encrypted
            DespatchStockTopics:   SplitTopics(fields[5]),
            SourcingStockTopics:   SplitTopics(fields[6]),
            ReturnStockTopics:     SplitTopics(fields[7]),
            StockMovementTopics:   SplitTopics(fields[8])
        );
    }

    private static IReadOnlyList<string> SplitTopics(string cell) =>
        string.IsNullOrWhiteSpace(cell)
            ? []
            : cell.Split(TopicDelimiter, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // RFC 4180 CSV field splitter — handles quoted fields and escaped double-quotes ("")
    private static List<string> SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

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
