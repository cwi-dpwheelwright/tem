select 'source_memory_count', count(*) from source_memory where user_id = 'ea87fbea-e06a-44b3-b3c5-b99297a31d6f';
select 'source_chunk_count', count(*) from source_chunks where user_id = 'ea87fbea-e06a-44b3-b3c5-b99297a31d6f';
select 'governed_memory_count', count(*) from governed_memory where user_id = 'ea87fbea-e06a-44b3-b3c5-b99297a31d6f';
select 'governed_memory_scope', memory_scope, count(*) from governed_memory where user_id = 'ea87fbea-e06a-44b3-b3c5-b99297a31d6f' group by memory_scope;
