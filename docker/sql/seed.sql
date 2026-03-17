-- EventProcessor Settings Seed
-- Inserts application settings into dbo.Settings for SqlSettings configuration provider.
-- Keys use .NET configuration path format (colon-separated).
-- Run AFTER init.sql has created the schema.
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
USE FraudEngine;
GO

-- =============================================================================
-- Helper: Upsert settings so the script is re-runnable
-- =============================================================================
IF OBJECT_ID('tempdb..#UpsertSetting') IS NOT NULL DROP PROCEDURE #UpsertSetting;
GO

CREATE PROCEDURE #UpsertSetting
    @AppId   NVARCHAR(200),
    @Key     NVARCHAR(2048),
    @Value   NVARCHAR(MAX),
    @Comment VARCHAR(4000) = NULL
AS
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.Settings WHERE ApplicationId = @AppId AND [Key] = @Key AND InstanceId IS NULL)
    BEGIN
        UPDATE dbo.Settings
        SET [Value] = @Value, ModifiedBy = 'seed.sql', ModifiedDate = SYSUTCDATETIME(), [Comment] = COALESCE(@Comment, [Comment])
        WHERE ApplicationId = @AppId AND [Key] = @Key AND InstanceId IS NULL;
    END
    ELSE
    BEGIN
        INSERT INTO dbo.Settings (ApplicationId, [Key], [Value], IsSecret, ValueEncrypted, CreatedBy, CreatedDate, ModifiedBy, ModifiedDate, [Comment])
        VALUES (@AppId, @Key, @Value, 0, 0, 'seed.sql', SYSUTCDATETIME(), 'seed.sql', SYSUTCDATETIME(), @Comment);
    END
END
GO

-- =============================================================================
-- Kafka Configuration (KoreForge.Kafka)
-- =============================================================================

EXEC #UpsertSetting 'EventProcessor', 'Kafka:Defaults:GroupIdTemplate',                     'dev-EventProcessor-{ProfileName}-{Random4}',   'Kafka consumer group ID pattern';
EXEC #UpsertSetting 'EventProcessor', 'Kafka:Clusters:local:BootstrapServers',              'localhost:29092',                               'Redpanda broker address';
EXEC #UpsertSetting 'EventProcessor', 'Kafka:Clusters:local:ConfluentOptions:client.id',    'eventprocessor',                                'Confluent client identifier';

-- Default consumer profile
EXEC #UpsertSetting 'EventProcessor', 'Kafka:Profiles:Default:Type',                        'Consumer',     NULL;
EXEC #UpsertSetting 'EventProcessor', 'Kafka:Profiles:Default:Cluster',                     'local',        NULL;
EXEC #UpsertSetting 'EventProcessor', 'Kafka:Profiles:Default:Topics:0',                    'fraud.transactions', 'Primary input topic';
EXEC #UpsertSetting 'EventProcessor', 'Kafka:Profiles:Default:ExtendedConsumer:ConsumerCount',    '1',      NULL;
EXEC #UpsertSetting 'EventProcessor', 'Kafka:Profiles:Default:ExtendedConsumer:StartMode',        'Earliest', NULL;
EXEC #UpsertSetting 'EventProcessor', 'Kafka:Profiles:Default:ExtendedConsumer:MaxBatchSize',     '500',    NULL;
EXEC #UpsertSetting 'EventProcessor', 'Kafka:Profiles:Default:ExtendedConsumer:MaxBatchWaitMs',   '1000',   NULL;
EXEC #UpsertSetting 'EventProcessor', 'Kafka:Profiles:Default:ExtendedConsumer:MaxInFlightBatches','4',     NULL;

-- =============================================================================
-- FraudEngine Processing
-- =============================================================================

EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:Processing:ConsumerThreads',        '8',       NULL;
EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:Processing:BucketCount',            '64',      NULL;
EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:Processing:BucketQueueCapacity',    '50000',   NULL;
EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:Processing:MaxBatchSize',           '500',     NULL;
EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:Processing:BatchTimeoutMs',         '100',     NULL;

-- =============================================================================
-- FraudEngine Flush
-- =============================================================================

EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:Flush:TimeBasedIntervalMs',         '5000',    NULL;
EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:Flush:CountThreshold',              '1000',    NULL;
EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:Flush:DirtyRatioThreshold',         '0.3',     NULL;
EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:Flush:MemoryPressureThreshold',     '0.85',    NULL;

-- =============================================================================
-- FraudEngine Sessions
-- =============================================================================

EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:Sessions:IdleTimeoutMinutes',       '30',      NULL;
EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:Sessions:MaxTransactionsPerSession','10000',   NULL;
EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:Sessions:MaxSessionDurationMinutes','1440',    'Max session = 24 hours';

-- =============================================================================
-- FraudEngine Scoring
-- =============================================================================

EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:Scoring:BaseScore',                '0.0',      NULL;
EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:Scoring:DecisionThreshold',        '0.75',     'Score >= threshold triggers fraud flag';
EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:Scoring:HighScoreAlertThreshold',  '0.5',      'Score >= threshold generates alert';

-- =============================================================================
-- FraudEngine SQL Sink
-- =============================================================================

EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:SqlSink:ConnectionString',         'Server=localhost,14333;Database=FraudEngine;User Id=sa;Password=FraudEngine!Pass123;TrustServerCertificate=True', NULL;
EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:SqlSink:BatchSize',                '500',      NULL;
EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:SqlSink:TimeoutSeconds',           '30',       NULL;

-- =============================================================================
-- FraudEngine Rules
-- =============================================================================

EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:Rules:0:Name',          'HighAmount',       NULL;
EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:Rules:0:Expression',    'amount > 10000',   'Single transaction over $10k';
EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:Rules:0:ScoreModifier', '0.3',              NULL;
EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:Rules:0:Enabled',       'true',             NULL;

EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:Rules:1:Name',          'VeryHighAmount',   NULL;
EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:Rules:1:Expression',    'amount > 50000',   'Single transaction over $50k';
EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:Rules:1:ScoreModifier', '0.6',              NULL;
EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:Rules:1:Enabled',       'true',             NULL;

EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:Rules:2:Name',          'RapidTransactions', NULL;
EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:Rules:2:Expression',    'session.transactionCount > 10', 'Many transactions in a session';
EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:Rules:2:ScoreModifier', '0.2',              NULL;
EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:Rules:2:Enabled',       'true',             NULL;

EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:Rules:3:Name',          'UnusualLocation',  NULL;
EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:Rules:3:Expression',    'tx.countryCode != session.baseCountry', 'Country mismatch';
EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:Rules:3:ScoreModifier', '0.25',             NULL;
EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:Rules:3:Enabled',       'true',             NULL;

EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:Rules:4:Name',          'LargeSessionTotal', NULL;
EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:Rules:4:Expression',    'session.totalAmount > 100000', 'Session total over $100k';
EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:Rules:4:ScoreModifier', '0.35',             NULL;
EXEC #UpsertSetting 'EventProcessor', 'FraudEngine:Rules:4:Enabled',       'true',             NULL;

-- =============================================================================
-- Cleanup
-- =============================================================================

DROP PROCEDURE #UpsertSetting;
GO

PRINT 'EventProcessor settings seeded successfully.';
GO
