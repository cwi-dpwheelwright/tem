# Resource Limits (Iteration-08)

## Recommended starting limits
- **Gateway (Caddy)**: 0.25 CPU / 256MB
- **Router**: 1 CPU / 1GB
- **Orchestrator**: 1-2 CPU / 2GB
- **Identity server**: 0.5 CPU / 512MB
- **Vector shard (Qdrant)**: 2 CPU / 4GB (scale with data)
- **Model worker (GPU)**: GPU required; CPU 2 / RAM 8GB minimum

## Restart policies
- All core services: `restart: unless-stopped` or `on-failure`

## Notes
Adjust based on observed load and query volume.
