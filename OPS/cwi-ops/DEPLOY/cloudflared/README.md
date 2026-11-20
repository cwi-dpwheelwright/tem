# cloudflared deploy bundle

This bundle runs the live remotely-managed Cloudflare Tunnel as a Docker-managed service on the public gateway host.

## Why this bundle exists

The environment already uses a token-based remotely-managed tunnel. This bundle makes the runtime deployment of `cloudflared` repo-managed so it can be deployed, audited, and restarted using the same `DEPLOY` workflow as the rest of the public host stack.

Cloudflare keeps the public hostname routing for remotely-managed tunnels. The current live hostnames are:

- `cwigpt.com`
- `www.cwigpt.com`
- `shield.cwigpt.com`

The gateway now serves those hostnames on both HTTP and HTTPS, which keeps the current Cloudflare origin mapping working even when Cloudflare is still forwarding to `http://127.0.0.1:80`.

The container uses host networking so that Cloudflare's existing `127.0.0.1:80` origin target resolves to the host gateway exactly the same way it did under the previous systemd service.

## Before deploy

1. Copy `.env.template` to `.env`.
2. Set `CLOUDFLARED_TUNNEL_TOKEN` to the live tunnel token.
3. Ensure the gateway bundle is deployed first.

## Deploy

```
cp .env.template .env
docker compose up -d
```

The container runs:

```
cloudflared tunnel --no-autoupdate run --token <TOKEN>
```

## Validation

After deployment, verify:

- `https://cwigpt.com/` returns `200`
- `https://www.cwigpt.com/` returns `200`
- `https://shield.cwigpt.com/healthz` returns `200`

## Important notes

- This bundle owns the runtime deployment of `cloudflared`.
- The Cloudflare-side tunnel routing rules remain remotely managed until API or Terraform credentials are added and the remote config is automated from code.
