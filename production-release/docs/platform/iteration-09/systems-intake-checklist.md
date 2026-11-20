# Systems URL/IP Intake (Production Release)

Use this short checklist to collect only the URLs and IPs that must be configured so the apps run correctly on the node/server. Fill in each field or mark `TBD`.

**Public Entry Points**
1. Primary platform base URL (DNS):
2. Load balancer hostname or public IP:
3. Public API base URL (gateway):
4. Public web UI URL (node UI):
5. Public identity admin UI URL:
6. Health check URL(s) exposed externally:

**Internal Service Endpoints**
1. Router base URL:
2. Orchestrator base URL:
3. Identity server base URL:
4. Pack service base URL:
5. Vector shard URL(s):
6. Graph DB endpoint (host:port):
7. Teams bot service URL:
8. Connectors/ingest base URL (if used by agents):

**Auth / OIDC URLs**
1. OIDC issuer / authority URL:
2. JWKS URL (if different from issuer):
3. Redirect URIs to allowlist (web + admin + mobile):
4. Post-logout redirect URIs:

**Data & Storage Endpoints**
1. Primary database host and port:
2. Object storage endpoint (S3/MinIO) URL:
3. Any external service URLs that must be allowlisted:

**Observability Endpoints (if configured in runtime)**
1. Log collection endpoint URL:
2. Metrics/OTel collector endpoint URL:
3. Tracing endpoint URL:

**Network Allowlisting (URLs/IPs)**
1. Egress IP(s) from the node/server:
2. Inbound allowlist requirements (source IPs):
3. Any proxy URL (if outbound traffic must pass through a proxy):

**Notes**
1. Anything else in the environment that requires URL or IP updates:
2260:  /api/admin/kill_switch:
2371:  /api/admin/kill_switch/token:
2260:  /api/admin/kill_switch:
2371:  /api/admin/kill_switch/token:
