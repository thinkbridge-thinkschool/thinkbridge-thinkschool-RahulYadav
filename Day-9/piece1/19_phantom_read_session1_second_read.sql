-- Day 9 - Piece 1
-- Phantom Read - Session 1
-- Step 3: Repeat the range query

SELECT Id, Name, Balance, Category
FROM dbo.IsolationLab
WHERE Category = 'A';