# Server Config Cutover Plan (Iteration-09)

## Objectives
- Allow post-deploy retargeting of all services without rebuilds
- Remove silent local defaults from runtime code and configs
- Enforce environment validation to block placeholder URLs in non-dev roles

## Strategy
- Use `OPS/cwi-ops/env/ENV_VAR_MATRIX.json` as the configuration contract
- Require explicit configuration for service base URLs, OIDC settings, and external dependencies
- Provide runtime config injection for web apps via `/config/runtime.json` or `window.__RUNTIME_CONFIG__`
- Provide mobile configuration via build flavors and optional remote config
- Maintain `.env.example` and deployment templates with placeholder tokens, not localhost

## Implementation Steps
- Extend ENV_VAR_MATRIX with frontend, mobile, agent, and bot variables
- Update `validate-env` scripts to enforce required values and block placeholder URLs in production roles
- Add runtime config file generation to Docker entrypoints for web apps
- Remove hard-coded localhost defaults from backend and agents
- Update READMEs and ops compose files to reference server config paths

## Cutover Checklist
- Populate server config values in DEPLOY bundles and secret manager
- Run `validate-env` against target role bundles
- Run full-stack smoke and E2E simulation suites
- Record artifacts under `artifacts/qa/iteration-09-*.txt`
