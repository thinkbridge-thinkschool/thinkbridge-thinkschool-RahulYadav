-- Day 9 - Piece 1
-- Phantom Read - Session 2
-- Step 2: Insert a new matching row

INSERT INTO dbo.IsolationLab
    (Id, Name, Balance, Category)
VALUES
    (4, 'David', 4000, 'A');
