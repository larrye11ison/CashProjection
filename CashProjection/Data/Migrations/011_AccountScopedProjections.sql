-- Any account can now have projection items/matching, not just the one
-- SupportsProjections=1 row — so that flag no longer means anything, and
-- Change History/Undo/Redo (previously implicit "the one account with
-- edits") needs its own explicit account scope now that more than one
-- account can have edits.

ALTER TABLE ChangeHistory ADD COLUMN AccountId INTEGER;

-- Backfill: every row recorded before this migration belongs to whichever
-- account was IsActive at the time — accurate for 100% of existing data,
-- since only the IsActive account has ever had projection items/matches to
-- generate history in the first place.
UPDATE ChangeHistory
SET AccountId = (SELECT Id FROM Accounts WHERE IsActive = 1 LIMIT 1)
WHERE AccountId IS NULL;

ALTER TABLE Accounts DROP COLUMN SupportsProjections;
