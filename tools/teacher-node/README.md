# Teacher Node Toolchain

CLI utilities for pack validation, anchor generation, publishing, rollback, artifact storage, and report generation.

## Setup
Graph tooling depends on the repo-native `cwi-etl` package, which requires Python 3.10+.

```
python -m pip install -r tools/teacher-node/requirements.txt
python -m pip install src/etl
```

## Validate a pack
```
python tools/teacher-node/teacher_node.py validate --pack OPS/cwi-ops/compose/packs/enterprise/pack.json
```
Schema source: `contracts/json-schema/pack_schema.json`.

## Generate anchor
```
python tools/teacher-node/teacher_node.py anchor --pack OPS/cwi-ops/compose/packs/enterprise/pack.json
```

## Dependency graph
```
python tools/teacher-node/teacher_node.py graph --pack OPS/cwi-ops/compose/packs/enterprise/pack.json
```

## Publish
```
python tools/teacher-node/teacher_node.py publish --pack-dir OPS/cwi-ops/compose/packs/enterprise --out-dir artifacts/packs
```
Optional transport:
- `--transport copy` (default)
- `--transport s3` (uses MinIO/S3 config)
- `--transport scp` (requires `scp` available)

To also write the MinIO published layout (`latest.txt` + `<hash>/pack.json`) for core mirroring:
```
TEACHER_NODE_PUBLISHED_BUCKET=cwi-packs-published \
python tools/teacher-node/teacher_node.py publish \
  --pack-dir OPS/cwi-ops/compose/packs/enterprise \
  --out-dir artifacts/packs \
  --transport s3 \
  --published-layout
```

To publish a report bundle alongside pack metadata:
```
python tools/teacher-node/teacher_node.py publish \
  --pack-dir OPS/cwi-ops/compose/packs/enterprise \
  --out-dir artifacts/packs \
  --report-bundle artifacts/teacher-node/reports/enterprise/run/report-bundle.zip \
  --run-id run-20260201
```

## Rollback
```
python tools/teacher-node/teacher_node.py rollback --registry-dir artifacts/packs --pack-id enterprise --version 0.0.1
```

## Eval setup
```
python tools/teacher-node/teacher_node.py eval-setup --vector-fixture path/to/vector_corpus.jsonl
```

## Report generation
```
python tools/teacher-node/teacher_node.py report-generate \
  --input contracts/fixtures/teacher-node/teacher_report_input.sample.json \
  --bundle
```

## Template validation
```
python tools/teacher-node/teacher_node.py template-validate \
  --template contracts/fixtures/teacher-node/teacher_template.sample.json
```
Schema source: `contracts/json-schema/teacher_template_schema.json`.

## Document generation + versioned store
```
python tools/teacher-node/teacher_node.py document-generate \
  --template contracts/fixtures/teacher-node/teacher_template.sample.json \
  --input tools/teacher-node/tests/fixtures/document_input.sample.json \
  --store-dir artifacts/teacher-node/document-store \
  --bundle \
  --request-approval
```

Rollback a document version:
```
python tools/teacher-node/teacher_node.py document-rollback \
  --store-dir artifacts/teacher-node/document-store \
  --document-id doc-123 \
  --version 20260202123000
```

## Reconciliation report generation
```
python tools/teacher-node/teacher_node.py reconciliation-generate \
  --input tools/teacher-node/tests/fixtures/reconciliation_input.sample.json \
  --bundle
```

## Simulation runs
```
python tools/teacher-node/teacher_node.py simulation-run --out artifacts/teacher-node/simulations.json
```

## Teacher Critic
The Teacher Critic validates Teacher-owned artifacts without mutating runtime
state. It checks pack anchor integrity, Teacher artifact JSON/hash references,
and vector or Qdrant export consistency against validated pack anchors.

```
python tools/teacher-node/teacher_node.py critic-run \
  --pack-root OPS/cwi-ops/compose/packs \
  --artifact-root artifacts/teacher-node \
  --vector-root artifacts/vector \
  --out artifacts/teacher-node/critic/latest.json \
  --validation-marker artifacts/teacher-node/last-successful-validation.json
```

The command exits nonzero when a failure is found. Warnings remain visible in
the structured report but do not produce a failing exit code.

## Artifact store (filesystem/MinIO)
```
python tools/teacher-node/teacher_node.py artifact-upload --source report.json --key reports/demo/report.json
python tools/teacher-node/teacher_node.py artifact-download --key reports/demo/report.json --dest /tmp/report.json
python tools/teacher-node/teacher_node.py artifact-prune --prefix reports/ --older-than-days 30
```

## Graph tooling CLI
The teacher-side graph preparation flow is exposed as thin CLI wrappers over repo-native `cwi_etl` modules.

Inspect ontology metadata:
```
python tools/teacher-node/graph_cli.py ontology
python tools/teacher-node/cwi_graph_ontology.py --entity-type equipment --name "Extruder 6"
```

Generate per-document entities and relationships:
```
python tools/teacher-node/cwi_entity_extractor.py --input sample.txt --out artifacts/graph/sample.entities.json --pack-target Operations-Pack
python tools/teacher-node/cwi_relationship_extractor.py --input sample.txt --entities artifacts/graph/sample.entities.json --out artifacts/graph/sample.relationships.json --pack-target Operations-Pack
```

Build rollups, curated rollups, cross-pack edges, and AGE artifacts:
```
python tools/teacher-node/cwi_graph_rollup.py --pack-target Operations-Pack --entities artifacts/graph/sample.entities.json --relationships artifacts/graph/sample.relationships.json --out artifacts/graph/operations.graph.rollup.json
python tools/teacher-node/cwi_graph_curate.py --input artifacts/graph/operations.graph.rollup.json --out artifacts/graph/operations.graph.curated.json
python tools/teacher-node/cwi_graph_crosswalk.py --inputs artifacts/graph/operations.graph.curated.json artifacts/graph/maintenance.graph.curated.json --out artifacts/graph/cwi_crosspack_edges.json
python tools/teacher-node/cwi_age_graph_writer.py --rollups artifacts/graph/operations.graph.curated.json --crosswalk artifacts/graph/cwi_crosspack_edges.json --out-sql artifacts/graph/cwi_graph.curated.sql --out-summary artifacts/graph/cwi_graph.curated.summary.json
```

Backfill existing extracted or normalized content:
```
python tools/teacher-node/cwi_graph_backfill.py --inputs artifacts/documents --out-dir artifacts/graph/backfill --pack-target Operations-Pack --summary artifacts/graph/backfill/summary.json
```

Env vars (artifact store):
- `TEACHER_NODE_ARTIFACT_BACKEND` (filesystem|minio)
- `TEACHER_NODE_ARTIFACT_ROOT` (filesystem root path)
- `MINIO_ENDPOINT`, `MINIO_ACCESS_KEY`, `MINIO_SECRET_KEY`, `MINIO_BUCKET`, `MINIO_SECURE`

Optional escalation/provenance:
- `ORCHESTRATOR_URL`
- `TEACHER_NODE_PROVENANCE_ENDPOINT` (POST for pack/report events)
- `TEACHER_NODE_PROVENANCE_TOKEN` (Bearer token override, falls back to `CWI_AUTH_TOKEN`)
- `TEACHER_NODE_APPROVAL_ENDPOINT` (POST for coherence escalation requests)
- `TEACHER_NODE_APPROVAL_TOKEN` (Bearer token override, falls back to `CWI_AUTH_TOKEN`)
- `TEACHER_NODE_ARTIFACT_REGISTRY_ENDPOINT` (POST to register artifacts, default `/api/artifacts/register`)
- `TEACHER_NODE_ARTIFACT_REGISTRY_TOKEN` (Bearer token override, falls back to `CWI_AUTH_TOKEN`)
- `TEACHER_NODE_TENANT_ID` (fallback tenant id for artifact registration)

## CI helpers
```
./OPS/cwi-ops/ci/run-teacher-node-checks.ps1
./OPS/cwi-ops/ci/run-teacher-node-eval.ps1 -BaseUrl http://localhost:18081 -Suite integration
```
