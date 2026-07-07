-- 3PL Automation onboarding data store
-- Server: 3pl-automation-sql-server
-- Run this script as the admin (or an owner) login against the target database.
-- Do NOT point the Logic App's runtime connection at the admin login used to run
-- this script — see the login/user section at the bottom, which creates a
-- dedicated least-privileged identity for that purpose.
--
-- Solace and MuleSoft are each modeled as one parent (identity + settings)
-- table plus child tables for their repeating CSV rows, so every comma
-- separated value from the onboarding CSVs has its own typed column instead
-- of being stored/parsed as raw CSV text. BTP has no CSV/repeating-row
-- concept, so it stays a single flat table.

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'dbo')
    EXEC('CREATE SCHEMA dbo');
GO

-- ============================================================================
-- dbo.BtpConfig
-- Natural key: SubAccount + ProductName + Environment identifies one
-- deployable BTP app-creation/deployment entity (see btp-config-publish
-- workflow.json / TriggerBtpDeploymentFunction.cs for the source fields).
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'BtpConfig' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.BtpConfig
    (
        Id                  INT IDENTITY(1,1)      NOT NULL,
        SubAccount          NVARCHAR(200)           NOT NULL,
        ProductName         NVARCHAR(200)           NOT NULL,
        Environment         NVARCHAR(50)            NOT NULL,
        Mode                NVARCHAR(50)            NULL,
        DeveloperId         NVARCHAR(100)           NULL,
        Title               NVARCHAR(300)           NULL,
        ShortText           NVARCHAR(500)           NULL,
        RepoOwner           NVARCHAR(200)           NULL,
        RepoName            NVARCHAR(200)           NULL,
        WorkflowFileName    NVARCHAR(200)           NULL,
        BranchRef           NVARCHAR(200)           NULL,
        ServiceExists       BIT                     NULL,       -- NULL = not yet asked
        DeploymentStatus    NVARCHAR(20)            NOT NULL CONSTRAINT DF_BtpConfig_DeploymentStatus DEFAULT ('Pending'),
        CorrelationId       NVARCHAR(100)           NULL,
        CreatedAt           DATETIME2               NOT NULL CONSTRAINT DF_BtpConfig_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt           DATETIME2               NOT NULL CONSTRAINT DF_BtpConfig_UpdatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_BtpConfig PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_BtpConfig_Key UNIQUE (SubAccount, ProductName, Environment),
        CONSTRAINT CK_BtpConfig_DeploymentStatus CHECK (DeploymentStatus IN ('Pending', 'InProgress', 'Deployed', 'Failed'))
    );
END
GO

-- Enrichment tracking columns (added after initial rollout -- ALTER, not part
-- of the CREATE above, so this applies correctly to an already-created table
-- too). EnrichmentStatus is the flag a future orchestrator integration will
-- filter on ('Complete' = ready to use); CardSentAt/CardRespondedAt let
-- data-enrichment-notifier find not-yet-notified rows and detect timeouts.
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.BtpConfig') AND name = 'EnrichmentStatus')
    ALTER TABLE dbo.BtpConfig ADD EnrichmentStatus NVARCHAR(20) NOT NULL CONSTRAINT DF_BtpConfig_EnrichmentStatus DEFAULT ('AwaitingInput');
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_BtpConfig_EnrichmentStatus')
    ALTER TABLE dbo.BtpConfig ADD CONSTRAINT CK_BtpConfig_EnrichmentStatus CHECK (EnrichmentStatus IN ('AwaitingInput', 'Complete', 'CardTimedOut'));
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.BtpConfig') AND name = 'CardSentAt')
    ALTER TABLE dbo.BtpConfig ADD CardSentAt DATETIME2 NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.BtpConfig') AND name = 'CardRespondedAt')
    ALTER TABLE dbo.BtpConfig ADD CardRespondedAt DATETIME2 NULL;
GO

-- ============================================================================
-- dbo.SolaceClient (parent) + dbo.SolaceMessageType (child)
-- Mirrors the Solace onboarding CSV: Brand,Env,SystemName,ThreePLCode identify
-- one client (the CSV's "FullOnboarding" row carries the client-profile/ACL/
-- user settings), and every subsequent MessageType/Topic/Queue* row for that
-- same client becomes one dbo.SolaceMessageType row.
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SolaceClient' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.SolaceClient
    (
        Id                                                          INT IDENTITY(1,1)  NOT NULL,
        Brand                                                       NVARCHAR(100)       NOT NULL,
        Env                                                         NVARCHAR(50)        NOT NULL,
        SystemName                                                  NVARCHAR(200)       NOT NULL,
        ThreePLCode                                                 NVARCHAR(100)       NOT NULL,
        EncryptedPassword                                           NVARCHAR(500)       NULL,
        Action                                                      NVARCHAR(50)        NULL,
        ClientProfileAllowGuaranteedMsgSendEnabled                  BIT                 NULL,
        ClientProfileAllowGuaranteedMsgReceiveEnabled               BIT                 NULL,
        ClientProfileCompressionEnabled                             BIT                 NULL,
        ClientProfileReplicationAllowClientConnectWhenStandbyEnabled BIT                NULL,
        ClientProfileAllowTransactedSessionsEnabled                 BIT                 NULL,
        ClientProfileAllowBridgeConnectionsEnabled                  BIT                 NULL,
        ClientProfileAllowGuaranteedEndpointCreateEnabled           BIT                 NULL,
        ClientProfileAllowSharedSubscriptionsEnabled                BIT                 NULL,
        AclClientConnectDefaultAction                               NVARCHAR(20)        NULL,
        AclPublishTopicDefaultAction                                NVARCHAR(20)        NULL,
        AclSubscribeShareNameDefaultAction                          NVARCHAR(20)        NULL,
        AclSubscribeTopicDefaultAction                              NVARCHAR(20)        NULL,
        ClientUserEnabled                                           BIT                 NULL,
        ClientUserGuaranteedEndpointPermissionOverrideEnabled       BIT                 NULL,
        ClientUserSubscriptionManagerEnabled                        BIT                 NULL,
        -- pipeline / GitHub publish metadata (not part of the Solace CSV)
        RepoOwner           NVARCHAR(200)           NULL,
        RepoName            NVARCHAR(200)           NULL,
        FilePath            NVARCHAR(500)           NULL,
        Branch              NVARCHAR(200)           NULL,
        BaseBranch          NVARCHAR(200)           NULL,
        FeatureBranchName   NVARCHAR(200)           NULL,
        RequesterEmail      NVARCHAR(320)           NULL,
        RecipientEmail      NVARCHAR(320)           NULL,
        CommitMessage       NVARCHAR(500)           NULL,
        ServiceExists       BIT                     NULL,       -- NULL = not yet asked
        DeploymentStatus    NVARCHAR(20)            NOT NULL CONSTRAINT DF_SolaceClient_DeploymentStatus DEFAULT ('Pending'),
        CorrelationId       NVARCHAR(100)           NULL,
        CreatedAt           DATETIME2               NOT NULL CONSTRAINT DF_SolaceClient_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt           DATETIME2               NOT NULL CONSTRAINT DF_SolaceClient_UpdatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_SolaceClient PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_SolaceClient_Key UNIQUE (Brand, Env, SystemName, ThreePLCode),
        CONSTRAINT CK_SolaceClient_DeploymentStatus CHECK (DeploymentStatus IN ('Pending', 'InProgress', 'Deployed', 'Failed'))
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SolaceClient') AND name = 'EnrichmentStatus')
    ALTER TABLE dbo.SolaceClient ADD EnrichmentStatus NVARCHAR(20) NOT NULL CONSTRAINT DF_SolaceClient_EnrichmentStatus DEFAULT ('AwaitingInput');
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_SolaceClient_EnrichmentStatus')
    ALTER TABLE dbo.SolaceClient ADD CONSTRAINT CK_SolaceClient_EnrichmentStatus CHECK (EnrichmentStatus IN ('AwaitingInput', 'Complete', 'CardTimedOut'));
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SolaceClient') AND name = 'CardSentAt')
    ALTER TABLE dbo.SolaceClient ADD CardSentAt DATETIME2 NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SolaceClient') AND name = 'CardRespondedAt')
    ALTER TABLE dbo.SolaceClient ADD CardRespondedAt DATETIME2 NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SolaceMessageType' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.SolaceMessageType
    (
        Id                      INT IDENTITY(1,1)  NOT NULL,
        ClientId                INT                 NOT NULL,
        MessageType             NVARCHAR(200)       NOT NULL,
        Topic                   NVARCHAR(500)       NULL,
        QueuePermission         NVARCHAR(50)        NULL,
        QueueEgressEnabled      BIT                 NULL,
        QueueMaxRedeliveryCount INT                 NULL,
        CreatedAt               DATETIME2           NOT NULL CONSTRAINT DF_SolaceMessageType_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_SolaceMessageType PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_SolaceMessageType_Key UNIQUE (ClientId, MessageType, Topic),
        CONSTRAINT FK_SolaceMessageType_Client FOREIGN KEY (ClientId) REFERENCES dbo.SolaceClient (Id) ON DELETE CASCADE
    );
END
GO

-- ============================================================================
-- dbo.MuleSoftPartner (parent) + child tables per RowType in the MuleSoft CSV
-- (Environment, TransactionType, MessageType, SourceDestination, UomMapping).
-- CountryKey identifies one partner/country onboarding; the CSV's first row
-- carries the partner-level NAV connection fields, and every subsequent
-- RowType-discriminated row becomes one row in its matching child table.
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'MuleSoftPartner' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.MuleSoftPartner
    (
        Id                      INT IDENTITY(1,1)  NOT NULL,
        CountryKey              NVARCHAR(200)       NOT NULL,
        CountryCode             NVARCHAR(10)        NULL,
        PartnerComment          NVARCHAR(500)       NULL,
        CreatedBy               NVARCHAR(200)       NULL,
        NavProtocol             NVARCHAR(20)        NULL,
        NavPort                 NVARCHAR(10)        NULL,
        NavUsername             NVARCHAR(200)       NULL,
        NavDomain               NVARCHAR(200)       NULL,
        NavService              NVARCHAR(200)       NULL,
        NavSoapPort             NVARCHAR(10)        NULL,
        NavUseCommonCert        BIT                 NULL,
        TranslationReceiverName NVARCHAR(200)       NULL,
        -- pipeline / GitHub publish metadata (not part of the MuleSoft CSV)
        RepoOwner           NVARCHAR(200)           NULL,
        RepoName            NVARCHAR(200)           NULL,
        FilePathPrefix      NVARCHAR(500)           NULL,
        Branch              NVARCHAR(200)           NULL,
        BaseBranch          NVARCHAR(200)           NULL,
        FeatureBranchName   NVARCHAR(200)           NULL,
        RequesterEmail      NVARCHAR(320)           NULL,
        RecipientEmail      NVARCHAR(320)           NULL,
        CommitMessage       NVARCHAR(500)           NULL,
        ServiceExists       BIT                     NULL,       -- NULL = not yet asked
        DeploymentStatus    NVARCHAR(20)            NOT NULL CONSTRAINT DF_MuleSoftPartner_DeploymentStatus DEFAULT ('Pending'),
        CorrelationId       NVARCHAR(100)           NULL,
        CreatedAt           DATETIME2               NOT NULL CONSTRAINT DF_MuleSoftPartner_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt           DATETIME2               NOT NULL CONSTRAINT DF_MuleSoftPartner_UpdatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_MuleSoftPartner PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_MuleSoftPartner_Key UNIQUE (CountryKey),
        CONSTRAINT CK_MuleSoftPartner_DeploymentStatus CHECK (DeploymentStatus IN ('Pending', 'InProgress', 'Deployed', 'Failed'))
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.MuleSoftPartner') AND name = 'EnrichmentStatus')
    ALTER TABLE dbo.MuleSoftPartner ADD EnrichmentStatus NVARCHAR(20) NOT NULL CONSTRAINT DF_MuleSoftPartner_EnrichmentStatus DEFAULT ('AwaitingInput');
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_MuleSoftPartner_EnrichmentStatus')
    ALTER TABLE dbo.MuleSoftPartner ADD CONSTRAINT CK_MuleSoftPartner_EnrichmentStatus CHECK (EnrichmentStatus IN ('AwaitingInput', 'Complete', 'CardTimedOut'));
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.MuleSoftPartner') AND name = 'CardSentAt')
    ALTER TABLE dbo.MuleSoftPartner ADD CardSentAt DATETIME2 NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.MuleSoftPartner') AND name = 'CardRespondedAt')
    ALTER TABLE dbo.MuleSoftPartner ADD CardRespondedAt DATETIME2 NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'MuleSoftEnvironment' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.MuleSoftEnvironment
    (
        Id              INT IDENTITY(1,1)  NOT NULL,
        PartnerId       INT                 NOT NULL,
        Environment     NVARCHAR(50)        NOT NULL,
        NavHost         NVARCHAR(200)       NULL,
        NavCompany      NVARCHAR(300)       NULL,
        NavSoapPath     NVARCHAR(1000)      NULL,
        NavRoutingCode  NVARCHAR(200)       NULL,
        CreatedAt       DATETIME2           NOT NULL CONSTRAINT DF_MuleSoftEnvironment_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_MuleSoftEnvironment PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_MuleSoftEnvironment_Key UNIQUE (PartnerId, Environment),
        CONSTRAINT FK_MuleSoftEnvironment_Partner FOREIGN KEY (PartnerId) REFERENCES dbo.MuleSoftPartner (Id) ON DELETE CASCADE
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'MuleSoftTransactionType' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.MuleSoftTransactionType
    (
        Id                      INT IDENTITY(1,1)  NOT NULL,
        PartnerId               INT                 NOT NULL,
        TransactionTypeCode     NVARCHAR(100)       NOT NULL,
        TransactionTypeEnabled  BIT                 NULL,
        TransactionTypeLabel    NVARCHAR(200)       NULL,
        CreatedAt               DATETIME2           NOT NULL CONSTRAINT DF_MuleSoftTransactionType_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_MuleSoftTransactionType PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_MuleSoftTransactionType_Key UNIQUE (PartnerId, TransactionTypeCode),
        CONSTRAINT FK_MuleSoftTransactionType_Partner FOREIGN KEY (PartnerId) REFERENCES dbo.MuleSoftPartner (Id) ON DELETE CASCADE
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'MuleSoftMessageType' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.MuleSoftMessageType
    (
        Id          INT IDENTITY(1,1)  NOT NULL,
        PartnerId   INT                 NOT NULL,
        MessageType NVARCHAR(200)       NOT NULL,
        CreatedAt   DATETIME2           NOT NULL CONSTRAINT DF_MuleSoftMessageType_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_MuleSoftMessageType PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_MuleSoftMessageType_Key UNIQUE (PartnerId, MessageType),
        CONSTRAINT FK_MuleSoftMessageType_Partner FOREIGN KEY (PartnerId) REFERENCES dbo.MuleSoftPartner (Id) ON DELETE CASCADE
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'MuleSoftSourceDestination' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.MuleSoftSourceDestination
    (
        Id                      INT IDENTITY(1,1)  NOT NULL,
        PartnerId               INT                 NOT NULL,
        SourceDestinationFrom   NVARCHAR(200)       NOT NULL,
        SourceDestinationTo     NVARCHAR(200)       NOT NULL,
        CreatedAt               DATETIME2           NOT NULL CONSTRAINT DF_MuleSoftSourceDestination_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_MuleSoftSourceDestination PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_MuleSoftSourceDestination_Key UNIQUE (PartnerId, SourceDestinationFrom, SourceDestinationTo),
        CONSTRAINT FK_MuleSoftSourceDestination_Partner FOREIGN KEY (PartnerId) REFERENCES dbo.MuleSoftPartner (Id) ON DELETE CASCADE
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'MuleSoftUomMapping' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.MuleSoftUomMapping
    (
        Id          INT IDENTITY(1,1)  NOT NULL,
        PartnerId   INT                 NOT NULL,
        UomFrom     NVARCHAR(50)        NOT NULL,
        UomTo       NVARCHAR(50)        NOT NULL,
        CreatedAt   DATETIME2           NOT NULL CONSTRAINT DF_MuleSoftUomMapping_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_MuleSoftUomMapping PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_MuleSoftUomMapping_Key UNIQUE (PartnerId, UomFrom, UomTo),
        CONSTRAINT FK_MuleSoftUomMapping_Partner FOREIGN KEY (PartnerId) REFERENCES dbo.MuleSoftPartner (Id) ON DELETE CASCADE
    );
END
GO

-- ============================================================================
-- dbo.EnrichmentAuditLog
-- Append-only event trail across every data-enrichment channel/workflow
-- (data-enrichment, data-enrichment-notifier, data-enrichment-mail-intake).
-- "WHERE CorrelationId = X" reconstructs a request's full lifecycle
-- regardless of which channel touched it -- the actual mechanism behind
-- end-to-end tracking, since App Insights logging is not durable today.
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'EnrichmentAuditLog' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.EnrichmentAuditLog
    (
        Id              BIGINT IDENTITY(1,1)   NOT NULL,
        Domain          NVARCHAR(20)            NOT NULL,
        CorrelationId   NVARCHAR(100)           NOT NULL,
        EntityKey       NVARCHAR(400)           NULL,
        Channel         NVARCHAR(20)            NOT NULL,
        ActorEmail      NVARCHAR(320)           NULL,       -- PII: requester/responder identity when known
        EventType       NVARCHAR(50)            NOT NULL,
        EventDetail     NVARCHAR(MAX)           NULL,
        CreatedAt       DATETIME2               NOT NULL CONSTRAINT DF_EnrichmentAuditLog_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_EnrichmentAuditLog PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_EnrichmentAuditLog_Channel CHECK (Channel IN ('Api', 'Mail', 'AdaptiveCard', 'System')),
        CONSTRAINT CK_EnrichmentAuditLog_EventType CHECK (EventType IN (
            'Received', 'ValidationFailed', 'Upserted', 'CardSent', 'CardResponded',
            'CardTimedOut', 'MailRejected', 'Error'
        ))
    );
    CREATE INDEX IX_EnrichmentAuditLog_CorrelationId ON dbo.EnrichmentAuditLog (CorrelationId);
END
GO

-- ============================================================================
-- Least-privileged identity for the Logic App's built-in SQL connection.
-- This is an Azure SQL Database CONTAINED user (password lives in this
-- database, no server-level CREATE LOGIN / [master] step required) -- the
-- right pattern when you only have the Portal's Query Editor, since it
-- connects straight to this database and can't switch context to [master].
-- Do NOT use the server admin login (threepl-automation-sql-server-admin) as
-- the Logic App's runtime credential -- use this dedicated identity instead,
-- with its own fresh password (the admin password shared out-of-band for
-- this work should still be rotated in the Azure portal regardless).
-- DELETE is granted (in addition to SELECT/INSERT/UPDATE) because the child
-- tables are refreshed by deleting a parent's existing rows and re-inserting
-- the current set on every enrichment call.
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'threepl-logicapp-svc')
BEGIN
    CREATE USER [threepl-logicapp-svc] WITH PASSWORD = '<generate a new strong password>';
END
GO

GRANT SELECT, INSERT, UPDATE         ON dbo.BtpConfig                  TO [threepl-logicapp-svc];
GRANT SELECT, INSERT, UPDATE         ON dbo.SolaceClient               TO [threepl-logicapp-svc];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.SolaceMessageType          TO [threepl-logicapp-svc];
GRANT SELECT, INSERT, UPDATE         ON dbo.MuleSoftPartner            TO [threepl-logicapp-svc];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.MuleSoftEnvironment        TO [threepl-logicapp-svc];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.MuleSoftTransactionType    TO [threepl-logicapp-svc];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.MuleSoftMessageType        TO [threepl-logicapp-svc];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.MuleSoftSourceDestination  TO [threepl-logicapp-svc];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.MuleSoftUomMapping         TO [threepl-logicapp-svc];
GRANT SELECT, INSERT                 ON dbo.EnrichmentAuditLog          TO [threepl-logicapp-svc];
GO

-- ============================================================================
-- 2026 Integration Updates: Orchestration event types and tracking indexes
-- ============================================================================

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_EnrichmentAuditLog_EventType' AND parent_object_id = OBJECT_ID('dbo.EnrichmentAuditLog'))
BEGIN
    ALTER TABLE dbo.EnrichmentAuditLog DROP CONSTRAINT CK_EnrichmentAuditLog_EventType;
END
GO
ALTER TABLE dbo.EnrichmentAuditLog ADD CONSTRAINT CK_EnrichmentAuditLog_EventType CHECK (EventType IN (
    'Received', 'ValidationFailed', 'Upserted', 'CardSent', 'CardResponded',
    'CardTimedOut', 'MailRejected', 'Error', 'OrchestrationStarted', 'OrchestrationSucceeded', 'OrchestrationFailed'
));
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BtpConfig_CorrelationId' AND object_id = OBJECT_ID('dbo.BtpConfig'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_BtpConfig_CorrelationId ON dbo.BtpConfig(CorrelationId);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SolaceClient_CorrelationId' AND object_id = OBJECT_ID('dbo.SolaceClient'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_SolaceClient_CorrelationId ON dbo.SolaceClient(CorrelationId);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MuleSoftPartner_CorrelationId' AND object_id = OBJECT_ID('dbo.MuleSoftPartner'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_MuleSoftPartner_CorrelationId ON dbo.MuleSoftPartner(CorrelationId);
END
GO

-- ============================================================================
-- Email-only enrichment/approval flow: Direction (Inbound/Outbound), the new
-- whole-onboarding architecture-approval gate, and per-domain branch-approval
-- state -- replacing the removed Teams adaptive cards everywhere.
-- ============================================================================

-- Direction: Inbound rows skip the SME enrichment chase entirely (see
-- data-enrichment's Compose_<Domain>_Enrichment_Status). Default 'Outbound'
-- preserves today's chase-loop behavior for every existing/future row unless
-- explicitly marked Inbound.
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.BtpConfig') AND name = 'Direction')
    ALTER TABLE dbo.BtpConfig ADD Direction NVARCHAR(20) NOT NULL CONSTRAINT DF_BtpConfig_Direction DEFAULT ('Outbound');
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_BtpConfig_Direction')
    ALTER TABLE dbo.BtpConfig ADD CONSTRAINT CK_BtpConfig_Direction CHECK (Direction IN ('Inbound', 'Outbound'));
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SolaceClient') AND name = 'Direction')
    ALTER TABLE dbo.SolaceClient ADD Direction NVARCHAR(20) NOT NULL CONSTRAINT DF_SolaceClient_Direction DEFAULT ('Outbound');
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_SolaceClient_Direction')
    ALTER TABLE dbo.SolaceClient ADD CONSTRAINT CK_SolaceClient_Direction CHECK (Direction IN ('Inbound', 'Outbound'));
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.MuleSoftPartner') AND name = 'Direction')
    ALTER TABLE dbo.MuleSoftPartner ADD Direction NVARCHAR(20) NOT NULL CONSTRAINT DF_MuleSoftPartner_Direction DEFAULT ('Outbound');
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_MuleSoftPartner_Direction')
    ALTER TABLE dbo.MuleSoftPartner ADD CONSTRAINT CK_MuleSoftPartner_Direction CHECK (Direction IN ('Inbound', 'Outbound'));
GO

-- dbo.OnboardingApproval -- one row per CorrelationId (the architecture
-- approval gate is a whole-onboarding concept, not per-domain like
-- enrichment). Read/written by onboarding-launcher, data-enrichment-mail-intake,
-- and the new approval-resume poller.
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'OnboardingApproval' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.OnboardingApproval
    (
        Id                          INT IDENTITY(1,1)  NOT NULL,
        CorrelationId               NVARCHAR(100)       NOT NULL,
        ArchitectureApprovalStatus  NVARCHAR(20)        NOT NULL CONSTRAINT DF_OnboardingApproval_Status DEFAULT ('Pending'),
        RequestedAt                 DATETIME2           NULL,
        RespondedAt                 DATETIME2           NULL,
        ApproverEmail               NVARCHAR(320)       NULL,
        CreatedAt                   DATETIME2           NOT NULL CONSTRAINT DF_OnboardingApproval_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt                   DATETIME2           NOT NULL CONSTRAINT DF_OnboardingApproval_UpdatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_OnboardingApproval PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_OnboardingApproval_CorrelationId UNIQUE (CorrelationId),
        CONSTRAINT CK_OnboardingApproval_Status CHECK (ArchitectureApprovalStatus IN ('Pending', 'Approved', 'Rejected'))
    );
    CREATE NONCLUSTERED INDEX IX_OnboardingApproval_Status ON dbo.OnboardingApproval (ArchitectureApprovalStatus);
END
GO

GRANT SELECT, INSERT, UPDATE ON dbo.OnboardingApproval TO [threepl-logicapp-svc];
GO

-- Branch-approval-via-email columns (Solace/MuleSoft only -- BTP never runs a
-- branch-existence check today).
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SolaceClient') AND name = 'BranchApprovalStatus')
    ALTER TABLE dbo.SolaceClient ADD BranchApprovalStatus NVARCHAR(20) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_SolaceClient_BranchApprovalStatus')
    ALTER TABLE dbo.SolaceClient ADD CONSTRAINT CK_SolaceClient_BranchApprovalStatus CHECK (BranchApprovalStatus IS NULL OR BranchApprovalStatus IN ('Pending', 'Approved', 'Rejected'));
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SolaceClient') AND name = 'PendingBranchName')
    ALTER TABLE dbo.SolaceClient ADD PendingBranchName NVARCHAR(200) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.MuleSoftPartner') AND name = 'BranchApprovalStatus')
    ALTER TABLE dbo.MuleSoftPartner ADD BranchApprovalStatus NVARCHAR(20) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_MuleSoftPartner_BranchApprovalStatus')
    ALTER TABLE dbo.MuleSoftPartner ADD CONSTRAINT CK_MuleSoftPartner_BranchApprovalStatus CHECK (BranchApprovalStatus IS NULL OR BranchApprovalStatus IN ('Pending', 'Approved', 'Rejected'));
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.MuleSoftPartner') AND name = 'PendingBranchName')
    ALTER TABLE dbo.MuleSoftPartner ADD PendingBranchName NVARCHAR(200) NULL;
GO

-- Extend DeploymentStatus CHECK with 'AwaitingBranchApproval' (Solace/MuleSoft
-- only) -- SQL Server has no ALTER CONSTRAINT-modify, so drop+recreate,
-- matching the pattern already used for CK_EnrichmentAuditLog_EventType.
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_SolaceClient_DeploymentStatus' AND parent_object_id = OBJECT_ID('dbo.SolaceClient'))
    ALTER TABLE dbo.SolaceClient DROP CONSTRAINT CK_SolaceClient_DeploymentStatus;
GO
ALTER TABLE dbo.SolaceClient ADD CONSTRAINT CK_SolaceClient_DeploymentStatus
    CHECK (DeploymentStatus IN ('Pending', 'InProgress', 'Deployed', 'Failed', 'AwaitingBranchApproval'));
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_MuleSoftPartner_DeploymentStatus' AND parent_object_id = OBJECT_ID('dbo.MuleSoftPartner'))
    ALTER TABLE dbo.MuleSoftPartner DROP CONSTRAINT CK_MuleSoftPartner_DeploymentStatus;
GO
ALTER TABLE dbo.MuleSoftPartner ADD CONSTRAINT CK_MuleSoftPartner_DeploymentStatus
    CHECK (DeploymentStatus IN ('Pending', 'InProgress', 'Deployed', 'Failed', 'AwaitingBranchApproval'));
GO

-- New EventType values for the email-based approval events. No Channel CHECK
-- change needed -- 'System' (email sent by a workflow) and 'Mail' (reply
-- received) already cover both directions. 'CardSent'/'CardResponded'/
-- 'CardTimedOut' literals are kept as-is (no rename) to avoid a historical
-- data migration -- only the mechanism producing them changes, not the enum.
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_EnrichmentAuditLog_EventType' AND parent_object_id = OBJECT_ID('dbo.EnrichmentAuditLog'))
    ALTER TABLE dbo.EnrichmentAuditLog DROP CONSTRAINT CK_EnrichmentAuditLog_EventType;
GO
ALTER TABLE dbo.EnrichmentAuditLog ADD CONSTRAINT CK_EnrichmentAuditLog_EventType CHECK (EventType IN (
    'Received', 'ValidationFailed', 'Upserted', 'CardSent', 'CardResponded',
    'CardTimedOut', 'MailRejected', 'Error', 'OrchestrationStarted', 'OrchestrationSucceeded', 'OrchestrationFailed',
    'ArchitectureApprovalRequested', 'ArchitectureApprovalApproved', 'ArchitectureApprovalRejected',
    'BranchApprovalRequested', 'BranchApprovalApproved', 'BranchApprovalRejected'
));
GO

-- DeploymentStatus was widened by the CHECK constraint above to allow
-- 'AwaitingBranchApproval' (22 chars), but the column itself was left at
-- NVARCHAR(20) -- every attempt to set that status failed with a truncation
-- error. sys.columns.max_length is in bytes (NVARCHAR = 2 bytes/char), so
-- NVARCHAR(20) reads as 40 here.
IF (SELECT max_length FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SolaceClient') AND name = 'DeploymentStatus') < 60
    ALTER TABLE dbo.SolaceClient ALTER COLUMN DeploymentStatus NVARCHAR(30) NOT NULL;
GO

IF (SELECT max_length FROM sys.columns WHERE object_id = OBJECT_ID('dbo.MuleSoftPartner') AND name = 'DeploymentStatus') < 60
    ALTER TABLE dbo.MuleSoftPartner ALTER COLUMN DeploymentStatus NVARCHAR(30) NOT NULL;
GO

