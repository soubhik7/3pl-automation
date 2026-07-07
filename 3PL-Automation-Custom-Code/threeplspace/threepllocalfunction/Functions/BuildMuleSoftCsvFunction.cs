using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Azure.Functions.Extensions.Workflows;
using Microsoft.Azure.Functions.Worker;
using ThreePlLocalFunction.Shared;

namespace MuleSoftAutomation.Functions;

public class MuleSoftPartnerRow
{
    public string? CountryKey { get; set; }
    public string? CountryCode { get; set; }
    public string? PartnerComment { get; set; }
    public string? CreatedBy { get; set; }
    public string? NavProtocol { get; set; }
    public string? NavPort { get; set; }
    public string? NavUsername { get; set; }
    public string? NavDomain { get; set; }
    public string? NavService { get; set; }
    public string? NavSoapPort { get; set; }
    public bool? NavUseCommonCert { get; set; }
    public string? TranslationReceiverName { get; set; }
}

public class MuleSoftEnvironmentRow
{
    public string? Environment { get; set; }
    public string? NavHost { get; set; }
    public string? NavCompany { get; set; }
    public string? NavSoapPath { get; set; }
    public string? NavRoutingCode { get; set; }
}

public class MuleSoftTransactionTypeRow
{
    public string? TransactionTypeCode { get; set; }
    public bool? TransactionTypeEnabled { get; set; }
    public string? TransactionTypeLabel { get; set; }
}

public class MuleSoftMessageTypeRow
{
    public string? MessageType { get; set; }
}

public class MuleSoftSourceDestinationRow
{
    public string? SourceDestinationFrom { get; set; }
    public string? SourceDestinationTo { get; set; }
}

public class MuleSoftUomMappingRow
{
    public string? UomFrom { get; set; }
    public string? UomTo { get; set; }
}

public class BuildMuleSoftCsvRequest
{
    public MuleSoftPartnerRow? ParentRow { get; set; }
    public List<MuleSoftEnvironmentRow>? Environments { get; set; }
    public List<MuleSoftTransactionTypeRow>? TransactionTypes { get; set; }
    public List<MuleSoftMessageTypeRow>? MessageTypes { get; set; }
    public List<MuleSoftSourceDestinationRow>? SourceDestinations { get; set; }
    public List<MuleSoftUomMappingRow>? UomMappings { get; set; }
}

public class BuildMuleSoftCsvFunction
{
    [Function("BuildMuleSoftCsv")]
    public IDictionary<string, object> Run([WorkflowActionTrigger] BuildMuleSoftCsvRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (request.ParentRow == null)
            throw new ArgumentException("ParentRow must not be null.");

        int totalChildRows = 
            (request.Environments?.Count ?? 0) +
            (request.TransactionTypes?.Count ?? 0) +
            (request.MessageTypes?.Count ?? 0) +
            (request.SourceDestinations?.Count ?? 0) +
            (request.UomMappings?.Count ?? 0);

        if (totalChildRows == 0)
            throw new ArgumentException("MuleSoft requirements must contain at least one child row across Environments, TransactionTypes, MessageTypes, SourceDestinations, or UomMappings.");

        var sb = new StringBuilder();
        // Append Header
        sb.Append("CountryKey,CountryCode,PartnerComment,CreatedBy,NavProtocol,NavPort,NavUsername,NavDomain,NavService,NavSoapPort,NavUseCommonCert,TranslationReceiverName,RowType,Environment,NavHost,NavCompany,NavSoapPath,NavRoutingCode,TransactionTypeCode,TransactionTypeEnabled,TransactionTypeLabel,MessageType,SourceDestinationFrom,SourceDestinationTo,UomFrom,UomTo\n");

        var parent = request.ParentRow;
        bool isFirst = true;

        if (request.Environments != null)
        {
            foreach (var env in request.Environments)
            {
                string childCols = $"{CsvFieldEscaper.Escape(env.Environment)},{CsvFieldEscaper.Escape(env.NavHost)},{CsvFieldEscaper.Escape(env.NavCompany)},{CsvFieldEscaper.Escape(env.NavSoapPath)},{CsvFieldEscaper.Escape(env.NavRoutingCode)},,,,,,,,";
                AppendRow(sb, parent, ref isFirst, "Environment", childCols);
            }
        }

        if (request.TransactionTypes != null)
        {
            foreach (var tt in request.TransactionTypes)
            {
                string childCols = $",,,,,{CsvFieldEscaper.Escape(tt.TransactionTypeCode)},{CsvFieldEscaper.EscapeBool(tt.TransactionTypeEnabled)},{CsvFieldEscaper.Escape(tt.TransactionTypeLabel)},,,,,";
                AppendRow(sb, parent, ref isFirst, "TransactionType", childCols);
            }
        }

        if (request.MessageTypes != null)
        {
            foreach (var mt in request.MessageTypes)
            {
                string childCols = $",,,,,,,,{CsvFieldEscaper.Escape(mt.MessageType)},,,,";
                AppendRow(sb, parent, ref isFirst, "MessageType", childCols);
            }
        }

        if (request.SourceDestinations != null)
        {
            foreach (var sd in request.SourceDestinations)
            {
                string childCols = $",,,,,,,,,{CsvFieldEscaper.Escape(sd.SourceDestinationFrom)},{CsvFieldEscaper.Escape(sd.SourceDestinationTo)},,";
                AppendRow(sb, parent, ref isFirst, "SourceDestination", childCols);
            }
        }

        if (request.UomMappings != null)
        {
            foreach (var uom in request.UomMappings)
            {
                string childCols = $",,,,,,,,,,{CsvFieldEscaper.Escape(uom.UomFrom)},{CsvFieldEscaper.Escape(uom.UomTo)}";
                AppendRow(sb, parent, ref isFirst, "UomMapping", childCols);
            }
        }

        return new Dictionary<string, object>
        {
            ["csvContent"] = sb.ToString()
        };
    }

    private static void AppendRow(StringBuilder sb, MuleSoftPartnerRow parent, ref bool isFirst, string rowType, string childCols)
    {
        if (isFirst)
        {
            sb.Append(CsvFieldEscaper.Escape(parent.CountryKey)).Append(",");
            sb.Append(CsvFieldEscaper.Escape(parent.CountryCode)).Append(",");
            sb.Append(CsvFieldEscaper.Escape(parent.PartnerComment)).Append(",");
            sb.Append(CsvFieldEscaper.Escape(parent.CreatedBy)).Append(",");
            sb.Append(CsvFieldEscaper.Escape(parent.NavProtocol)).Append(",");
            sb.Append(CsvFieldEscaper.Escape(parent.NavPort)).Append(",");
            sb.Append(CsvFieldEscaper.Escape(parent.NavUsername)).Append(",");
            sb.Append(CsvFieldEscaper.Escape(parent.NavDomain)).Append(",");
            sb.Append(CsvFieldEscaper.Escape(parent.NavService)).Append(",");
            sb.Append(CsvFieldEscaper.Escape(parent.NavSoapPort)).Append(",");
            sb.Append(CsvFieldEscaper.EscapeBool(parent.NavUseCommonCert)).Append(",");
            sb.Append(CsvFieldEscaper.Escape(parent.TranslationReceiverName)).Append(",");
            isFirst = false;
        }
        else
        {
            // 12 parent columns empty (12 commas)
            sb.Append(",,,,,,,,,,,,");
        }
        sb.Append(rowType).Append(",").Append(childCols).Append("\n");
    }
}
