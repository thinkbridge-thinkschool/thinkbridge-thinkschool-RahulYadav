-- Day 8 Piece 2: Covering Index with INCLUDE
-- Table: dbo.IndexLab
-- Rows: ~100,000

SET STATISTICS IO ON;

-- ============================================
-- BEFORE: Query that requires a Key Lookup
-- Expected plan: Index Seek + Key Lookup
-- Logical reads before: 502
-- ============================================

SELECT
    Author,
    Category,
    QuoteText,
    CreatedAt
FROM dbo.IndexLab
WHERE Author = 'Author500';


-- ============================================
-- COVERING INDEX
-- INCLUDE columns supply all columns needed
-- by the query, eliminating the Key Lookup.
-- ============================================

CREATE NONCLUSTERED INDEX IX_IndexLab_Author_Covering
ON dbo.IndexLab(Author)
INCLUDE (Category, QuoteText, CreatedAt);


-- ============================================
-- AFTER: Same query using the covering index
-- Expected plan: Index Seek, no Key Lookup
-- Logical reads after: 5
-- ============================================

SELECT
    Author,
    Category,
    QuoteText,
    CreatedAt
FROM dbo.IndexLab WITH (INDEX = IX_IndexLab_Author_Covering)
WHERE Author = 'Author500';


-- ============================================
-- RESULT
-- Before: 502 logical reads
-- After:    5 logical reads
-- Delta:  497 fewer logical reads
-- ===============================