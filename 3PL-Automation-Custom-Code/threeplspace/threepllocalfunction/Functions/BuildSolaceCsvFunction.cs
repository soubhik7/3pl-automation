using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Azure.Functions.Extensions.Workflows;
using Microsoft.Azure.Functions.Worker;
using ThreePlLocalFunction.Shared;

namespace SolaceConfigGenerator.Functions;

public class SolaceClientRow
{
    public string? Brand { get; set; }
    public string? Env { get; set; }
    public string? SystemName { get; set; }
    public string? ThreePLCode { get; set; }
    public string? EncryptedPassword { get; set; }
    public string? Action { get; set; }
    public bool? ClientProfileAllowGuaranteedMsgSendEnabled { get; set; }
    public bool? ClientProfileAllowGuaranteedMsgReceiveEnabled { get; set; }
    public bool? ClientProfileCompressionEnabled { get; set; }
    public bool? ClientProfileReplicationAllowClientConnectWhenStandbyEnabled { get; set; }
    public bool? ClientProfileAllowTransactedSessionsEnabled { get; set; }
    public bool? ClientProfileAllowBridgeConnectionsEnabled { get; set; }
    public bool? ClientProfileAllowGuaranteedEndpointCreateEnabled { get; set; }
    public bool? ClientProfileAllowSharedSubscriptionsEnabled { get; set; }
    public string? AclClientConnectDefaultAction { get; set; }
    public string? AclPublishTopicDefaultAction { get; set; }
    public string? AclSubscribeShareNameDefaultAction { get; set; }
    public string? AclSubscribeTopicDefaultAction { get; set; }
    public bool? ClientUserEnabled { get; set; }
    public bool? ClientUserGuaranteedEndpointPermissionOverrideEnabled { get; set; }
    public bool? ClientUserSubscriptionManagerEnabled { get; set; }
}

public class SolaceMessageTypeRow
{
    public string? MessageType { get; set; }
    public string? Topic { get; set; }
    public string? QueuePermission { get; set; }
    public bool? QueueEgressEnabled { get; set; }
    public int? QueueMaxRedeliveryCount { get; set; }
}

public class BuildSolaceCsvRequest
{
    public SolaceClientRow? ParentRow { get; set; }
    public List<SolaceMessageTypeRow>? MessageTypes { get; set; }
}

public class BuildSolaceCsvFunction
{
    [Function("BuildSolaceCsv")]
    public IDictionary<string, object> Run([WorkflowActionTrigger] BuildSolaceCsvRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (request.ParentRow == null)
            throw new ArgumentException("ParentRow must not be null.");
        if (request.MessageTypes == null || request.MessageTypes.Count == 0)
            throw new ArgumentException("MessageTypes must contain at least one row.");

        var sb = new StringBuilder();
        // Append Header
        sb.Append("Brand,Env,SystemName,ThreePLCode,EncryptedPassword,Action,ClientProfileAllowGuaranteedMsgSendEnabled,ClientProfileAllowGuaranteedMsgReceiveEnabled,ClientProfileCompressionEnabled,ClientProfileReplicationAllowClientConnectWhenStandbyEnabled,ClientProfileAllowTransactedSessionsEnabled,ClientProfileAllowBridgeConnectionsEnabled,ClientProfileAllowGuaranteedEndpointCreateEnabled,ClientProfileAllowSharedSubscriptionsEnabled,AclClientConnectDefaultAction,AclPublishTopicDefaultAction,AclSubscribeShareNameDefaultAction,AclSubscribeTopicDefaultAction,ClientUserEnabled,ClientUserGuaranteedEndpointPermissionOverrideEnabled,ClientUserSubscriptionManagerEnabled,MessageType,Topic,QueuePermission,QueueEgressEnabled,QueueMaxRedeliveryCount\n");

        var parent = request.ParentRow;
        bool isFirst = true;

        foreach (var mt in request.MessageTypes)
        {
            if (isFirst)
            {
                // Write parent columns (1 to 21) + child columns
                sb.Append(CsvFieldEscaper.Escape(parent.Brand)).Append(",");
                sb.Append(CsvFieldEscaper.Escape(parent.Env)).Append(",");
                sb.Append(CsvFieldEscaper.Escape(parent.SystemName)).Append(",");
                sb.Append(CsvFieldEscaper.Escape(parent.ThreePLCode)).Append(",");
                // EncryptedPassword passed through untrimmed and raw
                sb.Append(parent.EncryptedPassword ?? "").Append(",");
                sb.Append(CsvFieldEscaper.Escape(parent.Action)).Append(",");
                sb.Append(CsvFieldEscaper.EscapeBool(parent.ClientProfileAllowGuaranteedMsgSendEnabled)).Append(",");
                sb.Append(CsvFieldEscaper.EscapeBool(parent.ClientProfileAllowGuaranteedMsgReceiveEnabled)).Append(",");
                sb.Append(CsvFieldEscaper.EscapeBool(parent.ClientProfileCompressionEnabled)).Append(",");
                sb.Append(CsvFieldEscaper.EscapeBool(parent.ClientProfileReplicationAllowClientConnectWhenStandbyEnabled)).Append(",");
                sb.Append(CsvFieldEscaper.EscapeBool(parent.ClientProfileAllowTransactedSessionsEnabled)).Append(",");
                sb.Append(CsvFieldEscaper.EscapeBool(parent.ClientProfileAllowBridgeConnectionsEnabled)).Append(",");
                sb.Append(CsvFieldEscaper.EscapeBool(parent.ClientProfileAllowGuaranteedEndpointCreateEnabled)).Append(",");
                sb.Append(CsvFieldEscaper.EscapeBool(parent.ClientProfileAllowSharedSubscriptionsEnabled)).Append(",");
                sb.Append(CsvFieldEscaper.Escape(parent.AclClientConnectDefaultAction)).Append(",");
                sb.Append(CsvFieldEscaper.Escape(parent.AclPublishTopicDefaultAction)).Append(",");
                sb.Append(CsvFieldEscaper.Escape(parent.AclSubscribeShareNameDefaultAction)).Append(",");
                sb.Append(CsvFieldEscaper.Escape(parent.AclSubscribeTopicDefaultAction)).Append(",");
                sb.Append(CsvFieldEscaper.EscapeBool(parent.ClientUserEnabled)).Append(",");
                sb.Append(CsvFieldEscaper.EscapeBool(parent.ClientUserGuaranteedEndpointPermissionOverrideEnabled)).Append(",");
                sb.Append(CsvFieldEscaper.EscapeBool(parent.ClientUserSubscriptionManagerEnabled)).Append(",");

                isFirst = false;
            }
            else
            {
                // Write empty parent columns (21 commas)
                sb.Append(",,,,,,,,,,,,,,,,,,,,,");
            }

            // Write child columns (22 to 26)
            sb.Append(CsvFieldEscaper.Escape(mt.MessageType)).Append(",");
            sb.Append(CsvFieldEscaper.Escape(mt.Topic)).Append(",");
            sb.Append(CsvFieldEscaper.Escape(mt.QueuePermission)).Append(",");
            sb.Append(CsvFieldEscaper.EscapeBool(mt.QueueEgressEnabled)).Append(",");
            sb.Append(CsvFieldEscaper.EscapeInt(mt.QueueMaxRedeliveryCount)).Append("\n");
        }

        return new Dictionary<string, object>
        {
            ["csvContent"] = sb.ToString()
        };
    }
}
