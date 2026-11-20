from cwi_connectors.config_schema import validate_auth_config


def test_valid_api_key_auth():
    payload = {
        "type": "api_key",
        "api_key": {"header_name": "X-Api-Key", "value": "token"},
    }
    assert validate_auth_config(payload).raw["type"] == "api_key"


def test_invalid_auth_missing_type():
    payload = {"api_key": {"header_name": "X-Api-Key", "value": "token"}}
    try:
        validate_auth_config(payload)
    except ValueError as exc:
        assert "type" in str(exc)
    else:
        raise AssertionError("Expected ValueError")
