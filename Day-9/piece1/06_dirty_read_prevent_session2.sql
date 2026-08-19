-- Day 9 - Piece 1
-- Dirty Read Prevention - Session 2
-- READ COMMITTED

SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

SELECT Id, Name, Balance
FROM dbo.IsolationLab
WHERE Id = 1;