from cwi_connectors.registry import CONNECTOR_TYPES, resolve_connector
from cwi_connectors.versioning import CONNECTOR_SDK_VERSION


def test_connector_registry_versions_match_sdk():
    for connector_type, connector_cls in CONNECTOR_TYPES.items():
        assert connector_cls.name == connector_type
        assert connector_cls.sdk_version == CONNECTOR_SDK_VERSION
        assert resolve_connector(connector_type) is connector_cls
