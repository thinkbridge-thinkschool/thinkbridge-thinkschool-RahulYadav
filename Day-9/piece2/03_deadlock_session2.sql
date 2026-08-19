-- Day 9 - Piece 2
-- Deadlock Reproduction - Session 2

BEGIN TRANSACTION;

UPDATE dbo.DeadlockLab
SET Balance = Balance + 200
WHERE Id = 2;

-- Leave transaction open.
-- Do NOT COMMIT or ROLLBACK.