# Recursive Intelligence Controlled ASK E2E

This folder contains a controlled 50-case ASK suite for proving recursive learning quality with pre/post evidence.

## Assets
- `controlled-ask-e2e-suite.v1.json`: 50 synthetic, non-sensitive controlled cases.
- `run-controlled-ask-e2e.py`: stdlib-only runner that writes result tracking and Markdown reports.
- `e2e-recursive-sweep.sh`: broader environment sweep for recursive runtime capabilities.

## Dry Run
Use dry run to create a NOT_RUN report without touching live services:

```powershell
python testing\recursive-intelligence\run-controlled-ask-e2e.py
```

## Execute
Set the normal ASK endpoint and either identity login environment variables or a bearer token, then pass `--execute`. The default learning mode sends the controlled learning facts through the normal ASK path.

Identity login is preferred for production-like runs. The runner posts to `/auth/login`, extracts `access_token`, and applies it as `Authorization: Bearer <token>` for ASK calls. Passwords, access tokens, refresh tokens, and raw login bodies are not written to artifacts.
The controlled suite starts through `/api/ask`. If `/api/ask` returns deferred home-node work instead of a final answer, the runner polls `/api/ask/deferred/{work_id}?request_id=...` for up to 120 seconds by default before grading. Set `CWI_DEFERRED_POLL_SECONDS` or pass `--deferred-poll-seconds 0` to change or disable that loopback behavior.

```powershell
$env:CWI_BASE_URL = "https://<GATEWAY_FQDN>"
$env:CWI_IDENTITY_USERNAME = "<USERNAME>"
$env:CWI_IDENTITY_PASSWORD = "<PASSWORD>"
python testing\recursive-intelligence\run-controlled-ask-e2e.py --execute --auth-mode identity
```

For a standard tenant-scoped user, also set:

```powershell
$env:CWI_IDENTITY_TENANT_ID = "<TENANT_ID>"
```

If you already have a token, token mode remains supported:

```powershell
$env:CWI_BASE_URL = "https://<GATEWAY_FQDN>"
$env:CWI_AUTH_TOKEN = "<TOKEN>"
python testing\recursive-intelligence\run-controlled-ask-e2e.py --execute --auth-mode token
```

For deeper structural injection, use the recursive event API explicitly:

```powershell
$env:CWI_BASE_URL = "https://<GATEWAY_FQDN>"
$env:CWI_ORCHESTRATOR_BASE_URL = "http://<ORCHESTRATOR_HOST>:<PORT>"
$env:CWI_IDENTITY_USERNAME = "<USERNAME>"
$env:CWI_IDENTITY_PASSWORD = "<PASSWORD>"
python testing\recursive-intelligence\run-controlled-ask-e2e.py --execute --auth-mode identity --learning-mode recursive_event
```

Optional smoke execution:

```powershell
python testing\recursive-intelligence\run-controlled-ask-e2e.py --execute --case-limit 5
```

## Artifacts
Runs are written under:

```text
testing/runs/recursive-intelligence/<run-id>/
```

New run IDs use a readable UTC timestamp, for example:

```text
2026-05-08_01-37-42_UTC-<hash>
```

Each run includes:
- `summary.json`: machine-readable pass/fail counts and category totals.
- `results.ndjson`: one result record per controlled case.
- `report.md`: stakeholder-ready update with a category summary and recursive-intelligence grade.
- `auth-status.json`: redacted authentication status for executed runs.
- `cases/<case-id>/`: request/response evidence for live execution.

Deferred home-node runs also include `*-deferred-<attempt>-response.json` artifacts for each poll attempt. Response artifacts redact tokens, tenant/user identifiers, node identifiers, private routing IPs, and local source paths. If work remains unresolved, the report grades the run as `UNKNOWN_HOME_NODE_DEFERRED_UNRESOLVED` rather than treating a pending queue acknowledgement as recursive-intelligence evidence.

The output root also includes:
- `latest-run.md` and `latest-run.json`: pointer to the newest run.
- `runs-index.md`: newest-first run index with local and UTC timestamps.

Report validity is strict:
- `NOT RUN = 0`: valid behavioral report; PASS/FAIL results can be used as evidence.
- `NOT RUN > 0`: invalid behavioral report; the artifact is only an execution-attempt diagnostic.

The grade is intentionally evidence-scoped:
- `UNKNOWN`: dry run or no live evidence.
- `UNKNOWN_WAF_BLOCKED`: production perimeter blocked the runner before the app handled the ASK request.
- `B+`: all controlled ASK E2E cases passed; grade is capped until graph/vector/Critic/ledger transitions are independently verified.
- `B`/`C`/`D`: partial pass bands.
- `F`: most controlled behavior failed, or any privacy-boundary case failed.

## Structural Evidence
Executed reports include a structural evidence section with two levels of proof:

- **Trace-derived evidence**: collected from the ASK response trace. These checks prove the response reported governed memory update, governed memory recall, memory-source grounding, Truth Engine fields, response-framework overlay fields, provenance/trace consistency, and pack-trust hash signals.
- **Independent subsystem verification**: direct inspection of backing graph, vector, Critic, and ledger stores. These remain `UNKNOWN` in this runner until those stores are queried directly.

The trace-derived checks strengthen the controlled ASK result, but they do not by themselves prove every backing subsystem transition. Use that distinction when deciding whether the report supports a `B+` behavioral claim or an `A`-level structural readiness claim.

## Release Use
A recursive learning quality claim should only use an executed report when:
- the run is not a dry run;
- every hard governance gate remains covered by the validation contract;
- failures are triaged as regression test, known limitation, requirement, policy block, or rejected non-issue with evidence;
- private canary cases do not expose canary strings in user-facing answers.

## Structural Recursive Validation
The controlled ASK suite proves behavioral pre/post learning through the normal ASK path. It does not, by itself, prove the full structural recursive loop. Use the structural runner when the goal is to verify backing subsystem transitions.

Assets:
- `structural-recursive-suite.v1.json`: de-identified internal ownership/open-loop structural pilot.
- `run-structural-recursive-e2e.py`: structural runner that records PASS, FAIL, UNKNOWN, and NOT_RUN for each subsystem step.

Dry run:

```powershell
python testing\recursive-intelligence\run-structural-recursive-e2e.py
```

Production-like execution:

```powershell
$env:CWI_BASE_URL = "https://<GATEWAY_FQDN>"
$env:CWI_IDENTITY_USERNAME = "<USERNAME>"
$env:CWI_IDENTITY_PASSWORD = "<PASSWORD>"
python testing\recursive-intelligence\run-structural-recursive-e2e.py --execute --auth-mode identity
```

The structural runner checks:
- actual local Process Miner observation output;
- recursive event normalization and ingest;
- recursive event queryability;
- graph update candidate creation;
- graph conflict candidate creation;
- graph-to-vector refresh job creation;
- vector refresh completion status when available;
- optional ASK retrieval and Truth Engine trace signal;
- external opinion / Runtime Critic advisory path;
- quality observation ingest and Response Quality Graph update;
- federated summary generation;
- validated cache and ledger proof availability.

Evidence semantics:
- `PASS`: direct evidence was observed by this runner.
- `FAIL`: a live step contradicted expected structural behavior.
- `UNKNOWN`: the run executed, but the specific proof was not independently observable.
- `NOT_RUN`: dry run or dependency-gated step.

Structural grades are separate from ASK grades. A `B+` controlled ASK report can coexist with structural `UNKNOWN` items; that means behavioral learning worked for the controlled questions, while closed-loop graph/vector/Critic/cache/ledger readiness still needs direct evidence.
