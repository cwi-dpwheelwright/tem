# CWI Critic Node

The Critic node owns response-quality evaluation logic. Orchestrator may send observe-only ASK response snapshots to this service, but orchestrator must not run Critic scoring rules locally.

Endpoints:

- `GET /health`
- `POST /runtime/critic/observe`

The observe endpoint returns:

- `version`
- `mode`
- `overall_score`
- `scores`
- `findings`

The Critic service is intended to run as a separate container on the Critic host. Configure orchestrator with `CWI_CRITIC_BASE_URL` only after that container is deployed and reachable.
