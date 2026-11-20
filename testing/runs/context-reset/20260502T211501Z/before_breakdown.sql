select 'source_memory_by_type' as section, source_type, count(*) from source_memory group by source_type order by count(*) desc;
select 'governed_memory_by_scope' as section, memory_scope, count(*) from governed_memory group by memory_scope order by count(*) desc;
select 'ask_history_distinct_users' as section, count(distinct tenant_id)::text as tenant_count, count(distinct user_id)::text as user_count from ask_history;
select 'source_memory_distinct_users' as section, count(distinct tenant_id)::text as tenant_count, count(distinct user_id)::text as user_count from source_memory;