# CWI Gold Tests

## Install

```bash
python -m pip install -e tools/cwi-gold-tests
```

Or build the container:

```bash
docker build -t cwi-gold-tests -f tools/cwi-gold-tests/docker/Dockerfile tools/cwi-gold-tests
```

## Run Suites

```bash
python tools/cwi-gold-tests/run_tests.py --suite smoke --base-url http://localhost:18081
python tools/cwi-gold-tests/run_tests.py --suite integration --base-url http://localhost:18081
python tools/cwi-gold-tests/run_tests.py --suite negative --base-url http://localhost:18081
python tools/cwi-gold-tests/run_tests.py --suite e2e --base-url http://localhost:18081
python tools/cwi-gold-tests/run_tests.py --suite simulation --base-url http://localhost:18081
python tools/cwi-gold-tests/run_tests.py --suite contract
python tools/cwi-gold-tests/run_tests.py --suite performance --base-url http://localhost:18081
```

If installed as a CLI:

```bash
cwi-gold-tests --suite smoke --base-url http://localhost:18081
```

## OpenAPI Conformance

```bash
python tools/cwi-gold-tests/run_conformance.py \
  --schema contracts/openapi/cwi-router.openapi_10H_frozen.yaml \
  --base-url http://localhost:8081 \
  --safe-only
```

## Seeding (Iteration-09)

Use the seed helper to create identity tenants/users, activate packs, and ingest sample documents.

```bash
python tools/cwi-gold-tests/seeding/seed_full_stack.py \
  --config tools/cwi-gold-tests/seeding/seed_config.sample.json
```

Configuration can also be supplied via `CWI_SEED_CONFIG` or environment variables used by the script.

## Notes

- Auth tokens are fetched via the identity server when `CWI_AUTH_TOKEN` is not set.
- Most suites skip tests that require unavailable services; set the relevant `CWI_*` environment variables to enable them.
