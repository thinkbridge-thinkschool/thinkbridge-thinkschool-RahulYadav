WITH QuoteCounts AS
(
    SELECT
        Author,
        COUNT(*) AS QuoteCount
    FROM Quotes
    WHERE IsDeleted = 0
    GROUP BY Author
),
RankedQuotes AS
(
    SELECT
        Author,
        Text,
        ROW_NUMBER() OVER
        (
            PARTITION BY Author
            ORDER BY Id DESC
        ) AS RowNum
    FROM Quotes
    WHERE IsDeleted = 0
)
SELECT
    qc.Author,
    qc.QuoteCount,
    rq.Text AS MostRecentQuote
FROM QuoteCounts AS qc
INNER JOIN RankedQuotes AS rq
    ON qc.Author = rq.Author
WHERE rq.RowNum = 1
ORDER BY qc.QuoteCount DESC, qc.Author
LIMIT 10;