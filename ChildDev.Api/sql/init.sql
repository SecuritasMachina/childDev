CREATE TABLE IF NOT EXISTS Account (
    Guid        CHAR(36) PRIMARY KEY,
    NickName    VARCHAR(100) NOT NULL,
    PinHash     VARCHAR(100) NOT NULL,
    CreatedOn   BIGINT NOT NULL,
    UNIQUE INDEX idx_account_nickname (NickName)
);

CREATE TABLE IF NOT EXISTS Journal (
    Guid        CHAR(36) PRIMARY KEY,
    AccountFk   CHAR(36) NOT NULL,
    Notes       TEXT,
    Activity    VARCHAR(255),
    Mood        VARCHAR(50),
    Tags        VARCHAR(500),
    EnteredDate BIGINT NOT NULL,
    UpdatedOn   BIGINT NOT NULL,
    DeletedAt   BIGINT,
    INDEX idx_journal_account_updated (AccountFk, UpdatedOn)
);

CREATE TABLE IF NOT EXISTS Goal (
    Guid               CHAR(36) PRIMARY KEY,
    AccountFk          CHAR(36) NOT NULL,
    GoalText           TEXT,
    NextMeetingDate    BIGINT,
    ExpirationDate     BIGINT,
    EnteredDate        BIGINT NOT NULL,
    MeasurableOutcome  TEXT,
    CompletionDate     BIGINT,
    UpdatedOn          BIGINT NOT NULL,
    DeletedAt          BIGINT,
    INDEX idx_goal_account_updated (AccountFk, UpdatedOn)
);

CREATE TABLE IF NOT EXISTS GoalProgress (
    Guid             CHAR(36) PRIMARY KEY,
    AccountFk        CHAR(36) NOT NULL,
    GoalFk           CHAR(36) NOT NULL,
    NextStepItems    TEXT,
    NextMeetingDate  BIGINT,
    UpdatedOn        BIGINT NOT NULL,
    DeletedAt        BIGINT,
    INDEX idx_goalprogress_account_updated (AccountFk, UpdatedOn)
);

CREATE TABLE IF NOT EXISTS Todo (
    Guid        CHAR(36) PRIMARY KEY,
    AccountFk   CHAR(36) NOT NULL,
    Title       VARCHAR(500),
    Notes       TEXT,
    DueDate     BIGINT,
    CompletedAt BIGINT,
    UpdatedOn   BIGINT NOT NULL,
    DeletedAt   BIGINT,
    INDEX idx_todo_account_updated (AccountFk, UpdatedOn)
);
