-- Seeds the same data used in three3pllogicapp/data-enrichment/samples/*-complete.json,
-- inserted directly via T-SQL (bypasses the Logic App / Teams card entirely).
-- Safe to re-run: each block only inserts if its natural key isn't already present.
-- Run against 3pl-automation-sql-db1 after schema.sql has created the tables.

-- ============================================================================
-- BTP: royal-canin-france-uat / royal-canin-france-integration / UAT
-- ============================================================================
IF NOT EXISTS (
    SELECT 1 FROM dbo.BtpConfig
    WHERE SubAccount = 'royal-canin-france-uat' AND ProductName = 'royal-canin-france-integration' AND Environment = 'UAT'
)
BEGIN
    INSERT INTO dbo.BtpConfig
        (SubAccount, ProductName, Environment, Mode, DeveloperId, Title, ShortText, RepoOwner, RepoName, WorkflowFileName, BranchRef, ServiceExists, DeploymentStatus, CorrelationId)
    VALUES
        ('royal-canin-france-uat', 'royal-canin-france-integration', 'UAT', 'create', 'dev123', 'Royal Canin France BTP onboarding', 'RC France UAT app', 'your-org', '3pl-config-repo', 'btp-Api-management-deploy.yml', 'main', 0, 'Pending', 'test-btp-001');
END
GO

-- ============================================================================
-- Solace: petc / rc / navision / 3plpnp + 3 message types
-- ============================================================================
DECLARE @SolaceClientId INT;

IF NOT EXISTS (SELECT 1 FROM dbo.SolaceClient WHERE Brand = 'petc' AND Env = 'rc' AND SystemName = 'navision' AND ThreePLCode = '3plpnp')
BEGIN
    INSERT INTO dbo.SolaceClient
        (Brand, Env, SystemName, ThreePLCode, EncryptedPassword, Action,
         ClientProfileAllowGuaranteedMsgSendEnabled, ClientProfileAllowGuaranteedMsgReceiveEnabled, ClientProfileCompressionEnabled,
         ClientProfileReplicationAllowClientConnectWhenStandbyEnabled, ClientProfileAllowTransactedSessionsEnabled,
         ClientProfileAllowBridgeConnectionsEnabled, ClientProfileAllowGuaranteedEndpointCreateEnabled, ClientProfileAllowSharedSubscriptionsEnabled,
         AclClientConnectDefaultAction, AclPublishTopicDefaultAction, AclSubscribeShareNameDefaultAction, AclSubscribeTopicDefaultAction,
         ClientUserEnabled, ClientUserGuaranteedEndpointPermissionOverrideEnabled, ClientUserSubscriptionManagerEnabled,
         RepoOwner, RepoName, FilePath, Branch, BaseBranch, FeatureBranchName, RequesterEmail, RecipientEmail, CommitMessage,
         ServiceExists, DeploymentStatus, CorrelationId)
    VALUES
        ('petc', 'rc', 'navision', '3plpnp', '![+ycEERpRxxm5RfqLRNxBXw==]', 'FullOnboarding',
         1, 1, 0,
         0, 1,
         0, 1, 1,
         'allow', 'allow', 'allow', 'allow',
         1, 0, 0,
         'your-org', '3pl-config-repo', 'solace/petc-rc-navision-3plpnp.json', 'main', 'main', 'feature/petc-rc-navision-onboarding', 'requester@example.com', 'devops-owner@example.com', 'Onboard petc/rc/navision Solace client',
         0, 'Pending', 'test-solace-001');
END

SELECT @SolaceClientId = Id FROM dbo.SolaceClient WHERE Brand = 'petc' AND Env = 'rc' AND SystemName = 'navision' AND ThreePLCode = '3plpnp';

IF NOT EXISTS (SELECT 1 FROM dbo.SolaceMessageType WHERE ClientId = @SolaceClientId AND MessageType = 'DespatchStock' AND Topic = 'topic1')
    INSERT INTO dbo.SolaceMessageType (ClientId, MessageType, Topic, QueuePermission, QueueEgressEnabled, QueueMaxRedeliveryCount)
    VALUES (@SolaceClientId, 'DespatchStock', 'topic1', 'consume', 1, 3);

IF NOT EXISTS (SELECT 1 FROM dbo.SolaceMessageType WHERE ClientId = @SolaceClientId AND MessageType = 'DespatchStock' AND Topic = 'topic2')
    INSERT INTO dbo.SolaceMessageType (ClientId, MessageType, Topic, QueuePermission, QueueEgressEnabled, QueueMaxRedeliveryCount)
    VALUES (@SolaceClientId, 'DespatchStock', 'topic2', 'consume', 1, 3);

IF NOT EXISTS (SELECT 1 FROM dbo.SolaceMessageType WHERE ClientId = @SolaceClientId AND MessageType = 'SourcingStock' AND Topic = 'topic1')
    INSERT INTO dbo.SolaceMessageType (ClientId, MessageType, Topic, QueuePermission, QueueEgressEnabled, QueueMaxRedeliveryCount)
    VALUES (@SolaceClientId, 'SourcingStock', 'topic1', 'consume', 1, 3);
GO

-- ============================================================================
-- MuleSoft: royal-canin-france + environments/transactionTypes/messageTypes/
-- sourceDestinations/uomMappings
-- ============================================================================
DECLARE @MuleSoftPartnerId INT;

IF NOT EXISTS (SELECT 1 FROM dbo.MuleSoftPartner WHERE CountryKey = 'royal-canin-france')
BEGIN
    INSERT INTO dbo.MuleSoftPartner
        (CountryKey, CountryCode, PartnerComment, CreatedBy, NavProtocol, NavPort, NavUsername, NavDomain, NavService, NavSoapPort, NavUseCommonCert, TranslationReceiverName,
         RepoOwner, RepoName, FilePathPrefix, Branch, BaseBranch, FeatureBranchName, RequesterEmail, RecipientEmail, CommitMessage,
         ServiceExists, DeploymentStatus, CorrelationId)
    VALUES
        ('royal-canin-france', 'fr', 'Royal Canin France NAV onboarding', 'integration-pulse-mulesoft-publisher', 'https', '7124', 'SVC_UAT_MULESOFT', 'mars-ad.net', 'InterfaceWebServices', '7124', 1, 'petc-rc-navision-3plspl-sys',
         'your-org', '3pl-config-repo', 'mulesoft/royal-canin-france', 'main', 'main', 'feature/royal-canin-france-onboarding', 'requester@example.com', 'devops-owner@example.com', 'Onboard Royal Canin France MuleSoft partner',
         0, 'Pending', 'test-mulesoft-001');
END

SELECT @MuleSoftPartnerId = Id FROM dbo.MuleSoftPartner WHERE CountryKey = 'royal-canin-france';

IF NOT EXISTS (SELECT 1 FROM dbo.MuleSoftEnvironment WHERE PartnerId = @MuleSoftPartnerId AND Environment = 'app')
    INSERT INTO dbo.MuleSoftEnvironment (PartnerId, Environment, NavHost, NavCompany, NavSoapPath, NavRoutingCode)
    VALUES (@MuleSoftPartnerId, 'app', 'azr-xxx1234.mars-ad.net', 'Company(UAT - Royal Canin France)', '/sop_fra_uat_01/WS/UAT - Royal Canin France/Codeunit/InterfaceWebServices?wsdl', 'NAV_FM_IN_UAT_FRA');

IF NOT EXISTS (SELECT 1 FROM dbo.MuleSoftEnvironment WHERE PartnerId = @MuleSoftPartnerId AND Environment = 'dev')
    INSERT INTO dbo.MuleSoftEnvironment (PartnerId, Environment, NavHost, NavCompany, NavSoapPath, NavRoutingCode)
    VALUES (@MuleSoftPartnerId, 'dev', 'azr-xxx1234-dev.mars-ad.net', 'Company(DEV - Royal Canin France)', '/sop_fra_dev_01/WS/DEV - Royal Canin France/Codeunit/InterfaceWebServices?wsdl', 'NAV_FM_IN_DEV_FRA');

IF NOT EXISTS (SELECT 1 FROM dbo.MuleSoftTransactionType WHERE PartnerId = @MuleSoftPartnerId AND TransactionTypeCode = 'sal_008')
    INSERT INTO dbo.MuleSoftTransactionType (PartnerId, TransactionTypeCode, TransactionTypeEnabled, TransactionTypeLabel)
    VALUES (@MuleSoftPartnerId, 'sal_008', 1, 'SHIPMENT');

IF NOT EXISTS (SELECT 1 FROM dbo.MuleSoftMessageType WHERE PartnerId = @MuleSoftPartnerId AND MessageType = 'despatchStock')
    INSERT INTO dbo.MuleSoftMessageType (PartnerId, MessageType)
    VALUES (@MuleSoftPartnerId, 'despatchStock');

IF NOT EXISTS (SELECT 1 FROM dbo.MuleSoftSourceDestination WHERE PartnerId = @MuleSoftPartnerId AND SourceDestinationFrom = 'PLANT-FR1-PLANT-FR2' AND SourceDestinationTo = 'spl_001')
    INSERT INTO dbo.MuleSoftSourceDestination (PartnerId, SourceDestinationFrom, SourceDestinationTo)
    VALUES (@MuleSoftPartnerId, 'PLANT-FR1-PLANT-FR2', 'spl_001');

IF NOT EXISTS (SELECT 1 FROM dbo.MuleSoftUomMapping WHERE PartnerId = @MuleSoftPartnerId AND UomFrom = 'EA' AND UomTo = 'UNIT')
    INSERT INTO dbo.MuleSoftUomMapping (PartnerId, UomFrom, UomTo)
    VALUES (@MuleSoftPartnerId, 'EA', 'UNIT');
GO

-- ============================================================================
-- Verify
-- ============================================================================
SELECT * FROM dbo.BtpConfig;
SELECT * FROM dbo.SolaceClient;
SELECT * FROM dbo.SolaceMessageType;
SELECT * FROM dbo.MuleSoftPartner;
SELECT * FROM dbo.MuleSoftEnvironment;
SELECT * FROM dbo.MuleSoftTransactionType;
SELECT * FROM dbo.MuleSoftMessageType;
SELECT * FROM dbo.MuleSoftSourceDestination;
SELECT * FROM dbo.MuleSoftUomMapping;
