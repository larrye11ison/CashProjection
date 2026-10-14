CREATE TABLE IF NOT EXISTS AppSettings (
    Key   TEXT PRIMARY KEY,
    Value TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS BankTransactions (
    Id                 INTEGER PRIMARY KEY AUTOINCREMENT,
    AccountId          INTEGER NOT NULL REFERENCES Accounts(Id),
    PlaidTransactionId TEXT NOT NULL UNIQUE,
    TxnDate            TEXT NOT NULL,
    Description        TEXT NOT NULL,
    Amount             TEXT NOT NULL,
    Pending            INTEGER NOT NULL DEFAULT 0,
    Category           TEXT NULL,
    ImportedAtUtc      TEXT NOT NULL
);
