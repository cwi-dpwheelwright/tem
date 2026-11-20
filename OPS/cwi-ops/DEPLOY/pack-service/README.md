# DEPLOY: pack-service

## Purpose
Compose bundle for the static pack-service role.

## Files
- docker-compose.yml
- nginx.conf
- validate-packs.sh
- .env.template (copy to .env)

## Pack layout
Expected on disk under PACKS_DIR:
- /packs/enterprise/pack.json
- /packs/enterprise/anchor.txt
- /packs/personal/pack.json
- /packs/personal/anchor.txt

## Setup
1. Copy .env.template to .env and update values.
2. Ensure PACKS_DIR contains the frozen pack directories.
3. Start service:
   docker compose up -d
