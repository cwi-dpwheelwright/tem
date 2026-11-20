# ADR-TEACHER-NODE-001: Teacher Node Runtime + Packaging

## Status
Accepted

## Context
Iteration-03 requires a teacher-node toolchain for pack validation, anchor generation,
publishing, rollback, and evaluation setup. The tool should be easy to run locally,
portable across CI environments, and consistent with existing Python-based test tooling.

## Decision
Use Python 3.11 for teacher-node tooling with a minimal `requirements.txt` and a
single-file CLI (`tools/teacher-node/teacher_node.py`). Distribute as a repo-local
utility rather than a packaged artifact for now.

## Consequences
- Pros: low friction, aligns with gold test tooling, fast iteration in CI and local dev.
- Cons: not a distributable binary; future packaging (pipx or container) may be required.
