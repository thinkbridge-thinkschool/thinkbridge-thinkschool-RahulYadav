-- Day 9 - Piece 1
-- Non-Repeatable Read - Session 1
-- Step 3: Second read

SELECT Id, Name, Balance
FROM dbo.IsolationLab
WHERE Id = 1;

COMMIT TRANSACTION;