-- Day 9 - Piece 1
-- Non-Repeatable Read - Session 1
-- Step 1: First read

SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

BEGIN TRANSACTION;

SELECT Id, Name, Balance
FROM dbo.IsolationLab
WHERE Id = 1;