# CWI Graph Tooling Requirements Request

Date: 2026-03-20
Audience: ChatGPT / requirements clarification
Purpose: Gather the minimum missing requirements needed to implement the teacher-side graph tooling gaps in the current CWI repo.

## Response Instructions

Provide clear, concrete instructions on the apps that were implemented - including purpose, features, and how they fit into the CWI stack. The response needs to be formatted into a specification that Codex can interpret and help algin the direction of implementation.
## Context

The current CWI repo appears to already have the core runtime components needed for graph-backed reasoning:

- `cwi-orchestrator`
- `cwi-gpt-web-ui`
- graph database (`graphdb` / `cwi-graph-db`)
- teacher-side tooling split across `tools/teacher-node` and `src/etl`

However, several teacher-side graph utilities referenced in the handoff do not exist here as explicit implementations, or only exist partially under different modules. The likely tooling gaps are:

- ontology / canonicalization
- entity extraction
- relationship extraction
- historical backfill
- pack rollup
- graph curation
- cross-pack crosswalk generation
- possibly a dedicated AGE writer CLI, although AGE writing functionality already exists in the ETL layer

This document asks for the exact requirements and acceptance criteria needed before implementation starts.

## 1. Scope Confirmation

Please confirm which of the following must be implemented now:

- `cwi_graph_ontology.py`
- `cwi_entity_extractor.py`
- `cwi_relationship_extractor.py`
- `cwi_graph_backfill.py`
- `cwi_graph_rollup.py`
- `cwi_graph_curate.py`
- `cwi_graph_crosswalk.py`
- `cwi_age_graph_writer.py`

For each item, please classify it as one of:

- required now
- required later
- not needed because the current repo already satisfies the function another way

## 2. Implementation Shape

Please confirm how these should be implemented:

### Option A: Repo-native modules

Implement them as repo-native modules integrated into:

- `src/etl/cwi_etl/`
- `tools/teacher-node/`

This is the recommended option.

### Option B: Exact handoff filenames

Implement them as new files matching the exact handoff names:

- `cwi_graph_ontology.py`
- `cwi_entity_extractor.py`
- etc.

Please confirm which option is wanted.

## 3. Source of Truth for Ontology / Canonical Naming

Please provide the source of truth for graph ontology and canonical naming.

Examples:

- an existing glossary file
- a schema or JSON/YAML ontology file
- an external controlled vocabulary
- a document that defines approved entity types and names
- permission to derive an initial ontology from current fixtures and documents

If no existing ontology exists, please confirm whether an initial controlled vocabulary should be created from current repo fixtures and sample content.

## 4. Required Entity Types

Please list the exact entity types that must be supported.

Examples:

- `Equipment`
- `Component`
- `Metric`
- `Procedure`
- `Manual`
- `Operation`
- `MaintenanceTask`
- `Alarm`
- `Location`

For each entity type, please provide:

- required fields
- optional fields
- canonical naming rules
- whether confidence is required
- whether authority/classification is required

## 5. Required Relationship Types

Please list the exact relationship types that must be supported.

Examples:

- `MEASURED_BY`
- `TRACKS`
- `MONITORED_IN`
- `MAINTAINED_IN`
- `PROCEDURE_FAMILY_LINK`
- `RELATED_TO`

For each relationship type, please provide:

- source entity type
- target entity type
- required provenance fields
- whether confidence is required
- any domain-specific validation rules

## 6. Input Sources

Please define what inputs each stage should read.

Examples:

- extracted text files
- normalized document JSON
- manifest inventories
- existing ingested pack artifacts
- per-document entity JSON
- per-document relationship JSON

For each utility, please specify:

- expected input file format
- expected directory layout
- whether it operates per document, per pack, or across packs

## 7. Output Artifacts

Please define the expected outputs for each stage.

For each utility, specify:

- output file format
- JSON object, JSON array, or JSONL
- file naming convention
- required fields
- whether output is per-document, per-pack, per-release, or global

Examples to clarify:

- entity extraction output
- relationship extraction output
- pack rollup output
- curated rollup output
- crosswalk output
- AGE writer output

## 8. Ontology / Canonicalization Acceptance Criteria

Please define the acceptance criteria for the ontology layer.

Required clarifications:

- how canonical names are chosen
- how aliases/synonyms are handled
- how entity type normalization works
- how domain classification is assigned
- what causes a candidate entity to be rejected
- how deterministic shared entity IDs are generated

## 9. Entity Extraction Acceptance Criteria

Please define the acceptance criteria for entity extraction.

Required clarifications:

- minimum required fields in the output
- whether confidence is required
- whether document class is required
- whether authority/source weighting is required
- whether provenance back to the source document is required
- whether rule-based extraction, pattern-based extraction, or model-based extraction is expected

## 10. Relationship Extraction Acceptance Criteria

Please define the acceptance criteria for relationship extraction.

Required clarifications:

- minimum required fields in the output
- required provenance fields
- confidence scoring rules
- whether relationships must reference canonical entity IDs
- whether direct and inferred relationships should be distinguished

## 11. Backfill Requirements

Please define what the backfill utility must do.

Required clarifications:

- whether it walks manifest inventories
- whether it scans previously ingested documents
- how it decides which artifacts are backfillable
- how skipped files should be reported
- whether it writes a summary report

## 12. Rollup Requirements

Please define the rollup stage.

Required clarifications:

- whether rollup is pack-level only
- whether rollup is release-level as well
- what counts or metrics must be reported
- whether raw provenance is preserved in rollup output

## 13. Curation Requirements

Please define the curation stage.

Required clarifications:

- confidence threshold for dropping entities/relationships
- rules for removing generic or invalid terms
- whether pack-boundary enforcement is required
- whether curation must output summary metrics and rejection counts
- whether rejected items should be retained in a sidecar report

## 14. Crosswalk Requirements

Please define the cross-pack crosswalk logic.

Required clarifications:

- exact rules for creating `MONITORED_IN`
- exact rules for creating `MAINTAINED_IN`
- exact rules for creating `PROCEDURE_FAMILY_LINK`
- whether links are rule-based, heuristic, or model-derived
- whether provenance from both packs/documents must be retained

## 15. AGE Writer Requirements

Please define the expected AGE writer behavior.

Required clarifications:

- should it output SQL, Cypher text, or both
- should it load directly into AGE or only generate artifacts
- required graph-name handling
- required graph summary metadata
- required vertex/edge/cross-pack counts
- whether idempotent `MERGE` behavior is required

Important note:

The current repo already has AGE ingestion functionality in:

- `src/etl/cwi_etl/graph_store.py`
- `src/etl/cwi_etl/pipeline.py`

Please confirm whether that existing path should be extended, or whether a separate dedicated writer CLI is still desired.

## 16. Sample Inputs and Expected Outputs

Please provide at least:

1. one sample document that should produce entities
2. one sample document that should produce relationships
3. one sample cross-pack case that should produce crosswalk edges

For each sample, please include:

- input
- expected entities
- expected relationships
- expected canonical names
- expected output artifact shape

Without concrete samples, implementation will require guesswork.

## 17. Testing / Validation Requirements

Please define the required test coverage.

Examples:

- unit tests for each utility
- end-to-end graph build test
- deterministic output tests
- fixture-based regression tests
- graph ingest integration test against Apache AGE

Please confirm whether each stage must be testable independently.

## 18. Default Assumptions If No Further Guidance Is Available

If no additional requirements are provided, the recommended defaults are:

- implement these as repo-native modules under `src/etl/cwi_etl/`
- add thin teacher-node CLI entrypoints under `tools/teacher-node/`
- use deterministic JSON outputs
- preserve provenance fields wherever possible
- use canonical IDs derived deterministically from normalized entity content
- extend the existing AGE ingestion path instead of creating a second AGE writer unless separation is explicitly required
- add unit tests for each stage plus one end-to-end teacher pipeline test

## 19. Recommended Phase Order

If implementation should begin before every detail is finalized, the recommended order is:

### Phase 1

- ontology / canonicalization
- entity extractor
- relationship extractor
- rollup
- AGE writer integration

### Phase 2

- backfill
- curation
- crosswalk generation

## 20. Short Prompt You Can Paste Into ChatGPT

```text
We need to implement the missing teacher-side graph tooling in the current CWI repo. Please answer the following so engineering can build the correct modules without guesswork:

1. Which of these are required now: ontology, entity extractor, relationship extractor, backfill, rollup, curation, crosswalk, AGE writer?
2. Should these be repo-native ETL/teacher-node modules or exact new files matching the handoff names?
3. What is the source of truth for ontology and canonical naming?
4. What entity types and relationship types are required?
5. What are the exact input and output formats for each stage?
6. What are the acceptance criteria for ontology, extraction, curation, crosswalk, and AGE writing?
7. Please provide sample inputs and expected outputs for at least one entity case, one relationship case, and one cross-pack case.
8. Please confirm whether the existing AGE ingestion logic should be extended or whether a separate dedicated writer CLI is required.
```
