from cwi_connectors.decorators import retry_decorator
from cwi_connectors.execution import ChainHandler, ServiceResponse
from cwi_connectors.framework import ConnectorContext
from cwi_connectors.normalization import normalize_document
from cwi_connectors.google_drive_connector import GoogleDriveConfig, GoogleDriveConnector
from cwi_connectors.json_connector import JsonConnector, JsonConnectorConfig
from cwi_connectors.outlook_connector import OutlookConfig, OutlookConnector
from cwi_connectors.smartsheet_connector import SmartsheetConfig, SmartsheetConnector
from cwi_connectors.structured_connector import CsvStructuredConfig, CsvStructuredConnector


class EchoHandler:
    def handle(self, payload):
        return ServiceResponse.success({"payload": payload})


def add_flag(name: str):
    def decorator(handler):
        class Wrapped:
            def handle(self, payload):
                payload[name] = True
                return handler.handle(payload)

        return Wrapped()

    return decorator


def test_chain_handler_applies_decorators():
    handler = ChainHandler(EchoHandler(), add_flag("a"), add_flag("b"))
    response = handler.handle({})
    assert response.ok
    assert response.data["payload"]["a"] is True
    assert response.data["payload"]["b"] is True


def test_retry_decorator_retries():
    class Flaky:
        def __init__(self):
            self.calls = 0

        def handle(self, payload):
            self.calls += 1
            if self.calls < 2:
                return ServiceResponse.fail("boom", "fail", status_code=500)
            return ServiceResponse.success({"ok": True})

    handler = ChainHandler(Flaky(), retry_decorator(max_attempts=2, backoff_seconds=0))
    response = handler.handle({})
    assert response.ok


def test_normalize_document_redacts():
    record = normalize_document(
        {"doc_id": "doc-1", "title": "Title", "content": "secret content", "source": "demo"},
        redaction_tokens=["secret"],
    )
    assert "REDACTED" in record.content


def test_google_drive_connector_returns_items():
    connector = GoogleDriveConnector(GoogleDriveConfig(root_folder_id="root", fixture_path="drive/list_files.json"))
    result = connector.run(
        context=ConnectorContext(tenant_id="tenant", workspace_id="ws", connector_id="conn"),
        state=None,
    )
    assert len(list(result.items)) == 1


def test_outlook_connector_returns_items():
    connector = OutlookConnector(OutlookConfig(mailbox="user@example.com", fixture_path="outlook/messages.json"))
    result = connector.run(
        context=ConnectorContext(tenant_id="tenant", workspace_id="ws", connector_id="conn"),
        state=None,
    )
    assert len(list(result.items)) == 1


def test_smartsheet_connector_returns_items():
    connector = SmartsheetConnector(SmartsheetConfig(sheet_id="sheet-1", fixture_path="smartsheet/sheet.json"))
    result = connector.run(
        context=ConnectorContext(tenant_id="tenant", workspace_id="ws", connector_id="conn"),
        state=None,
    )
    assert len(list(result.items)) == 1


def test_structured_csv_connector_returns_items():
    connector = CsvStructuredConnector(
        CsvStructuredConfig(
            source_id="contacts",
            fixture_path="connectors/structured/sample.csv",
        )
    )
    result = connector.run(
        context=ConnectorContext(tenant_id="tenant", workspace_id="ws", connector_id="conn"),
        state=None,
    )
    assert len(list(result.items)) == 2


def test_json_connector_returns_items():
    connector = JsonConnector(JsonConnectorConfig(fixture_path="connectors/json/records.json", record_path="records"))
    result = connector.run(
        context=ConnectorContext(tenant_id="tenant", workspace_id="ws", connector_id="conn"),
        state=None,
    )
    assert len(list(result.items)) == 2
