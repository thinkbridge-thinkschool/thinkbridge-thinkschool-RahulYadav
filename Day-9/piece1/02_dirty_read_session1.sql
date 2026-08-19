-- Day 9 - Piece 1
-- Dirty Read - Session 1
-- Step 1: Create an uncommitted change

SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

BEGIN TRANSACTION;

UPDATE dbo.IsolationLab
SET Balance = 500
WHERE Id = 1;

SELECT Id, Name, Balance
FROM dbo.IsolationLab
WHERE Id = 1;