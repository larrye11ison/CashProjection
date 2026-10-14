ALTER TABLE Accounts ADD COLUMN SupportsProjections INTEGER NOT NULL DEFAULT 0;
UPDATE Accounts SET SupportsProjections = 1 WHERE IsActive = 1;
