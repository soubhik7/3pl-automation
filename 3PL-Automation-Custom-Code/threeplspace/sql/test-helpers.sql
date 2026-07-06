-- Test-cycle helper queries for the data-enrichment tables.
-- Run sections individually (select the block you want, then Run) --
-- this file is a toolbox, not a single script to run top-to-bottom.

-- ============================================================================
-- 0) Row counts across all 10 tables -- quick "what's in the DB right now"
-- ============================================================================
SELECT 'BtpConfig' AS TableName, COUNT(*) AS Rows FROM dbo.BtpConfig
UNION ALL SELECT 'SolaceClient', COUNT(*) FROM dbo.SolaceClient
UNION ALL SELECT 'SolaceMessageType', COUNT(*) FROM dbo.SolaceMessageType
UNION ALL SELECT 'MuleSoftPartner', COUNT(*) FROM dbo.MuleSoftPartner
UNION ALL SELECT 'MuleSoftEnvironment', COUNT(*) FROM dbo.MuleSoftEnvironment
UNION ALL SELECT 'MuleSoftTransactionType', COUNT(*) FROM dbo.MuleSoftTransactionType
UNION ALL SELECT 'MuleSoftMessageType', COUNT(*) FROM dbo.MuleSoftMessageType
UNION ALL SELECT 'MuleSoftSourceDestination', COUNT(*) FROM dbo.MuleSoftSourceDestination
UNION ALL SELECT 'MuleSoftUomMapping', COUNT(*) FROM dbo.MuleSoftUomMapping
UNION ALL SELECT 'EnrichmentAuditLog', COUNT(*) FROM dbo.EnrichmentAuditLog;

-- ============================================================================
-- 1) Fetch everything -- one SELECT per table
-- ============================================================================
SELECT * FROM dbo.BtpConfig ORDER BY UpdatedAt DESC;
SELECT * FROM dbo.SolaceClient ORDER BY UpdatedAt DESC;
SELECT * FROM dbo.SolaceMessageType ORDER BY ClientId, MessageType, Topic;
SELECT * FROM dbo.MuleSoftPartner ORDER BY UpdatedAt DESC;
SELECT * FROM dbo.MuleSoftEnvironment ORDER BY PartnerId, Environment;
SELECT * FROM dbo.MuleSoftTransactionType ORDER BY PartnerId, TransactionTypeCode;
SELECT * FROM dbo.MuleSoftMessageType ORDER BY PartnerId, MessageType;
SELECT * FROM dbo.MuleSoftSourceDestination ORDER BY PartnerId, SourceDestinationFrom;
SELECT * FROM dbo.MuleSoftUomMapping ORDER BY PartnerId, UomFrom;
SELECT * FROM dbo.EnrichmentAuditLog ORDER BY CreatedAt DESC;

-- ============================================================================
-- 2) Parent + children in one shot (handy after a MuleSoft/Solace test run)
-- ============================================================================
SELECT p.*, c.MessageType, c.Topic, c.QueuePermission, c.QueueEgressEnabled, c.QueueMaxRedeliveryCount
FROM dbo.SolaceClient p
LEFT JOIN dbo.SolaceMessageType c ON c.ClientId = p.Id
ORDER BY p.UpdatedAt DESC;

SELECT p.CountryKey, p.EnrichmentStatus, p.CardSentAt, p.CardRespondedAt,
       e.Environment, e.NavHost,
       t.TransactionTypeCode, t.TransactionTypeLabel,
       m.MessageType,
       sd.SourceDestinationFrom, sd.SourceDestinationTo,
       u.UomFrom, u.UomTo
FROM dbo.MuleSoftPartner p
LEFT JOIN dbo.MuleSoftEnvironment e ON e.PartnerId = p.Id
LEFT JOIN dbo.MuleSoftTransactionType t ON t.PartnerId = p.Id
LEFT JOIN dbo.MuleSoftMessageType m ON m.PartnerId = p.Id
LEFT JOIN dbo.MuleSoftSourceDestination sd ON sd.PartnerId = p.Id
LEFT JOIN dbo.MuleSoftUomMapping u ON u.PartnerId = p.Id
ORDER BY p.UpdatedAt DESC;

-- ============================================================================
-- 3) Trace one test run end-to-end by CorrelationId
-- ============================================================================
DECLARE @TestCorrelationId NVARCHAR(100) = 'test-solace-001';  -- <-- change this
SELECT * FROM dbo.EnrichmentAuditLog WHERE CorrelationId = @TestCorrelationId ORDER BY CreatedAt;

-- ============================================================================
-- 4) Notifier visibility -- what's pending, what's already been notified,
--    what's still waiting on a human, what timed out
-- ============================================================================
-- Not yet picked up by the notifier (CardSentAt IS NULL):
SELECT 'Btp' AS Domain, SubAccount AS Key1, ProductName AS Key2, Environment AS Key3, EnrichmentStatus, CardSentAt, CardRespondedAt FROM dbo.BtpConfig WHERE EnrichmentStatus = 'AwaitingInput' AND CardSentAt IS NULL
UNION ALL
SELECT 'Solace', Brand, SystemName, ThreePLCode, EnrichmentStatus, CardSentAt, CardRespondedAt FROM dbo.SolaceClient WHERE EnrichmentStatus = 'AwaitingInput' AND CardSentAt IS NULL
UNION ALL
SELECT 'MuleSoft', CountryKey, NULL, NULL, EnrichmentStatus, CardSentAt, CardRespondedAt FROM dbo.MuleSoftPartner WHERE EnrichmentStatus = 'AwaitingInput' AND CardSentAt IS NULL;

-- Notified, still waiting on a human response:
SELECT 'Btp' AS Domain, SubAccount AS Key1, CardSentAt FROM dbo.BtpConfig WHERE EnrichmentStatus = 'AwaitingInput' AND CardSentAt IS NOT NULL
UNION ALL
SELECT 'Solace', Brand, CardSentAt FROM dbo.SolaceClient WHERE EnrichmentStatus = 'AwaitingInput' AND CardSentAt IS NOT NULL
UNION ALL
SELECT 'MuleSoft', CountryKey, CardSentAt FROM dbo.MuleSoftPartner WHERE EnrichmentStatus = 'AwaitingInput' AND CardSentAt IS NOT NULL;

-- Timed out waiting for a response:
SELECT 'Btp' AS Domain, SubAccount AS Key1, CardSentAt FROM dbo.BtpConfig WHERE EnrichmentStatus = 'CardTimedOut'
UNION ALL
SELECT 'Solace', Brand, CardSentAt FROM dbo.SolaceClient WHERE EnrichmentStatus = 'CardTimedOut'
UNION ALL
SELECT 'MuleSoft', CountryKey, CardSentAt FROM dbo.MuleSoftPartner WHERE EnrichmentStatus = 'CardTimedOut';

-- ============================================================================
-- 5a) SOFT reset -- delete test rows only, by natural key (safe, targeted).
--     Deleting the parent cascades to its child tables automatically
--     (ON DELETE CASCADE), so you don't need separate child DELETEs.
-- ============================================================================
DELETE FROM dbo.BtpConfig      WHERE SubAccount = 'royal-canin-france-uat' AND ProductName = 'royal-canin-france-integration' AND Environment = 'UAT';
DELETE FROM dbo.SolaceClient   WHERE Brand = 'petc' AND Env IN ('rc', 'qa') AND SystemName = 'navision';
DELETE FROM dbo.MuleSoftPartner WHERE CountryKey IN ('royal-canin-france', 'royal-canin-belgium');

-- ============================================================================
-- 5b) FULL reset -- wipe every domain table and the audit log, reseed
--     identities back to 1. Uncomment to run -- this deletes ALL data, not
--     just test rows.
--
-- NOTE: TRUNCATE TABLE is blocked on any table that has an INCOMING foreign
-- key -- this is a schema-level restriction, not a data one, so it still
-- applies even after the child tables are already empty. SolaceClient and
-- MuleSoftPartner are both referenced by FKs, so they must use DELETE
-- (+ DBCC CHECKIDENT to mimic TRUNCATE's identity-reset) instead of TRUNCATE.
-- The 6 child tables and BtpConfig/EnrichmentAuditLog have no incoming FKs,
-- so TRUNCATE works fine on those.
-- ============================================================================
-- TRUNCATE TABLE dbo.SolaceMessageType;
-- TRUNCATE TABLE dbo.MuleSoftEnvironment;
-- TRUNCATE TABLE dbo.MuleSoftTransactionType;
-- TRUNCATE TABLE dbo.MuleSoftMessageType;
-- TRUNCATE TABLE dbo.MuleSoftSourceDestination;
-- TRUNCATE TABLE dbo.MuleSoftUomMapping;
-- TRUNCATE TABLE dbo.BtpConfig;
-- TRUNCATE TABLE dbo.EnrichmentAuditLog;
--
-- DELETE FROM dbo.SolaceClient;
-- DBCC CHECKIDENT ('dbo.SolaceClient', RESEED, 0);
-- DELETE FROM dbo.MuleSoftPartner;
-- DBCC CHECKIDENT ('dbo.MuleSoftPartner', RESEED, 0);

-- ============================================================================
-- 6) Manually flip a row back to AwaitingInput + un-notified, to force the
--    notifier to pick it up again on its next tick without a fresh Postman call
-- ============================================================================
-- UPDATE dbo.SolaceClient SET EnrichmentStatus = 'AwaitingInput', CardSentAt = NULL, CardRespondedAt = NULL
-- WHERE Brand = 'petc' AND Env = 'qa' AND SystemName = 'navision' AND ThreePLCode = '3plpnp2';
