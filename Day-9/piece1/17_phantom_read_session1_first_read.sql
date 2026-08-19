-- Day 9 - Piece 1
-- Phantom Read - Session 1
-- Step 1: First range query
-- REPEATABLE READ

SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;

BEGIN TRANSACTION;

SELECT Id, Name, Balance, Category
FROM dbo.IsolationLab
WHERE Category = 'A';