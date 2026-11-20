select 'ownership_transfer_requests_by_status' as section, "Status", count(*) from ownership_transfer_requests group by "Status" order by count(*) desc;
select 'succession_plans_by_status' as section, "Status", count(*) from succession_plans group by "Status" order by count(*) desc;
select 'succession_snapshots_count' as section, count(*)::text, '' from succession_snapshots;