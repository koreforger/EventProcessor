-- EventProcessor SQL Schema Initialization
-- Creates all required tables for the FraudScoringEngine
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

-- =============================================================================
-- Database Creation
-- =============================================================================

IF DB_ID('FraudEngine') IS NULL
BEGIN
    CREATE DATABASE FraudEngine;
END
GO

USE FraudEngine;
GO

-- =============================================================================
-- Settings Tables (KoreForge.Settings)
-- =============================================================================

IF OBJECT_ID('dbo.Settings', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Settings
    (
        ID               BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ApplicationId    NVARCHAR(200)  NULL,
        InstanceId       NVARCHAR(200)  NULL,
        [Key]            NVARCHAR(850)  NOT NULL,
        [Value]          NVARCHAR(MAX)  NULL,
        BinaryValue      VARBINARY(MAX) NULL,
        IsSecret         BIT            NOT NULL DEFAULT(0),
        ValueEncrypted   BIT            NOT NULL DEFAULT(0),
        CreatedBy        NVARCHAR(50)   NOT NULL,
        CreatedDate      DATETIME2(3)   NOT NULL,
        ModifiedBy       NVARCHAR(50)   NOT NULL,
        ModifiedDate     DATETIME2(3)   NOT NULL,
        [Comment]        VARCHAR(4000)  NULL,
        [Notes]          VARCHAR(MAX)   NULL,
        RowVersion       ROWVERSION     NOT NULL,
        CONSTRAINT CK_Settings_Value_XOR_Binary CHECK (
            ([Value] IS NOT NULL AND BinaryValue IS NULL) OR
            ([Value] IS NULL AND BinaryValue IS NOT NULL)
        )
    );
END
GO

-- Unique indexes for settings
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_Settings_Global_Key')
    CREATE UNIQUE INDEX UX_Settings_Global_Key ON dbo.Settings([Key]) WHERE ApplicationId IS NULL AND InstanceId IS NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_Settings_App_Key')
    CREATE UNIQUE INDEX UX_Settings_App_Key ON dbo.Settings(ApplicationId, [Key]) WHERE ApplicationId IS NOT NULL AND InstanceId IS NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_Settings_Instance_Key')
    CREATE UNIQUE INDEX UX_Settings_Instance_Key ON dbo.Settings(ApplicationId, InstanceId, [Key]) WHERE ApplicationId IS NOT NULL AND InstanceId IS NOT NULL;
GO

-- =============================================================================
-- Fraud Session Tables
-- =============================================================================

IF OBJECT_ID('dbo.FraudSessions', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.FraudSessions
    (
        SessionId        BIGINT IDENTITY(1,1) NOT NULL,
        NID              NVARCHAR(100)   NOT NULL,
        Status           TINYINT         NOT NULL DEFAULT(0),  -- 0=Active, 1=Closed, 2=Flagged, 3=Blocked
        CreatedAt        DATETIME2(3)    NOT NULL,
        LastActivityAt   DATETIME2(3)    NOT NULL,
        ClosedAt         DATETIME2(3)    NULL,
        TransactionCount INT             NOT NULL DEFAULT(0),
        TotalAmount      DECIMAL(18,2)   NOT NULL DEFAULT(0),
        CurrentScore     FLOAT           NOT NULL DEFAULT(0),
        FeatureVectorJson NVARCHAR(MAX)  NULL,
        TriggeredRulesJson NVARCHAR(MAX) NULL,
        BucketIndex      INT             NOT NULL,
        ModifiedDate     DATETIME2(3)    NOT NULL,
        RowVersion       ROWVERSION      NOT NULL,

        CONSTRAINT PK_FraudSessions PRIMARY KEY CLUSTERED (SessionId),
        CONSTRAINT UX_FraudSessions_NID UNIQUE (NID)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_FraudSessions_Status_LastActivity')
    CREATE INDEX IX_FraudSessions_Status_LastActivity ON dbo.FraudSessions(Status, LastActivityAt) INCLUDE (NID, CurrentScore);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_FraudSessions_BucketIndex')
    CREATE INDEX IX_FraudSessions_BucketIndex ON dbo.FraudSessions(BucketIndex) INCLUDE (NID, Status);
GO

-- =============================================================================
-- Fraud Decisions Table
-- =============================================================================

IF OBJECT_ID('dbo.FraudDecisions', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.FraudDecisions
    (
        DecisionId       BIGINT IDENTITY(1,1) NOT NULL,
        SessionId        BIGINT          NOT NULL,
        NID              NVARCHAR(100)   NOT NULL,
        Score            FLOAT           NOT NULL,
        DecisionType     TINYINT         NOT NULL,  -- 0=Allow, 1=Flagged, 2=Blocked
        DecidedAt        DATETIME2(3)    NOT NULL,
        TriggeredRules   NVARCHAR(MAX)   NULL,
        TransactionId    NVARCHAR(100)   NULL,
        CreatedDate      DATETIME2(3)    NOT NULL DEFAULT(SYSUTCDATETIME()),

        CONSTRAINT PK_FraudDecisions PRIMARY KEY CLUSTERED (DecisionId),
        CONSTRAINT FK_FraudDecisions_Sessions FOREIGN KEY (SessionId) REFERENCES dbo.FraudSessions(SessionId)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_FraudDecisions_NID')
    CREATE INDEX IX_FraudDecisions_NID ON dbo.FraudDecisions(NID, DecidedAt DESC);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_FraudDecisions_DecidedAt')
    CREATE INDEX IX_FraudDecisions_DecidedAt ON dbo.FraudDecisions(DecidedAt DESC) INCLUDE (NID, Score, DecisionType);
GO

-- =============================================================================
-- Transaction History (Recent transactions for each session)
-- =============================================================================

IF OBJECT_ID('dbo.TransactionHistory', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TransactionHistory
    (
        HistoryId        BIGINT IDENTITY(1,1) NOT NULL,
        SessionId        BIGINT          NOT NULL,
        NID              NVARCHAR(100)   NOT NULL,
        TransactionId    NVARCHAR(100)   NOT NULL,
        Amount           DECIMAL(18,2)   NOT NULL,
        TransactionType  NVARCHAR(50)    NULL,
        Timestamp        DATETIME2(3)    NOT NULL,
        RawMessageJson   NVARCHAR(MAX)   NULL,
        CreatedDate      DATETIME2(3)    NOT NULL DEFAULT(SYSUTCDATETIME()),

        CONSTRAINT PK_TransactionHistory PRIMARY KEY CLUSTERED (HistoryId),
        CONSTRAINT FK_TransactionHistory_Sessions FOREIGN KEY (SessionId) REFERENCES dbo.FraudSessions(SessionId)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_TransactionHistory_SessionId')
    CREATE INDEX IX_TransactionHistory_SessionId ON dbo.TransactionHistory(SessionId, Timestamp DESC);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_TransactionHistory_NID')
    CREATE INDEX IX_TransactionHistory_NID ON dbo.TransactionHistory(NID, Timestamp DESC);
GO

-- =============================================================================
-- Alerts Table
-- =============================================================================

IF OBJECT_ID('dbo.FraudAlerts', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.FraudAlerts
    (
        AlertId          BIGINT IDENTITY(1,1) NOT NULL,
        NID              NVARCHAR(100)   NOT NULL,
        SessionId        BIGINT          NULL,
        AlertType        NVARCHAR(50)    NOT NULL,
        Severity         TINYINT         NOT NULL,  -- 0=Low, 1=Medium, 2=High, 3=Critical
        Score            FLOAT           NOT NULL,
        TriggeredRules   NVARCHAR(MAX)   NULL,
        AlertedAt        DATETIME2(3)    NOT NULL,
        AcknowledgedAt   DATETIME2(3)    NULL,
        AcknowledgedBy   NVARCHAR(100)   NULL,

        CONSTRAINT PK_FraudAlerts PRIMARY KEY CLUSTERED (AlertId)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_FraudAlerts_NID')
    CREATE INDEX IX_FraudAlerts_NID ON dbo.FraudAlerts(NID, AlertedAt DESC);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_FraudAlerts_Severity')
    CREATE INDEX IX_FraudAlerts_Severity ON dbo.FraudAlerts(Severity, AlertedAt DESC) WHERE AcknowledgedAt IS NULL;
GO

PRINT 'FraudEngine database schema created successfully.';
GO
