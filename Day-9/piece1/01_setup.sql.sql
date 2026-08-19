-- ============================================================
-- Day 9 - Piece 1
-- Transaction Isolation Levels & Read Anomalies
-- SQL Server
-- ============================================================

IF OBJECT_ID('dbo.IsolationLab', 'U') IS NOT NULL
    DROP TABLE dbo.IsolationLab;

CREATE TABLE dbo.IsolationLab
(
    Id INT PRIMARY KEY,
    Name VARCHAR(50),
    Balance INT,
    Category VARCHAR(50)
);

INSERT INTO dbo.IsolationLab (Id, Name, Balance, Category)
VALUES
(1, 'Alice', 1000, 'A'),
(2, 'Bob',   2000, 'A'),
(3, 'Charlie', 3000, 'B');

SELECT *
FROM dbo.IsolationLab;