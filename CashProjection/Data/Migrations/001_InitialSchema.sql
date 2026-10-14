CREATE TABLE IF NOT EXISTS SchemaVersion (
    Version INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS Accounts (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    Name                TEXT NOT NULL,
    IsActive            INTEGER NOT NULL DEFAULT 0,
    PlaidItemId         TEXT NULL,
    PlaidAccountId      TEXT NULL,
    StartingBalance     TEXT NOT NULL DEFAULT '0',
    StartingBalanceAsOf TEXT NULL
);

CREATE TABLE IF NOT EXISTS ProjectionItems (
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    AccountId    INTEGER NOT NULL REFERENCES Accounts(Id),
    Name         TEXT NOT NULL,
    ItemDate     TEXT NOT NULL,
    Deposit      TEXT NULL,
    Payment      TEXT NULL,
    Periodicity  INTEGER NOT NULL,
    MatchText    TEXT NULL,
    NeedsReview  INTEGER NOT NULL DEFAULT 0,
    UpdatedAtUtc TEXT NOT NULL
);
