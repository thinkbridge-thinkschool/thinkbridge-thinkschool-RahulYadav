-- Day 9 - Piece 2
-- Deadlock Lab Setup

IF OBJECT_ID('dbo.DeadlockLab', 'U') IS NOT NULL
    DROP TABLE dbo.DeadlockLab;

CREATE TABLE dbo.DeadlockLab
(
    Id INT PRIMARY KEY,
    Name VARCHAR(50),
    Balance INT
);

INSERT INTO dbo.DeadlockLab (Id, Name, Balance)
VALUES
(1, 'Account-A', 1000),
(2, 'Account-B', 2000);

SELECT *
FROM dbo.DeadlockLab;