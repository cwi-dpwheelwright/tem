from cwi_connectors.framework import ConnectorContext, ConnectorRunResult, ConnectorScheduler
from cwi_connectors.state_store import FileStateStore


class FakeConnector:
    name = "fake"

    def __init__(self) -> None:
        self.calls = 0

    def run(self, context, state):
        self.calls += 1
        cursor = f"cursor-{self.calls}"
        return ConnectorRunResult(items=[{"id": self.calls}], next_cursor=cursor)


def test_scheduler_persists_cursor(tmp_path):
    store = FileStateStore(str(tmp_path / "state.json"))
    scheduler = ConnectorScheduler(store)
    connector = FakeConnector()
    context = ConnectorContext(tenant_id="tenant", workspace_id="ws", connector_id="conn-1")

    result = scheduler.run_once(connector, context)
    assert result.next_cursor == "cursor-1"

    result_two = scheduler.run_once(connector, context)
    assert result_two.next_cursor == "cursor-2"
