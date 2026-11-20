import os
from pathlib import Path

from cwi_connectors.config import load_runtime_config


def test_load_runtime_config_validates(tmp_path):
    config = tmp_path / "connectors.yaml"
    os.environ["CONNECTOR_API_KEY"] = "secret"
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
            enabled: true
            config:
              root_folder_id: root
            auth:
              type: api_key
              api_key:
                header_name: X-Api-Key
                value: env://CONNECTOR_API_KEY
        """,
        encoding="utf-8",
    )
    runtime = load_runtime_config(str(config))
    assert runtime.tenant_id == "tenant-1"
    assert runtime.connectors[0].auth["api_key"]["value"] == "secret"
