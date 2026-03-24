from __future__ import annotations

from pathlib import Path


def resolve_documents_dir() -> Path:
    documents = Path.home() / "Documents"
    documents.mkdir(parents=True, exist_ok=True)
    return documents


def resolve_base_dir() -> Path:
    base_dir = resolve_documents_dir() / "DOPAgent"
    base_dir.mkdir(parents=True, exist_ok=True)
    return base_dir
