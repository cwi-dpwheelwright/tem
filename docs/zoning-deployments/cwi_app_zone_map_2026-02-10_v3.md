# CWI App → Zone Map (v3) — Dual Teacher-Node Confirmed

**Update applied:** You confirmed there will be **two teacher-node instances**:
- **teacher-node (internal publisher/eval)** → `internal-02`
- **teacher-node (client-site validator/eval)** → `client-restricted`

Zones used:
- **public**: Internet-exposed ingress/reverse-proxy host and public-facing surfaces (gateway + public UIs; upstream services remain internal)
- **internal-01**: Core control-plane/services (routing, orchestration, identity, bots, pack serving)
- **internal-02**: Data/evidence/compute/tooling layers (datastores, vector/graph, model workers, teacher tooling)
- **client-restricted**: Runs only on the client/customer node

---

## App list (current environment inventory) → Zone

| App / Container | Zone |
|---|---|
| public-core-gateway | public |
| internal-01-core-router | internal-01 |
| internal-01-core-orchestrator | internal-01 |
| internal-01-identity-server | internal-01 |
| internal-01-identity-admin | internal-01 |
| public-ui-glassbox-web-ui | public |
| public-ui-gpt-web-ui | public |
| internal-01-ui-admin-console | internal-01 |
| internal-01-bot-teams | internal-01 |
| internal-01-core-pack-service | internal-01 |
| internal-01-model-worker-health | internal-01 |
| internal-01-test-mockserver | internal-01 |
| internal-02-data-redis | internal-02 |
| internal-02-data-postgres | internal-02 |
| internal-02-identity-postgres | internal-02 |
| internal-02-data-vector-shard | internal-02 |
| internal-02-data-graph-db | internal-02 |
| internal-02-teacher-node | internal-02 |
| client-restricted-teacher-node | client-restricted |

---

## Operational guardrail (important)
- Only **one** teacher-node should be the **publisher** to the canonical packs location.
- The **client** teacher-node should default to **read-only validation/eval** to avoid pack drift.
