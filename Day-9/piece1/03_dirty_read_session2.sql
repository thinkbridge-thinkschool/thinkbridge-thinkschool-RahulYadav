-- Day 9 - Piece 1
-- Dirty Read - Session 2
-- Step 2: Read uncommitted data

SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT Id, Name, Balance
FROM dbo.IsolationLab
WHERE Id = 1;