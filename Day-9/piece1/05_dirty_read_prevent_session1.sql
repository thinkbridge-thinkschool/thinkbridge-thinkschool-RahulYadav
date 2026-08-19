-- Day 9 - Piece 1
-- Dirty Read Prevention - Session 1
-- READ COMMITTED

SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

BEGIN TRANSACTION;

UPDATE dbo.IsolationLab
SET Balance = 500
WHERE Id = 1;

SELECT Id, Name, Balance
FROM dbo.IsolationLab
WHERE Id = 1;