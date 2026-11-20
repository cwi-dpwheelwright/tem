# DEPLOY: teacher_minio

## Purpose
Optional MinIO object-store bundle for teacher-side artifact storage.

This recreates the MinIO-backed teacher artifact pattern observed on the live teacher host:
- service DNS: `cwi-teacher-node-minio`
- default container name: `cwi-minio`
- API port: `9000`
- Console port: `9001`

## Files
- docker-compose.yml
- .env.template (copy to .env)

## Required env
- `MINIO_ROOT_USER`
- `MINIO_ROOT_PASSWORD`

## Optional env
- `TEACHER_MINIO_IMAGE`
- `TEACHER_MINIO_CONTAINER_NAME`
- `TEACHER_MINIO_DATA_HOST_PATH`
- `TEACHER_MINIO_API_PORT`
- `TEACHER_MINIO_CONSOLE_PORT`

## Teacher-node integration
To point `teacher-node` at this MinIO service, use:
- `TEACHER_NODE_ARTIFACT_BACKEND=minio`
- `MINIO_ENDPOINT=cwi-teacher-node-minio:9000`
- `MINIO_ACCESS_KEY=<same value as MINIO_ROOT_USER>`
- `MINIO_SECRET_KEY=<same value as MINIO_ROOT_PASSWORD>`
- `MINIO_BUCKET=teacher-node`
- `MINIO_SECURE=false`

## Start
```bash
docker compose up -d
```

## Health checks
```bash
curl -s http://localhost:9000/minio/health/live
curl -s http://localhost:9001
```
