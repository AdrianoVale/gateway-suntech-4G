SELECT table_schema, table_name
FROM information_schema.tables
WHERE table_name IN ('comando','position')
ORDER BY table_schema, table_name;

SELECT table_schema, column_name, data_type, is_nullable, column_default
FROM information_schema.columns
WHERE table_name = 'comando'
ORDER BY ordinal_position;

SELECT table_schema, column_name, data_type, is_nullable, column_default
FROM information_schema.columns
WHERE table_name = 'position'
ORDER BY ordinal_position;
