-- Day 9 - Piece 2
-- Deadlock Reproduction - Session 1

BEGIN TRANSACTION;

UPDATE dbo.DeadlockLab
SET Balance = Balance + 100
WHERE Id = 1;

-- Leave transaction open.
-- Do NOT COMMIT or ROLLBACK.