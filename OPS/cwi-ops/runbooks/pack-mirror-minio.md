# Runbook: Teacher MinIO -> Core Pack Service Mirror

## Purpose
Mirror Teacher-published packs from MinIO into Core Pack Service filesystem layout with integrity verification.

## Preconditions
- `mc` installed on the Core host.
- Read-only MinIO credentials configured for the Teacher bucket.
- Pack Service container mounts `/srv/cwi/packs` read-only.

## Published layout (Teacher MinIO)
Bucket: `cwi-packs-published`
Path layout per pack:
```
<pack_id>/latest.txt
<pack_id>/<hash>/pack.json
<pack_id>/<hash>/anchor.txt
```
`latest.txt` is the canonical SHA-256 hash of `pack.json` serialized with sorted keys and no whitespace.

## Scripts
Copy these scripts to `/opt/cwi/bin` on Core and mark executable:
```
cp OPS/cwi-ops/scripts/verify_pack_anchor.py /opt/cwi/bin/
cp OPS/cwi-ops/scripts/pack_mirror_one.sh /opt/cwi/bin/
chmod +x /opt/cwi/bin/verify_pack_anchor.py /opt/cwi/bin/pack_mirror_one.sh
```

## systemd units
Create `/etc/systemd/system/cwi-pack-mirror.service`:
```ini
[Unit]
Description=CWI Pack Mirror (Teacher MinIO -> Core packs)
Wants=network-online.target
After=network-online.target

[Service]
Type=oneshot
User=root
Group=root
Environment=HOME=/root
Environment=PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
ExecStart=/opt/cwi/bin/pack_mirror_one.sh enterprise
ExecStart=/opt/cwi/bin/pack_mirror_one.sh personal
TimeoutStartSec=300

[Install]
WantedBy=multi-user.target
```

Create `/etc/systemd/system/cwi-pack-mirror.timer`:
```ini
[Unit]
Description=Run CWI Pack Mirror periodically

[Timer]
OnBootSec=30s
OnUnitActiveSec=60s
Persistent=true
Unit=cwi-pack-mirror.service

[Install]
WantedBy=timers.target
```

Enable:
```
systemctl daemon-reload
systemctl enable --now cwi-pack-mirror.timer
systemctl list-timers | grep cwi-pack-mirror || true
```

## Verification
Teacher MinIO live check:
```
curl -s -o /dev/null -w "%{http_code}\n" http://<TEACHER_MINIO>:9000/minio/health/live
```

Smoke script (sets required env vars and runs all checks):
```
TEACHER_MINIO_BASE=http://<TEACHER_MINIO>:9000 \
PACK_SERVICE_URL=http://localhost:18083 \
GATEWAY_BASE_URL=http://<GATEWAY_BASE_URL> \
OPS/cwi-ops/scripts/pack-mirror-smoke.sh
```

Optional systemd units for smoke checks:

Create `/etc/systemd/system/cwi-pack-mirror-smoke.service`:
```ini
[Unit]
Description=CWI Pack Mirror Smoke Checks
Wants=network-online.target
After=network-online.target

[Service]
Type=oneshot
User=root
Group=root
Environment=PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
# Optional: EnvironmentFile=-/etc/cwi/pack-mirror-smoke.env
Environment=TEACHER_MINIO_BASE=http://<TEACHER_MINIO>:9000
Environment=PACK_SERVICE_URL=http://localhost:18083
# Environment=GATEWAY_BASE_URL=http://<GATEWAY_BASE_URL>
ExecStart=/opt/cwi/bin/pack-mirror-smoke.sh
TimeoutStartSec=120

[Install]
WantedBy=multi-user.target
```

Create `/etc/systemd/system/cwi-pack-mirror-smoke.timer`:
```ini
[Unit]
Description=Run CWI Pack Mirror smoke checks periodically

[Timer]
OnBootSec=2m
OnUnitActiveSec=10m
Persistent=true
Unit=cwi-pack-mirror-smoke.service

[Install]
WantedBy=timers.target
```

Enable:
```
systemctl daemon-reload
systemctl enable --now cwi-pack-mirror-smoke.timer
systemctl list-timers | grep cwi-pack-mirror-smoke || true
```

Mirror one pack manually:
```
/opt/cwi/bin/pack_mirror_one.sh enterprise
/opt/cwi/bin/pack_mirror_one.sh personal
```

Filesystem layout on Core:
```
find /srv/cwi/packs/enterprise -maxdepth 2 -type f | sort
find /srv/cwi/packs/personal -maxdepth 2 -type f | sort
```

Pack Service checks:
```
curl -s http://localhost:18083/packs | head -n 50
curl -s http://localhost:18083/packs/enterprise/latest/pack.json | head
```

Logs:
```
journalctl -u cwi-pack-mirror.service -n 200 --no-pager
```
