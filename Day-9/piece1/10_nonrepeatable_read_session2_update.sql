-- Day 9 - Piece 1
-- Non-Repeatable Read - Session 2
-- Step 2: Update and commit the row

UPDATE dbo.IsolationLab
SET Balance = 1500
WHERE Id = 1;

COMMIT TRANSACTION;