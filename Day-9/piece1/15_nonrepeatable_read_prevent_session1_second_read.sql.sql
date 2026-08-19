-- Day 9 - Piece 1
-- Non-Repeatable Read Prevention
-- Session 1 - REPEATABLE READ
-- Step 2: Second read

SELECT Id, Name, Balance
FROM dbo.IsolationLab
WHERE Id = 1;