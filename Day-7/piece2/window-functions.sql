SELECT
    Author,
    Id,
    Text,
    CreatedAt,

    COUNT(*) OVER (
        PARTITION BY Author
        ORDER BY CreatedAt
    ) AS RunningCount,

    LAG(CreatedAt) OVER (
        PARTITION BY Author
        ORDER BY CreatedAt
    ) AS PreviousQuoteDate,

    CAST(
        julianday(CreatedAt) -
        julianday(
            LAG(CreatedAt) OVER (
                PARTITION BY Author
                ORDER BY CreatedAt
            )
        )
        AS INTEGER
    ) AS GapInDays

FROM QuoteWindowExercise
ORDER BY Author, CreatedAt;