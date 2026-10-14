CREATE TABLE IF NOT EXISTS Matches (
    Id                INTEGER PRIMARY KEY AUTOINCREMENT,
    ProjectionItemId  INTEGER NOT NULL REFERENCES ProjectionItems(Id),
    BankTransactionId INTEGER NOT NULL REFERENCES BankTransactions(Id),
    Status            TEXT NOT NULL,
    Score             INTEGER NOT NULL,
    CreatedAtUtc      TEXT NOT NULL,
    DecidedAtUtc      TEXT NULL,
    UNIQUE(ProjectionItemId, BankTransactionId)
);

CREATE TABLE IF NOT EXISTS ChangeHistory (
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    EntityType   TEXT NOT NULL,
    EntityId     INTEGER NOT NULL,
    ChangeType   TEXT NOT NULL,
    BeforeJson   TEXT NULL,
    AfterJson    TEXT NULL,
    Description  TEXT NOT NULL,
    TimestampUtc TEXT NOT NULL
);
