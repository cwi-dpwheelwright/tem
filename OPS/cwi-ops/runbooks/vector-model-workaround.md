**Purpose**
Temporary vector shard and model stubs to keep E2E running when external dependencies are unreachable.

**Enable**
1. `powershell -ExecutionPolicy Bypass -File OPS/cwi-ops/scripts/workaround-vector-model-stubs.ps1 -Mode enable`
2. Verify `http://100.120.208.51:18085/api/health` returns `ok` and `powershell -ExecutionPolicy Bypass -File OPS/cwi-ops/scripts/run-e2e-gateway.ps1` completes.

**Disable**
1. `powershell -ExecutionPolicy Bypass -File OPS/cwi-ops/scripts/workaround-vector-model-stubs.ps1 -Mode disable -VectorShard1 http://100.115.118.16:6333 -VectorShard2 http://100.91.142.103:6333 -ModelUrl http://100.124.140.66:11434`
2. Verify `http://100.120.208.51:18085/api/health` and the E2E script.

**Notes**
1. Stubs run as containers `internal-02-data-vector-stub` and `internal-02-model-stub` on the MCP host.
2. This modifies `/opt/cwi/DEPLOY/mcp-core/.env` to point to the stubs.
3. Orchestrator DB schema updates were applied to fix runtime errors. Reverting those requires a DB restore.
