using System;
using System.Collections.Generic;
using MuleSoftAutomation.Models;
using ThreePlLocalFunction.Shared;

namespace MuleSoftAutomation.Services;

/// <summary>
/// Parses a MuleSoft NAV onboarding requirements CSV into one MuleSoftOnboardingRecord.
/// Unlike the Solace CSV, this always describes a single country/partner — no
/// Brand/Env/SystemName-style grouping — since one requirements document is one
/// onboarding request.
///
/// Long format — one row per list item (an environment override, a transaction type,
/// a message type, or a mapping), discriminated by RowType. Identity/base-config
/// columns repeat on every row; only the first row's values are used for those.
///
/// Columns (order matters, header row required):
///   CountryKey, CountryCode, PartnerComment, CreatedBy,
///   NavProtocol, NavPort, NavUsername, NavDomain, NavService, NavSoapPort, NavUseCommonCert,
///   TranslationReceiverName,
///   RowType, Environment, NavHost, NavCompany, NavSoapPath, NavRoutingCode,
///   TransactionTypeCode, TransactionTypeEnabled, TransactionTypeLabel,
///   MessageType, SourceDestinationFrom, SourceDestinationTo, UomFrom, UomTo
///
/// RowType is one of: Environment, TransactionType, MessageType, SourceDestination, UomMapping.
/// Only the columns relevant to that row's RowType need to be filled in.
///
/// Example (one row per item, identity columns only filled on the first row):
///   CountryKey,CountryCode,PartnerComment,CreatedBy,NavProtocol,NavPort,NavUsername,NavDomain,NavService,NavSoapPort,NavUseCommonCert,TranslationReceiverName,RowType,Environment,NavHost,NavCompany,NavSoapPath,NavRoutingCode,TransactionTypeCode,TransactionTypeEnabled,TransactionTypeLabel,MessageType,SourceDestinationFrom,SourceDestinationTo,UomFrom,UomTo
///   royal-canin-france,fr,Royal Canin France NAV onboarding,integration-pulse-mulesoft-publisher,https,7124,SVC_UAT_MULESOFT,mars-ad.net,InterfaceWebServices,7124,true,petc-rc-navision-3plspl-sys,Environment,app,azr-xxx1234.mars-ad.net,Company(UAT - Royal Canin France),/sop_fra_uat_01/WS/UAT - Royal Canin France/Codeunit/InterfaceWebServices?wsdl,NAV_FM_IN_UAT_FRA,,,,,,,,
///   ,,,,,,,,,,,,Environment,dev,azr-xxx1234-dev.mars-ad.net,Company(DEV - Royal Canin France),/sop_fra_dev_01/WS/DEV - Royal Canin France/Codeunit/InterfaceWebServices?wsdl,NAV_FM_IN_DEV_FRA,,,,,,,,
///   ,,,,,,,,,,,,TransactionType,,,,,,sal_008,true,SHIPMENT,,,,,
///   ,,,,,,,,,,,,MessageType,,,,,,,,,despatchStock,,,,
///   ,,,,,,,,,,,,SourceDestination,,,,,,,,,,PLANT-FR1-PLANT-FR2,spl_001,,
///   ,,,,,,,,,,,,UomMapping,,,,,,,,,,,,EA,UNIT
/// </summary>
public sealed class MuleSoftCsvParser
{
    private const int ColumnCount = 26;

    public MuleSoftOnboardingRecord Parse(string csvContent)
    {
        var lines = csvContent
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length < 2)
            throw new ArgumentException("CSV must contain a header row and at least one data row.");

        var rows = new List<CsvRow>();
        for (var i = 1; i < lines.Length; i++)
            rows.Add(ParseRow(lines[i], i));

        var first = rows[0];

        var environmentOverrides = new Dictionary<string, EnvironmentOverride>();
        var transactionTypes = new List<TransactionTypeEntry>();
        var messageTypes = new List<string>();
        var sourceDestinationMappings = new List<SourceDestinationMapping>();
        var uomMappings = new List<UomMapping>();

        foreach (var row in rows)
        {
            switch (row.RowType.ToLowerInvariant())
            {
                case "environment":
                    if (row.Environment.Length == 0)
                        throw new FormatException("RowType 'Environment' requires the Environment column (app/dev/tst/prod).");
                    environmentOverrides[row.Environment.ToLowerInvariant()] = new EnvironmentOverride(
                        Host: row.NavHost, Company: row.NavCompany, SoapPath: row.NavSoapPath, RoutingCode: row.NavRoutingCode);
                    break;

                case "transactiontype":
                    if (row.TransactionTypeCode.Length == 0)
                        throw new FormatException("RowType 'TransactionType' requires TransactionTypeCode.");
                    transactionTypes.Add(new TransactionTypeEntry(
                        Code: row.TransactionTypeCode, Enabled: row.TransactionTypeEnabled, Label: row.TransactionTypeLabel));
                    break;

                case "messagetype":
                    if (row.MessageType.Length > 0)
                        messageTypes.Add(row.MessageType);
                    break;

                case "sourcedestination":
                    if (row.SourceDestinationFrom.Length == 0 || row.SourceDestinationTo.Length == 0)
                        throw new FormatException(
                            "RowType 'SourceDestination' requires both SourceDestinationFrom and SourceDestinationTo.");
                    sourceDestinationMappings.Add(new SourceDestinationMapping(row.SourceDestinationFrom, row.SourceDestinationTo));
                    break;

                case "uommapping":
                    if (row.UomFrom.Length == 0 || row.UomTo.Length == 0)
                        throw new FormatException("RowType 'UomMapping' requires both UomFrom and UomTo.");
                    uomMappings.Add(new UomMapping(row.UomFrom, row.UomTo));
                    break;

                default:
                    throw new FormatException(
                        $"Unrecognized RowType '{row.RowType}'. Expected one of: Environment, TransactionType, " +
                        "MessageType, SourceDestination, UomMapping.");
            }
        }

        return new MuleSoftOnboardingRecord(
            CountryKey:              first.CountryKey,
            CountryCode:             first.CountryCode,
            PartnerComment:          first.PartnerComment,
            CreatedBy:               first.CreatedBy,
            NavProtocol:             first.NavProtocol,
            NavPort:                 first.NavPort,
            NavUsername:             first.NavUsername,
            NavDomain:               first.NavDomain,
            NavService:              first.NavService,
            NavSoapPort:             first.NavSoapPort,
            NavUseCommonCert:        first.NavUseCommonCert,
            TranslationReceiverName: first.TranslationReceiverName,
            EnvironmentOverrides:      environmentOverrides,
            TransactionTypes:          transactionTypes,
            MessageTypes:              messageTypes,
            SourceDestinationMappings: sourceDestinationMappings,
            UomMappings:               uomMappings);
    }

    private static CsvRow ParseRow(string line, int rowIndex)
    {
        var fields = CsvLineSplitter.Split(line);

        if (fields.Count < ColumnCount)
            throw new FormatException(
                $"Row {rowIndex + 1} has {fields.Count} column(s); expected {ColumnCount}. Row content: {line}");

        for (var i = 0; i < fields.Count; i++)
            fields[i] = fields[i].Trim();

        return new CsvRow(
            CountryKey:              fields[0],
            CountryCode:             fields[1],
            PartnerComment:          fields[2],
            CreatedBy:               fields[3],
            NavProtocol:             fields[4],
            NavPort:                 fields[5],
            NavUsername:             fields[6],
            NavDomain:               fields[7],
            NavService:              fields[8],
            NavSoapPort:             fields[9],
            NavUseCommonCert:        fields[10],
            TranslationReceiverName: fields[11],
            RowType:                 fields[12],
            Environment:             fields[13],
            NavHost:                 fields[14],
            NavCompany:              fields[15],
            NavSoapPath:             fields[16],
            NavRoutingCode:          fields[17],
            TransactionTypeCode:     fields[18],
            TransactionTypeEnabled:  fields[19],
            TransactionTypeLabel:    fields[20],
            MessageType:             fields[21],
            SourceDestinationFrom:   fields[22],
            SourceDestinationTo:     fields[23],
            UomFrom:                 fields[24],
            UomTo:                   fields[25]
        );
    }

    private readonly record struct CsvRow(
        string CountryKey, string CountryCode, string PartnerComment, string CreatedBy,
        string NavProtocol, string NavPort, string NavUsername, string NavDomain,
        string NavService, string NavSoapPort, string NavUseCommonCert,
        string TranslationReceiverName,
        string RowType, string Environment, string NavHost, string NavCompany, string NavSoapPath, string NavRoutingCode,
        string TransactionTypeCode, string TransactionTypeEnabled, string TransactionTypeLabel,
        string MessageType,
        string SourceDestinationFrom, string SourceDestinationTo,
        string UomFrom, string UomTo);
}
