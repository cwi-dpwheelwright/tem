select source_type, regexp_replace(storage_location, '[^/\\]+$', '<file>') as storage_bucket, count(*)
from source_memory
group by source_type, storage_bucket
order by count(*) desc
limit 20;