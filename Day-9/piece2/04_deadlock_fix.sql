-- Day 9 - Piece 2
-- Deadlock Fix - Consistent Lock Ordering
-- Both sessions access Id 1 first, then Id 2.

-- SESSION 1
BEGIN TRANSACTION;

UPDATE dbo.DeadlockLab
SET Balance = Balance + 100
WHERE Id = 1;

UPDATE dbo.DeadlockLab
SET Balance = Balance + 100
WHERE Id = 2;

COMMIT TRANSACTION;


-- SESSION 2
BEGIN TRANSACTION;

UPDATE dbo.DeadlockLab
SET Balance = Balance + 200
WHERE Id = 1;

UPDATE dbo.DeadlockLab
SET Balance = Balance + 200
WHERE Id = 2;

COMMIT TRANSACTION;


-- Final verification
SELECT *
FROM dbo.DeadlockLab
ORDER BY Id;