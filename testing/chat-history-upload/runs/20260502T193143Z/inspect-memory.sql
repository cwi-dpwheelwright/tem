select 'source_memory_count', count(*) from source_memory where user_id = 'bad199b6-4d63-4ae3-bcc1-95b1c5a06332';
select 'source_chunk_count', count(*) from source_chunks where user_id = 'bad199b6-4d63-4ae3-bcc1-95b1c5a06332';
select 'governed_memory_count', count(*) from governed_memory where user_id = 'bad199b6-4d63-4ae3-bcc1-95b1c5a06332';
select 'governed_memory_scope', memory_scope, count(*) from governed_memory where user_id = 'bad199b6-4d63-4ae3-bcc1-95b1c5a06332' group by memory_scope;
select 'sample_source', source_id, source_type, left(filename, 80), created_utc from source_memory where user_id = 'bad199b6-4d63-4ae3-bcc1-95b1c5a06332' order by created_utc desc limit 5;
select 'sample_memory', memory_scope, left(memory_type, 100), left(memory_text, 180), provenance_id from governed_memory where user_id = 'bad199b6-4d63-4ae3-bcc1-95b1c5a06332' order by updated_utc desc limit 10;
