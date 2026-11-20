from __future__ import annotations

import datetime as dt
import os
import shutil
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable


@dataclass(frozen=True)
class ArtifactEntry:
    key: str
    size_bytes: int
    last_modified_utc: dt.datetime


class ArtifactStore:
    def upload(self, source: Path, key: str) -> None:
        raise NotImplementedError

    def download(self, key: str, dest: Path) -> None:
        raise NotImplementedError

    def list(self, prefix: str = "") -> Iterable[ArtifactEntry]:
        raise NotImplementedError

    def delete(self, key: str) -> None:
        raise NotImplementedError

    def prune(self, prefix: str, older_than_days: int) -> int:
        raise NotImplementedError


class FileSystemArtifactStore(ArtifactStore):
    def __init__(self, root: Path) -> None:
        self.root = root
        self.root.mkdir(parents=True, exist_ok=True)

    def upload(self, source: Path, key: str) -> None:
        dest = self.root / _normalize_key(key)
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, dest)

    def download(self, key: str, dest: Path) -> None:
        src = self.root / _normalize_key(key)
        if not src.exists():
            raise FileNotFoundError(str(src))
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, dest)

    def list(self, prefix: str = "") -> Iterable[ArtifactEntry]:
        prefix_path = _normalize_key(prefix)
        for path in self.root.rglob("*"):
            if not path.is_file():
                continue
            rel = path.relative_to(self.root).as_posix()
            if prefix_path and not rel.startswith(prefix_path):
                continue
            stat = path.stat()
            yield ArtifactEntry(
                key=rel,
                size_bytes=stat.st_size,
                last_modified_utc=dt.datetime.utcfromtimestamp(stat.st_mtime),
            )

    def delete(self, key: str) -> None:
        path = self.root / _normalize_key(key)
        if path.exists():
            path.unlink()

    def prune(self, prefix: str, older_than_days: int) -> int:
        cutoff = dt.datetime.utcnow() - dt.timedelta(days=older_than_days)
        deleted = 0
        for entry in list(self.list(prefix)):
            if entry.last_modified_utc < cutoff:
                self.delete(entry.key)
                deleted += 1
        return deleted


class MinioArtifactStore(ArtifactStore):
    def __init__(
        self,
        endpoint: str,
        access_key: str,
        secret_key: str,
        bucket: str,
        secure: bool = True,
        create_bucket: bool = False,
    ) -> None:
        try:
            from minio import Minio  # type: ignore
        except ImportError as exc:  # pragma: no cover
            raise RuntimeError("minio package required for MinIO artifact store") from exc

        self.client = Minio(
            endpoint,
            access_key=access_key,
            secret_key=secret_key,
            secure=secure,
        )
        self.bucket = bucket
        if create_bucket and not self.client.bucket_exists(bucket):
            self.client.make_bucket(bucket)

    def upload(self, source: Path, key: str) -> None:
        key = _normalize_key(key)
        self.client.fput_object(self.bucket, key, str(source))

    def download(self, key: str, dest: Path) -> None:
        key = _normalize_key(key)
        dest.parent.mkdir(parents=True, exist_ok=True)
        self.client.fget_object(self.bucket, key, str(dest))

    def list(self, prefix: str = "") -> Iterable[ArtifactEntry]:
        key_prefix = _normalize_key(prefix)
        for obj in self.client.list_objects(self.bucket, prefix=key_prefix, recursive=True):
            last_modified = obj.last_modified
            if last_modified is None:
                last_modified = dt.datetime.utcnow()
            if last_modified.tzinfo is not None:
                last_modified = last_modified.astimezone(dt.timezone.utc).replace(tzinfo=None)
            yield ArtifactEntry(
                key=obj.object_name,
                size_bytes=obj.size,
                last_modified_utc=last_modified,
            )

    def delete(self, key: str) -> None:
        self.client.remove_object(self.bucket, _normalize_key(key))

    def prune(self, prefix: str, older_than_days: int) -> int:
        cutoff = dt.datetime.utcnow() - dt.timedelta(days=older_than_days)
        deleted = 0
        for entry in list(self.list(prefix)):
            if entry.last_modified_utc < cutoff:
                self.delete(entry.key)
                deleted += 1
        return deleted


def _normalize_key(key: str) -> str:
    key = (key or "").lstrip("/")
    return key.replace("\\", "/")


def build_artifact_store_from_env(
    bucket_override: str | None = None,
    root_override: Path | None = None,
) -> ArtifactStore:
    backend = os.getenv("TEACHER_NODE_ARTIFACT_BACKEND", "filesystem").lower()
    if backend == "minio":
        endpoint = os.getenv("MINIO_ENDPOINT", "")
        access_key = os.getenv("MINIO_ACCESS_KEY", "")
        secret_key = os.getenv("MINIO_SECRET_KEY", "")
        bucket = bucket_override or os.getenv("MINIO_BUCKET", "teacher-node")
        secure = os.getenv("MINIO_SECURE", "true").lower() == "true"
        create_bucket = os.getenv("MINIO_CREATE_BUCKET", "false").lower() == "true"
        if not endpoint or not access_key or not secret_key:
            raise RuntimeError("MINIO_ENDPOINT, MINIO_ACCESS_KEY, MINIO_SECRET_KEY required")
        return MinioArtifactStore(
            endpoint=endpoint,
            access_key=access_key,
            secret_key=secret_key,
            bucket=bucket,
            secure=secure,
            create_bucket=create_bucket,
        )

    root = root_override or Path(os.getenv("TEACHER_NODE_ARTIFACT_ROOT", "artifacts/teacher-node"))
    return FileSystemArtifactStore(root)
