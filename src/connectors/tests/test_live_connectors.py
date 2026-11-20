import os

import pytest

from cwi_connectors.framework import ConnectorContext
from cwi_connectors.google_drive_connector import GoogleDriveConnector
from cwi_connectors.outlook_connector import OutlookConnector
from cwi_connectors.smartsheet_connector import SmartsheetConnector

LIVE_ENABLED = os.getenv("CWI_CONNECTORS_LIVE_TESTS") == "1"


def _api_key_auth(token: str) -> dict:
    return {"type": "api_key", "api_key": {"header_name": "Authorization", "value": f"Bearer {token}"}}


def _context(connector_id: str) -> ConnectorContext:
    return ConnectorContext(tenant_id="tenant-live", workspace_id="workspace-live", connector_id=connector_id)


@pytest.mark.integration
@pytest.mark.skipif(not LIVE_ENABLED, reason="Set CWI_CONNECTORS_LIVE_TESTS=1 to enable live tests.")
@pytest.mark.skipif(
    not os.getenv("CWI_CONNECTORS_GDRIVE_TOKEN") or not os.getenv("CWI_CONNECTORS_GDRIVE_ROOT_FOLDER_ID"),
    reason="Set CWI_CONNECTORS_GDRIVE_TOKEN and CWI_CONNECTORS_GDRIVE_ROOT_FOLDER_ID.",
)
def test_google_drive_live():
    token = os.environ["CWI_CONNECTORS_GDRIVE_TOKEN"]
    root_folder = os.environ["CWI_CONNECTORS_GDRIVE_ROOT_FOLDER_ID"]
    base_url = os.getenv("CWI_CONNECTORS_GDRIVE_API_BASE_URL", "https://www.googleapis.com/drive/v3")
    page_size = int(os.getenv("CWI_CONNECTORS_GDRIVE_PAGE_SIZE", "25"))
    connector = GoogleDriveConnector.from_config(
        {
            "root_folder_id": root_folder,
            "fixture_path": None,
            "api_base_url": base_url,
            "page_size": page_size,
        },
        auth=_api_key_auth(token),
    )
    result = connector.run(_context("gdrive-live"), None)
    assert isinstance(result.items, list)
    assert all(item.get("doc_id") and item.get("title") for item in result.items)


@pytest.mark.integration
@pytest.mark.skipif(not LIVE_ENABLED, reason="Set CWI_CONNECTORS_LIVE_TESTS=1 to enable live tests.")
@pytest.mark.skipif(
    not os.getenv("CWI_CONNECTORS_OUTLOOK_TOKEN"),
    reason="Set CWI_CONNECTORS_OUTLOOK_TOKEN.",
)
def test_outlook_live():
    token = os.environ["CWI_CONNECTORS_OUTLOOK_TOKEN"]
    mailbox = os.getenv("CWI_CONNECTORS_OUTLOOK_MAILBOX", "me")
    base_url = os.getenv("CWI_CONNECTORS_OUTLOOK_API_BASE_URL", "https://graph.microsoft.com/v1.0")
    page_size = int(os.getenv("CWI_CONNECTORS_OUTLOOK_PAGE_SIZE", "25"))
    connector = OutlookConnector.from_config(
        {
            "mailbox": mailbox,
            "fixture_path": None,
            "api_base_url": base_url,
            "page_size": page_size,
        },
        auth=_api_key_auth(token),
    )
    result = connector.run(_context("outlook-live"), None)
    assert isinstance(result.items, list)
    assert all(item.get("doc_id") and item.get("title") for item in result.items)


@pytest.mark.integration
@pytest.mark.skipif(not LIVE_ENABLED, reason="Set CWI_CONNECTORS_LIVE_TESTS=1 to enable live tests.")
@pytest.mark.skipif(
    not os.getenv("CWI_CONNECTORS_SMARTSHEET_TOKEN") or not os.getenv("CWI_CONNECTORS_SMARTSHEET_SHEET_ID"),
    reason="Set CWI_CONNECTORS_SMARTSHEET_TOKEN and CWI_CONNECTORS_SMARTSHEET_SHEET_ID.",
)
def test_smartsheet_live():
    token = os.environ["CWI_CONNECTORS_SMARTSHEET_TOKEN"]
    sheet_id = os.environ["CWI_CONNECTORS_SMARTSHEET_SHEET_ID"]
    base_url = os.getenv("CWI_CONNECTORS_SMARTSHEET_API_BASE_URL", "https://api.smartsheet.com/2.0")
    page_size = int(os.getenv("CWI_CONNECTORS_SMARTSHEET_PAGE_SIZE", "50"))
    connector = SmartsheetConnector.from_config(
        {
            "sheet_id": sheet_id,
            "fixture_path": None,
            "api_base_url": base_url,
            "page_size": page_size,
        },
        auth=_api_key_auth(token),
    )
    result = connector.run(_context("smartsheet-live"), None)
    assert isinstance(result.items, list)
    assert all(item.get("doc_id") and item.get("title") for item in result.items)
