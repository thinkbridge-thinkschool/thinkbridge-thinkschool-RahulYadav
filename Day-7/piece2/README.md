Day 7 – Piece 1: SQL Window Functions
Objective

Practice SQL window functions by returning each quote per author with:

A running quote count per author
The previous quote date using LAG()
The gap in days since the previous quote
Database

SQLite database: quotes.db

The original Quotes table contains:

Id
Author
Text
IsDeleted

Because the original table did not contain a date column, a separate QuoteWindowExercise table was used with a CreatedAt column for the time-based window-function exercise.

SQL Query
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
Sample Result
Author	Id	Text	CreatedAt	RunningCount	PreviousQuoteDate	GapInDays
Policy Test	1	Testing authorization policy	2026-08-01	1	NULL	NULL
Serilog Test	2	Testing structured logging	2026-08-03	1	NULL	NULL
Serilog Test	3	Testing TraceId logging	2026-08-07	2	2026-08-03	4
Serilog Test	4	Testing correlation logging	2026-08-10	3	2026-08-07	3
Telemetry Test	5	Verifying Azure Monitor UserId log	2026-08-15	1	NULL	NULL
Window Functions Used
COUNT() OVER

Calculates the running number of quotes for each author.

LAG()

Returns the previous quote's date for the same author.

julianday()

Calculates the difference between the current and previous quote dates in days.

PARTITION BY Author

Makes the calculations restart independently for each author.