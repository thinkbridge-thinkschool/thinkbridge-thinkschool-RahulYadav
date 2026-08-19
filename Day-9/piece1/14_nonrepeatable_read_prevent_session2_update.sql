-- Day 9 - Piece 1
-- Non-Repeatable Read Prevention
-- Session 2 - Attempted Update

UPDATE dbo.IsolationLab
SET Balance = 2000
WHERE Id = 1;

COMMIT TRANSACTION;