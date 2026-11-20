from __future__ import annotations

import base64

from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric import ed25519

from node_agent_runtime.cache import load_cached_asset, store_cached_asset
from node_agent_runtime.config import CacheConfig, ConfigError


def test_cache_store_and_load(tmp_path):
    config = CacheConfig(cache_dir=tmp_path, max_bytes=1024, max_items=10)
    data = b"hello"
    entry = store_cached_asset(
        config,
        asset_id="asset-1",
        filename="payload.bin",
        data=data,
        sha256="2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824",
    )
    loaded = load_cached_asset(config, asset_id="asset-1")
    assert loaded.sha256 == entry.sha256
    assert loaded.path.read_bytes() == data


def test_cache_eviction_by_count(tmp_path):
    config = CacheConfig(cache_dir=tmp_path, max_bytes=1024 * 1024, max_items=2)
    store_cached_asset(
        config,
        asset_id="asset-1",
        filename="a.bin",
        data=b"a",
        sha256="ca978112ca1bbdcafac231b39a23dc4da786eff8147c4e72b9807785afee48bb",
    )
    store_cached_asset(
        config,
        asset_id="asset-2",
        filename="b.bin",
        data=b"b",
        sha256="3e23e8160039594a33894f6564e1b1348bbd7a0088d42c4acb73eeaed59c009d",
    )
    store_cached_asset(
        config,
        asset_id="asset-3",
        filename="c.bin",
        data=b"c",
        sha256="2e7d2c03a9507ae265ecf5b5356885a53393a2029d241394997265a1a25aefc6",
    )
    try:
        load_cached_asset(config, asset_id="asset-1")
        assert False, "expected eviction"
    except ConfigError:
        assert True


def test_cache_signature_required(tmp_path):
    private_key = ed25519.Ed25519PrivateKey.generate()
    public_key = private_key.public_key()
    public_pem = public_key.public_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PublicFormat.SubjectPublicKeyInfo,
    ).decode("utf-8")

    config = CacheConfig(
        cache_dir=tmp_path,
        max_bytes=1024,
        max_items=10,
        require_signature=True,
        signature_public_key_pem_env="CACHE_PUB_PEM",
    )

    data = b"secure"
    signature = private_key.sign(data)
    signature_b64 = base64.b64encode(signature).decode("utf-8")

    import os
    os.environ["CACHE_PUB_PEM"] = public_pem

    import hashlib
    digest = hashlib.sha256(data).hexdigest()

    store_cached_asset(
        config,
        asset_id="asset-secure",
        filename="secure.bin",
        data=data,
        sha256=digest,
        signature=signature_b64,
    )
    loaded = load_cached_asset(config, asset_id="asset-secure")
    assert loaded.sha256
