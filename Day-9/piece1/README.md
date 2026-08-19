# Day 9 — Piece 1: Transaction Isolation Levels & Read Anomalies

## Objective

The objective of this task was to understand SQL Server transaction isolation levels by using two independent SQL sessions to reproduce three common transaction read anomalies:

1. Dirty Read
2. Non-Repeatable Read
3. Phantom Read

The task also demonstrates the lowest isolation level that prevents each anomaly.

### Isolation Levels Covered

- `READ UNCOMMITTED`
- `READ COMMITTED`
- `REPEATABLE READ`
- `SERIALIZABLE`

---

## Environment

| Item | Details |
|---|---|
| Database | Microsoft SQL Server |
| Language | T-SQL |
| Tool | Visual Studio Code |
| Extension | SQL Server |
| Table | `dbo.IsolationLab` |
| Sessions | Two independent SQL Server sessions |

---

# 1. Database Setup

A test table named `dbo.IsolationLab` was created for all experiments.

### `01_setup.sql`

    -- ============================================================
    -- Day 9 - Piece 1
    -- Transaction Isolation Levels & Read Anomalies
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
    (2, 'Bob', 2000, 'A'),
    (3, 'Charlie', 3000, 'B');

    SELECT *
    FROM dbo.IsolationLab;

### Initial Data

| Id | Name | Balance | Category |
|---:|---|---:|---|
| 1 | Alice | 1000 | A |
| 2 | Bob | 2000 | A |
| 3 | Charlie | 3000 | B |

---

# 2. Dirty Read

## Definition

A **dirty read** occurs when one transaction reads data that another transaction has modified but has not committed yet.

The second transaction is therefore reading a value that may later be rolled back.

---

## Session 1 — Create an Uncommitted Change

### `02_dirty_read_session1_begin.sql`

    -- Dirty Read - Session 1

    SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

    BEGIN TRANSACTION;

    UPDATE dbo.IsolationLab
    SET Balance = 500
    WHERE Id = 1;

    SELECT Id, Name, Balance
    FROM dbo.IsolationLab
    WHERE Id = 1;

    -- Leave transaction open.
    -- Do not COMMIT or ROLLBACK yet.

### Session 1 Result

    Id    Name    Balance
    1     Alice   500

At this point:

- Alice's original balance was `1000`.
- Session 1 changed it to `500`.
- The transaction has not been committed.

---

## Session 2 — Read the Uncommitted Value

### `03_dirty_read_session2.sql`

    -- Dirty Read - Session 2

    SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

    SELECT Id, Name, Balance
    FROM dbo.IsolationLab
    WHERE Id = 1;

### Session 2 Result

    Id    Name    Balance
    1     Alice   500

Session 2 was able to read the uncommitted value `500`.

This is a **dirty read**.

---

## Cleanup

The uncommitted transaction was rolled back.

    ROLLBACK TRANSACTION;

The balance returned to:

    Alice | 1000

### Conclusion

A **dirty read was successfully reproduced** using `READ UNCOMMITTED`.

---

# 3. Dirty Read Prevention

## Isolation Level: READ COMMITTED

`READ COMMITTED` prevents a transaction from reading uncommitted data.

### Session 1

    SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

    BEGIN TRANSACTION;

    UPDATE dbo.IsolationLab
    SET Balance = 500
    WHERE Id = 1;

    SELECT Id, Name, Balance
    FROM dbo.IsolationLab
    WHERE Id = 1;

Session 1 has an uncommitted value of `500`.

### Session 2

    SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

    SELECT Id, Name, Balance
    FROM dbo.IsolationLab
    WHERE Id = 1;

### Observed Result

    Id    Name    Balance
    1     Alice   1000

Session 2 did not read the uncommitted `500`.

In this SQL Server environment, `READ_COMMITTED_SNAPSHOT` may allow Session 2 to read the last committed row version instead of waiting.

### Conclusion

`READ COMMITTED` prevents **Dirty Reads**.

---

# 4. Non-Repeatable Read

## Definition

A **non-repeatable read** occurs when a transaction reads the same row twice and gets different values because another transaction modifies and commits that row between the two reads.

---

## Session 1 — First Read

### `09_nonrepeatable_read_session1_first_read.sql`

    -- Non-Repeatable Read - Session 1

    SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

    BEGIN TRANSACTION;

    SELECT Id, Name, Balance
    FROM dbo.IsolationLab
    WHERE Id = 1;

    -- Leave transaction open.

### First Result

    Id    Name    Balance
    1     Alice   1000

---

## Session 2 — Update and Commit

### `10_nonrepeatable_read_session2_update.sql`

    -- Non-Repeatable Read - Session 2

    UPDATE dbo.IsolationLab
    SET Balance = 1500
    WHERE Id = 1;

    COMMIT TRANSACTION;

Session 2 changed Alice's balance from `1000` to `1500` and committed the change.

---

## Session 1 — Second Read

### `11_nonrepeatable_read_session1_second_read.sql`

    -- Non-Repeatable Read - Session 1

    SELECT Id, Name, Balance
    FROM dbo.IsolationLab
    WHERE Id = 1;

    COMMIT TRANSACTION;

### Second Result

    Id    Name    Balance
    1     Alice   1500

### Observation

The same transaction read the same row twice:

    First Read  → 1000
    Second Read → 1500

The value changed between the two reads.

### Conclusion

A **Non-Repeatable Read** occurred under `READ COMMITTED`.

---

# 5. Non-Repeatable Read Prevention

## Isolation Level: REPEATABLE READ

`REPEATABLE READ` prevents changes to rows that have already been read by the current transaction.

---

## Session 1 — First Read

### `13_nonrepeatable_read_prevent_session1_first_read.sql`

    -- Non-Repeatable Read Prevention
    -- Session 1

    SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;

    BEGIN TRANSACTION;

    SELECT Id, Name, Balance
    FROM dbo.IsolationLab
    WHERE Id = 1;

    -- Leave transaction open.

### First Result

The experiment was started with the current committed balance.

    Id    Name    Balance
    1     Alice   2000

---

## Session 2 — Attempt Update

### `14_nonrepeatable_read_prevent_session2_update.sql`

    -- Non-Repeatable Read Prevention
    -- Session 2

    UPDATE dbo.IsolationLab
    SET Balance = 2500
    WHERE Id = 1;

    COMMIT TRANSACTION;

While Session 1's `REPEATABLE READ` transaction was active, Session 2's update was blocked from changing the row visible to Session 1.

---

## Session 1 — Second Read

### `15_nonrepeatable_read_prevent_session1_second_read.sql`

    -- Non-Repeatable Read Prevention
    -- Session 1

    SELECT Id, Name, Balance
    FROM dbo.IsolationLab
    WHERE Id = 1;

### Observed Result

    First Read  → 2000
    Second Read → 2000

The value remained unchanged during Session 1's transaction.

### Conclusion

`REPEATABLE READ` prevents **Non-Repeatable Reads**.

---

## Transaction Cleanup

After completing the second read:

    COMMIT TRANSACTION;

This releases the locks held by Session 1.

---

# 6. Phantom Read

## Definition

A **phantom read** occurs when a transaction executes the same range query twice and the second execution returns a different set of rows because another transaction inserted or deleted rows matching the search condition.

---

## Session 1 — First Range Read

### `20_phantom_read_session1_first_read.sql`

    -- Phantom Read - Session 1

    SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

    BEGIN TRANSACTION;

    SELECT Id, Name, Balance, Category
    FROM dbo.IsolationLab
    WHERE Category = 'A';

    -- Leave transaction open.

### First Result

    Id    Name    Balance    Category
    1     Alice   1000       A
    2     Bob     2000       A

Total rows: **2**

---

## Session 2 — Insert Matching Row

### `21_phantom_read_session2_insert.sql`

    -- Phantom Read - Session 2

    INSERT INTO dbo.IsolationLab
        (Id, Name, Balance, Category)
    VALUES
        (4, 'David', 4000, 'A');

    COMMIT TRANSACTION;

Session 2 inserted a new row where:

    Category = 'A'

---

## Session 1 — Second Range Read

### `22_phantom_read_session1_second_read.sql`

    -- Phantom Read - Session 1

    SELECT Id, Name, Balance, Category
    FROM dbo.IsolationLab
    WHERE Category = 'A';

    COMMIT TRANSACTION;

### Second Result

    Id    Name    Balance    Category
    1     Alice   1000       A
    2     Bob     2000       A
    4     David   4000       A

Total rows: **3**

### Observation

The same query returned:

    First Read  → 2 rows
    Second Read → 3 rows

David is the new row that appeared between the two reads.

### Conclusion

A **Phantom Read** occurred under `READ COMMITTED`.

---

# 7. Phantom Read Prevention

## Isolation Level: SERIALIZABLE

`SERIALIZABLE` is the strongest standard SQL Server isolation level.

It protects not only rows that have been read but also the range of rows matching the query.

---

## Session 1 — First Range Read

### `23_phantom_read_prevent_serializable.sql`

    -- Phantom Read Prevention
    -- Session 1

    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

    BEGIN TRANSACTION;

    SELECT Id, Name, Balance, Category
    FROM dbo.IsolationLab
    WHERE Category = 'A';

    -- Leave transaction open.

Session 1 reads the rows belonging to Category `A`.

---

## Session 2 — Attempt Insert

Run in the second SQL session:

    -- Phantom Read Prevention
    -- Session 2

    INSERT INTO dbo.IsolationLab
        (Id, Name, Balance, Category)
    VALUES
        (5, 'Eve', 5000, 'A');

    COMMIT TRANSACTION;

Because Session 1 is using `SERIALIZABLE`, the insert into the protected range is blocked until Session 1 completes its transaction.

---

## Session 1 — Second Range Read

While Session 2 is waiting, run:

    SELECT Id, Name, Balance, Category
    FROM dbo.IsolationLab
    WHERE Category = 'A';

The second query returns the same rows that were visible during the first query.

The new `Eve` row does not appear while Session 1's transaction is active.

---

## Session 1 — Commit

After completing the second read:

    COMMIT TRANSACTION;

The range protection is released.

Session 2 can now complete its insert.

---

## Final Check

    SELECT Id, Name, Balance, Category
    FROM dbo.IsolationLab
    WHERE Category = 'A';

The newly inserted `Eve` row can now appear in the result.

### Conclusion

`SERIALIZABLE` prevents **Phantom Reads** by protecting the range accessed by the transaction.

---

# 8. Lowest Isolation Level That Prevents Each Anomaly

| Anomaly | Lowest Isolation Level That Prevents It |
|---|---|
| Dirty Read | **READ COMMITTED** |
| Non-Repeatable Read | **REPEATABLE READ** |
| Phantom Read | **SERIALIZABLE** |

---

# 9. Isolation Level Comparison

| Isolation Level | Dirty Read | Non-Repeatable Read | Phantom Read |
|---|---|---|---|
| `READ UNCOMMITTED` | ❌ Possible | ❌ Possible | ❌ Possible |
| `READ COMMITTED` | ✅ Prevented | ❌ Possible | ❌ Possible |
| `REPEATABLE READ` | ✅ Prevented | ✅ Prevented | ❌ Possible |
| `SERIALIZABLE` | ✅ Prevented | ✅ Prevented | ✅ Prevented |

---

# 10. Anomaly Summary

| Anomaly | What Happened | Reproduced Using | Prevented By |
|---|---|---|---|
| Dirty Read | Read uncommitted `500` | `READ UNCOMMITTED` | `READ COMMITTED` |
| Non-Repeatable Read | Same row changed from `1000` to `1500` | `READ COMMITTED` | `REPEATABLE READ` |
| Phantom Read | Matching rows changed from `2` to `3` | `READ COMMITTED` | `SERIALIZABLE` |

---

# 11. Isolation Level Progression

    READ UNCOMMITTED
            ↓
    Dirty Reads possible
            ↓
    READ COMMITTED
            ↓
    Dirty Reads prevented
    Non-Repeatable Reads possible
            ↓
    REPEATABLE READ
            ↓
    Dirty Reads prevented
    Non-Repeatable Reads prevented
    Phantom Reads possible
            ↓
    SERIALIZABLE
            ↓
    Dirty Reads prevented
    Non-Repeatable Reads prevented
    Phantom Reads prevented

---

# 12. Key Learnings

- Transaction isolation levels determine how transactions interact with concurrent transactions.
- `READ UNCOMMITTED` provides the weakest isolation and allows dirty reads.
- `READ COMMITTED` prevents dirty reads but allows non-repeatable reads and phantom reads.
- `REPEATABLE READ` prevents changes to rows already read during a transaction.
- `SERIALIZABLE` protects the entire range involved in a query and prevents phantom reads.
- Higher isolation levels provide stronger consistency but can reduce concurrency because of additional locking and blocking.
- Two independent SQL Server sessions are required to reproduce transaction concurrency anomalies.
- Dirty reads involve uncommitted data.
- Non-repeatable reads involve changes to an existing row.
- Phantom reads involve changes to the set of rows returned by a range query.
- `READ_COMMITTED_SNAPSHOT` can affect the blocking behavior of `READ COMMITTED` in SQL Server while still preventing dirty reads.

---

# 13. Final Results

All three required read anomalies were successfully reproduced:

- ✅ Dirty Read
- ✅ Non-Repeatable Read
- ✅ Phantom Read

The corresponding prevention levels were also demonstrated:

- ✅ `READ COMMITTED` prevents Dirty Reads.
- ✅ `REPEATABLE READ` prevents Non-Repeatable Reads.
- ✅ `SERIALIZABLE` prevents Phantom Reads.

---

# 14. Final Conclusion

This exercise demonstrated how SQL Server isolation levels provide progressively stronger consistency guarantees.

The lowest isolation level required to prevent each anomaly is:

    Dirty Read
        ↓
    READ COMMITTED

    Non-Repeatable Read
        ↓
    REPEATABLE READ

    Phantom Read
        ↓
    SERIALIZABLE

The overall progression is:

    READ UNCOMMITTED
        ↓
    READ COMMITTED
        ↓
    REPEATABLE READ
        ↓
    SERIALIZABLE

As isolation increases, protection against concurrency anomalies increases, but stronger isolation can also reduce concurrency through additional locking or blocking.

---

## Final Status

**Day 9 — Piece 1: COMPLETED**

### Completed Experiments

- [x] Database setup
- [x] Dirty Read reproduction
- [x] Dirty Read prevention using `READ COMMITTED`
- [x] Non-Repeatable Read reproduction
- [x] Non-Repeatable Read prevention using `REPEATABLE READ`
- [x] Phantom Read reproduction
- [x] Phantom Read prevention using `SERIALIZABLE`
- [x] Two-session concurrency testing
- [x] Isolation-level comparison
- [x] Final results documented