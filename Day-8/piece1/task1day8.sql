-- Day 8 Piece 1: Index Performance Lab
-- Table: dbo.IndexLab
-- Approximately 100,000 rows generated before running these tests.

SET STATISTICS IO ON;

-- ============================================
-- CLUSTERED INDEX
-- ============================================

-- Before clustered index:
-- Logical reads: 1147
SELECT Id
FROM dbo.IndexLab
WHERE Id BETWEEN 50000 AND 50100;

CREATE CLUSTERED INDEX IX_IndexLab_Id
ON dbo.IndexLab(Id);

-- After clustered index:
-- Logical reads: 5
SELECT Id
FROM dbo.IndexLab
WHERE Id BETWEEN 50000 AND 50100;


-- ============================================
-- NON-CLUSTERED INDEX: Author
-- ============================================

-- Before Author index:
-- Logical reads: 1002
SELECT *
FROM dbo.IndexLab
WHERE Author = 'Author500';

CREATE NONCLUSTERED INDEX IX_IndexLab_Author
ON dbo.IndexLab(Author);

-- Index-only query after Author index:
-- Logical reads: 2
SELECT Author
FROM dbo.IndexLab
WHERE Author = 'Author500';


-- ============================================
-- NON-CLUSTERED INDEX: Category
-- ============================================

-- Before Category index:
-- Logical reads: 1002
SELECT Category
FROM dbo.IndexLab
WHERE Category = 'classic';

CREATE NONCLUSTERED INDEX IX_IndexLab_Category
ON dbo.IndexLab(Category);

-- Index-only query after Category index:
-- Logical reads: 2
SELECT Category
FROM dbo.IndexLab
WHERE Category = 'classic';


-- ============================================
-- WRITE-SIDE COST TEST
-- ============================================

INSERT INTO dbo.IndexLab
(
    Id,
    Author,
    Category,
    QuoteText,
    CreatedAt
)
VALUES
(
    200001,
    'WriteTest',
    'modern',
    'Testing write overhead from indexes',
    GETDATE()
);

-- Observed insert: 13 logical reads.

DELETE FROM dbo.IndexLab
WHERE Id = 200001;

-- Observed cleanup delete: 7 logical reads.


-- ============================================
-- FINAL INDEX LIST
-- ============================================

SELECT
    name AS IndexName,
    type_desc AS IndexType
FROM sys.indexes
WHERE object_id = OBJECT_ID('dbo.IndexLab')
  AND index_id > 0;