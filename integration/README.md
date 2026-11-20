# CWI integration env bundle (local/dev)

This folder contains:

- `.env.integration.local` — a ready-to-load env file
- `fixtures/` — JSON payloads used by integration gold tests for patent-aligned suites

## How to use

1) Copy `cwi-integration-env-bundle/` into the root of your CWI repo (or keep it there).

2) Load env vars (bash):

   ```bash
   set -a
   source cwi-integration-env-bundle/.env.integration.local
   set +a
   ```

3) Run your integration gold tests runner (whatever your repo uses).

## Auth token

The generated `CWI_AUTH_TOKEN` is a placeholder (`Bearer cwi-dev-integration-token`).
If your Router enforces JWT in your local stack, replace this with a real Bearer JWT that matches
your dev signing key/issuer/audience settings.
