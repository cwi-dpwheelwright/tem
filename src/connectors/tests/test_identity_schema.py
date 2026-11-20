import base64
import json

import pytest
import jwt
from cryptography.hazmat.primitives.asymmetric import rsa

from cwi_connectors.config import load_runtime_config
from cwi_connectors.config_schema import validate_identity_config


def _jwt_with_sub(subject: str) -> str:
    header = base64.urlsafe_b64encode(json.dumps({"alg": "none"}).encode("utf-8")).decode("utf-8").rstrip("=")
    payload = base64.urlsafe_b64encode(json.dumps({"sub": subject}).encode("utf-8")).decode("utf-8").rstrip("=")
    return f"{header}.{payload}."


def _jwks_with_subject(subject: str, issuer: str = "test-issuer", audience: str = "test-aud") -> tuple[str, dict]:
    private_key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    public_key = private_key.public_key()
    jwk = json.loads(jwt.algorithms.RSAAlgorithm.to_jwk(public_key))
    jwk["kid"] = "test-kid"
    token = jwt.encode(
        {"sub": subject, "iss": issuer, "aud": audience},
        private_key,
        algorithm="RS256",
        headers={"kid": "test-kid"},
    )
    return token, {"keys": [jwk]}


def test_identity_schema_validates_owner():
    payload = {"owner_id": "user-1", "delegate_ids": ["user-2"]}
    assert validate_identity_config(payload).raw["owner_id"] == "user-1"


def test_identity_binding_requires_token(tmp_path):
    config = tmp_path / "connectors.yaml"
    config.write_text(
        """
        tenant_id: tenant-1
        workspace_id: ws-1
        poll_interval_seconds: 10
        state_store:
          kind: file
          path: state.json
        connectors:
          - id: drive
            type: google_drive
            config:
              root_folder_id: root
            auth:
              type: api_key
              api_key:
                header_name: X-Api-Key
                value: token
            identity:
              owner_id: user-1
        """,
        encoding="utf-8",
    )
    with pytest.raises(ValueError, match="orchestrator_token is required"):
        load_runtime_config(str(config))


def test_identity_binding_rejects_unmatched_subject(tmp_path):
    config = tmp_path / "connectors.yaml"
    token = _jwt_with_sub("user-2")
    config.write_text(
        f"""
        tenant_id: tenant-1
        workspace_id: ws-1
        poll_interval_seconds: 10
        state_store:
          kind: file
          path: state.json
        orchestrator_url: http://localhost:18081
        orchestrator_token: "{token}"
        orchestrator_allow_insecure_jwt: true
        connectors:
          - id: drive
            type: google_drive
            config:
              root_folder_id: root
            auth:
              type: api_key
              api_key:
                header_name: X-Api-Key
                value: token
            identity:
              owner_id: user-1
        """,
        encoding="utf-8",
    )
    with pytest.raises(ValueError, match="not authorized"):
        load_runtime_config(str(config))


def test_identity_binding_allows_owner(tmp_path):
    config = tmp_path / "connectors.yaml"
    token = _jwt_with_sub("user-1")
    config.write_text(
        f"""
        tenant_id: tenant-1
        workspace_id: ws-1
        poll_interval_seconds: 10
        state_store:
          kind: file
          path: state.json
        orchestrator_url: http://localhost:18081
        orchestrator_token: "{token}"
        orchestrator_allow_insecure_jwt: true
        connectors:
          - id: drive
            type: google_drive
            config:
              root_folder_id: root
            auth:
              type: api_key
              api_key:
                header_name: X-Api-Key
                value: token
            identity:
              owner_id: user-1
              delegate_ids:
                - user-3
        """,
        encoding="utf-8",
    )
    runtime = load_runtime_config(str(config))
    assert runtime.connectors[0].identity is not None


def test_identity_binding_validates_jwks_token(tmp_path):
    config = tmp_path / "connectors.yaml"
    token, jwks = _jwks_with_subject("user-1")
    jwks_path = tmp_path / "jwks.json"
    jwks_path.write_text(json.dumps(jwks), encoding="utf-8")
    jwks_path_text = jwks_path.as_posix()
    config.write_text(
        f"""
        tenant_id: tenant-1
        workspace_id: ws-1
        poll_interval_seconds: 10
        state_store:
          kind: file
          path: state.json
        orchestrator_url: http://localhost:18081
        orchestrator_token: "{token}"
        orchestrator_jwks_path: '{jwks_path_text}'
        orchestrator_jwt_issuer: test-issuer
        orchestrator_jwt_audience: test-aud
        connectors:
          - id: drive
            type: google_drive
            config:
              root_folder_id: root
            auth:
              type: api_key
              api_key:
                header_name: X-Api-Key
                value: token
            identity:
              owner_id: user-1
        """,
        encoding="utf-8",
    )
    runtime = load_runtime_config(str(config))
    assert runtime.connectors[0].identity is not None


def test_identity_binding_rejects_invalid_signature(tmp_path):
    config = tmp_path / "connectors.yaml"
    token, jwks = _jwks_with_subject("user-1")
    jwks_path = tmp_path / "jwks.json"
    jwks_path.write_text(json.dumps(jwks), encoding="utf-8")
    jwks_path_text = jwks_path.as_posix()
    invalid_private_key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    invalid_token = jwt.encode(
        {"sub": "user-1", "iss": "test-issuer", "aud": "test-aud"},
        invalid_private_key,
        algorithm="RS256",
        headers={"kid": "test-kid"},
    )
    config.write_text(
        f"""
        tenant_id: tenant-1
        workspace_id: ws-1
        poll_interval_seconds: 10
        state_store:
          kind: file
          path: state.json
        orchestrator_url: http://localhost:18081
        orchestrator_token: "{invalid_token}"
        orchestrator_jwks_path: '{jwks_path_text}'
        orchestrator_jwt_issuer: test-issuer
        orchestrator_jwt_audience: test-aud
        connectors:
          - id: drive
            type: google_drive
            config:
              root_folder_id: root
            auth:
              type: api_key
              api_key:
                header_name: X-Api-Key
                value: token
            identity:
              owner_id: user-1
        """,
        encoding="utf-8",
    )
    with pytest.raises(ValueError, match="orchestrator_token validation failed"):
        load_runtime_config(str(config))
