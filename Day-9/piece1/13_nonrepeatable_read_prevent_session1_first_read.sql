-- Day 9 - Piece 1
-- Non-Repeatable Read Prevention
-- Session 1 - REPEATABLE READ
-- Step 1: First read

SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;

BEGIN TRANSACTION;

SELECT Id, Name, Balance
FROM dbo.IsolationLab
WHERE Id = 1;