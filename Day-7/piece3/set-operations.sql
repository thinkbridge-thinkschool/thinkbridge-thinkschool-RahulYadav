-- 1. Authors with quotes but no tags
SELECT DISTINCT Author
FROM Quotes
WHERE IsDeleted = 0

EXCEPT

SELECT DISTINCT Author
FROM QuoteTagsExercise;


-- 2. Authors in both classic and modern
SELECT Author
FROM AuthorCategoriesExercise
WHERE Category = 'classic'

INTERSECT

SELECT Author
FROM AuthorCategoriesExercise
WHERE Category = 'modern';


-- 3. Combined distinct tag list across classic and modern
SELECT Tag
FROM CategoryTagsExercise
WHERE Category = 'classic'

UNION

SELECT Tag
FROM CategoryTagsExercise
WHERE Category = 'modern'

ORDER BY Tag;