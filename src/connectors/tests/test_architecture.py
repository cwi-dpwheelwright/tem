import ast
from pathlib import Path


def test_architecture_no_db_in_runtime_modules():
    root = Path(__file__).resolve().parents[1] / "cwi_connectors"
    for path in root.glob("*.py"):
        if path.name == "state_store.py":
            continue
        if "psycopg" in path.read_text(encoding="utf-8"):
            raise AssertionError(f"psycopg import found in {path.name}")


def test_architecture_no_deep_if_chains():
    root = Path(__file__).resolve().parents[1] / "cwi_connectors"
    for path in root.glob("*.py"):
        tree = ast.parse(path.read_text(encoding="utf-8"))
        max_depth = _max_if_depth(tree)
        if max_depth > 4:
            raise AssertionError(f"Deep if/else chain in {path.name}: depth {max_depth}")


def test_architecture_network_client_layering():
    root = Path(__file__).resolve().parents[1] / "cwi_connectors"
    allowed = {
        "auth.py",
        "http_client.py",
        "vector_ingest.py",
        "provenance.py",
        "reconciliation.py",
    }
    for path in root.glob("*.py"):
        if path.name in allowed:
            continue
        if "urllib.request" in path.read_text(encoding="utf-8"):
            raise AssertionError(f"Network client import found in {path.name}")


def _max_if_depth(tree: ast.AST) -> int:
    def walk(node: ast.AST, depth: int) -> int:
        current = depth
        for child in ast.iter_child_nodes(node):
            if isinstance(child, ast.If):
                current = max(current, walk(child, depth + 1))
            else:
                current = max(current, walk(child, depth))
        return current

    return walk(tree, 0)
