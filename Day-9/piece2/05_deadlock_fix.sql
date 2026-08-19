BEGIN TRANSACTION;

UPDATE dbo.DeadlockLab
SET Balance = Balance + 200
WHERE Id = 1;

UPDATE dbo.DeadlockLab
SET Balance = Balance + 200
WHERE Id = 2;

COMMIT TRANSACTION;