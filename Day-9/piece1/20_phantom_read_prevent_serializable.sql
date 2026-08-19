-- ============================================================
-- Day 9 - Piece 1
-- Phantom Read Prevention using SERIALIZABLE
-- SQL Server
-- ============================================================


-- ============================================================
-- SESSION 1 - STEP 1
-- Run this part in Session 1
-- ============================================================

SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

BEGIN TRANSACTION;

SELECT Id, Name, Balance, Category
FROM dbo.IsolationLab
WHERE Category = 'A';


-- STOP HERE
-- Leave the transaction open.
-- Do NOT COMMIT or ROLLBACK yet.


-- ============================================================
-- SESSION 2 - STEP 2
-- Run this part in Session 2
-- ============================================================

INSERT INTO dbo.IsolationLab
    (Id, Name, Balance, Category)
VALUES
    (5, 'Eve', 5000, 'A');

COMMIT TRANSACTION;


-- ============================================================
-- SESSION 1 - STEP 3
-- Return to Session 1 and run this
-- ============================================================

SELECT Id, Name, Balance, Category
FROM dbo.IsolationLab
WHERE Category = 'A';


-- Expected:
-- The second SELECT should show the same rows
-- as the first SELECT.
--
-- Eve should NOT appear until Session 1 commits.


-- ============================================================
-- SESSION 1 - STEP 4
-- Commit the transaction
-- ============================================================

COMMIT TRANSACTION;


-- ============================================================
-- FINAL CHECK
-- ============================================================

SELECT Id, Name, Balance, Category
FROM dbo.IsolationLab
WHERE Category = 'A';