import base64
import hashlib
from pathlib import Path
from typing import Dict, Optional


def sign_report_bundle(report_json: Path, report_md: Optional[Path], signing_key_path: Path) -> Dict[str, str]:
    try:
        from cryptography.hazmat.primitives import hashes, serialization
        from cryptography.hazmat.primitives.asymmetric import ed25519, ec, rsa, padding
    except ImportError as exc:  # pragma: no cover
        raise RuntimeError("cryptography package required for signing") from exc

    payload = _build_payload(report_json, report_md)
    digest = hashlib.sha256(payload).digest()

    key_bytes = signing_key_path.read_bytes()
    private_key = serialization.load_pem_private_key(key_bytes, password=None)

    signature: bytes
    if isinstance(private_key, ed25519.Ed25519PrivateKey):
        signature = private_key.sign(digest)
        alg = "ed25519"
    elif isinstance(private_key, ec.EllipticCurvePrivateKey):
        signature = private_key.sign(digest, ec.ECDSA(hashes.SHA256()))
        alg = "es256"
    elif isinstance(private_key, rsa.RSAPrivateKey):
        signature = private_key.sign(digest, padding.PKCS1v15(), hashes.SHA256())
        alg = "rs256"
    else:  # pragma: no cover
        raise RuntimeError("unsupported key type for signing")

    public_key = private_key.public_key()
    public_bytes = public_key.public_bytes(
        encoding=serialization.Encoding.DER,
        format=serialization.PublicFormat.SubjectPublicKeyInfo,
    )
    fingerprint = hashlib.sha256(public_bytes).hexdigest()

    return {
        "algorithm": alg,
        "signature": base64.b64encode(signature).decode("utf-8"),
        "public_key_fingerprint": fingerprint,
    }


def _build_payload(report_json: Path, report_md: Optional[Path]) -> bytes:
    data = report_json.read_bytes()
    if report_md and report_md.exists():
        data += report_md.read_bytes()
    return data
