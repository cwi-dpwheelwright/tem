from __future__ import annotations

from typing import Dict, Type

from .framework import Connector
from .google_drive_connector import GoogleDriveConnector
from .json_connector import JsonConnector
from .outlook_connector import OutlookConnector
from .smartsheet_connector import SmartsheetConnector
from .streaming_connector import SimulatedStreamingConnector
import os
from .structured_connector import CsvStructuredConnector
from .versioning import CONNECTOR_SDK_VERSION


CONNECTOR_TYPES: Dict[str, Type[Connector]] = {
    "google_drive": GoogleDriveConnector,
    "json": JsonConnector,
    "outlook": OutlookConnector,
    "smartsheet": SmartsheetConnector,
    "structured_csv": CsvStructuredConnector,
}
if os.environ.get("CWI_ENABLE_SIMULATED_CONNECTORS", "").lower() == "true":
    CONNECTOR_TYPES["stream_simulated"] = SimulatedStreamingConnector


def resolve_connector(connector_type: str) -> Type[Connector]:
    connector_type = connector_type.strip()
    if connector_type not in CONNECTOR_TYPES:
        raise ValueError(f"Unknown connector type: {connector_type}")
    connector_cls = CONNECTOR_TYPES[connector_type]
    sdk_version = getattr(connector_cls, "sdk_version", None)
    if sdk_version != CONNECTOR_SDK_VERSION:
        raise ValueError(
            f"Connector {connector_type} SDK mismatch: {sdk_version} != {CONNECTOR_SDK_VERSION}"
        )
    return connector_cls
