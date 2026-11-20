from __future__ import annotations

from dataclasses import dataclass
import zipfile
from xml.etree import ElementTree
from typing import Any, Dict, Iterable, List

from pathlib import Path

from .redaction import apply_redactions, default_rules


@dataclass(frozen=True)
class DocumentRecord:
    doc_id: str
    title: str
    content: str
    source: str
    mime_type: str = "text/plain"
    metadata: Dict[str, Any] | None = None


@dataclass(frozen=True)
class DocumentChunk:
    chunk_id: str
    doc_id: str
    content: str
    index: int
    metadata: Dict[str, Any]


def redact_text(text: str, tokens: list[str]) -> str:
    return apply_redactions(text, tokens, default_rules())


def normalize_document(
    raw: Dict[str, Any],
    redaction_tokens: list[str] | None = None,
    extra_metadata: Dict[str, Any] | None = None,
    source_override: str | None = None,
) -> DocumentRecord:
    doc_id = str(raw.get("doc_id") or raw.get("id") or "")
    title = str(raw.get("title") or raw.get("name") or "")
    content = str(raw.get("content") or raw.get("body") or "")
    source = str(raw.get("source") or raw.get("origin") or source_override or "connector")
    mime_type = str(raw.get("mime_type") or "text/plain")
    if not doc_id or not title:
        raise ValueError("Document requires doc_id and title.")
    if not content:
        content, mime_type = _extract_content(raw, mime_type)
    redaction_tokens = redaction_tokens or []
    content = redact_text(content, redaction_tokens)
    metadata = dict(raw.get("metadata", {}) or {})
    if extra_metadata:
        metadata.update(extra_metadata)
    return DocumentRecord(
        doc_id=doc_id,
        title=title,
        content=content,
        source=source,
        mime_type=mime_type,
        metadata=metadata,
    )


def _extract_content(raw: Dict[str, Any], default_mime: str) -> tuple[str, str]:
    file_path = raw.get("file_path")
    if not file_path:
        return "", default_mime
    path = Path(str(file_path))
    if not path.exists():
        return "", default_mime

    suffix = path.suffix.lower()
    if suffix in {".txt", ".md"}:
        return path.read_text(encoding="utf-8"), "text/plain"
    if suffix == ".pdf":
        text = _extract_pdf_text(path)
        if not text.strip():
            raise ValueError(f"PDF extraction produced empty text for {path}")
        return text, _guess_mime(suffix)
    if suffix == ".docx":
        text = _extract_docx_text(path)
        if not text.strip():
            raise ValueError(f"DOCX extraction produced empty text for {path}")
        return text, _guess_mime(suffix)
    if suffix == ".xlsx":
        text = _extract_xlsx_text(path)
        if not text.strip():
            raise ValueError(f"XLSX extraction produced empty text for {path}")
        return text, _guess_mime(suffix)
    return "", default_mime


def _guess_mime(suffix: str) -> str:
    return {
        ".pdf": "application/pdf",
        ".docx": "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx": "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    }.get(suffix, "application/octet-stream")


def _extract_pdf_text(path: Path) -> str:
    try:
        from pypdf import PdfReader
    except ImportError as exc:
        raise RuntimeError("PDF extraction requires the 'pypdf' package.") from exc
    reader = PdfReader(str(path))
    parts: List[str] = []
    for page in reader.pages:
        page_text = page.extract_text() or ""
        if page_text.strip():
            parts.append(page_text.strip())
    return "\n\n".join(parts).strip()


def _extract_docx_text(path: Path) -> str:
    with zipfile.ZipFile(path) as archive:
        xml_bytes = archive.read("word/document.xml")
    root = ElementTree.fromstring(xml_bytes)
    namespace = {"w": "http://schemas.openxmlformats.org/wordprocessingml/2006/main"}
    paragraphs: List[str] = []
    for paragraph in root.findall(".//w:p", namespace):
        runs = [node.text or "" for node in paragraph.findall(".//w:t", namespace)]
        if runs:
            paragraphs.append("".join(runs))
    return "\n".join(paragraphs).strip()


def _extract_xlsx_text(path: Path) -> str:
    with zipfile.ZipFile(path) as archive:
        shared_strings: List[str] = []
        if "xl/sharedStrings.xml" in archive.namelist():
            shared_xml = archive.read("xl/sharedStrings.xml")
            root = ElementTree.fromstring(shared_xml)
            namespace = {"x": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"}
            for si in root.findall(".//x:si", namespace):
                texts = [node.text or "" for node in si.findall(".//x:t", namespace)]
                shared_strings.append("".join(texts))
        namespace = {"x": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"}
        sheet_texts: List[str] = []
        for name in archive.namelist():
            if not name.startswith("xl/worksheets/sheet") or not name.endswith(".xml"):
                continue
            sheet_root = ElementTree.fromstring(archive.read(name))
            rows: List[str] = []
            for row in sheet_root.findall(".//x:row", namespace):
                cells: List[str] = []
                for cell in row.findall("x:c", namespace):
                    cell_type = cell.get("t")
                    value_node = cell.find("x:v", namespace)
                    if value_node is None or value_node.text is None:
                        cells.append("")
                        continue
                    raw_value = value_node.text
                    if cell_type == "s":
                        try:
                            index = int(raw_value)
                        except ValueError:
                            index = -1
                        cell_value = shared_strings[index] if 0 <= index < len(shared_strings) else ""
                    else:
                        cell_value = raw_value
                    cells.append(cell_value)
                if cells:
                    rows.append("	".join(cells).rstrip())
            if rows:
                sheet_texts.append("\n".join(rows).strip())
    return "\n\n".join(text for text in sheet_texts if text).strip()


def chunk_document(
    record: DocumentRecord,
    chunk_size: int,
    overlap: int,
) -> Iterable[DocumentChunk]:
    if chunk_size <= 0:
        raise ValueError("chunk_size must be positive")
    if overlap < 0:
        raise ValueError("overlap must be non-negative")
    if not record.content:
        return []

    chunks: List[DocumentChunk] = []
    step = max(1, chunk_size - overlap)
    start = 0
    index = 0
    while start < len(record.content):
        end = min(len(record.content), start + chunk_size)
        chunk_text = record.content[start:end]
        chunk_id = f"{record.doc_id}:{index}"
        metadata = dict(record.metadata or {})
        metadata.update({"chunk_index": index})
        chunks.append(
            DocumentChunk(
                chunk_id=chunk_id,
                doc_id=record.doc_id,
                content=chunk_text,
                index=index,
                metadata=metadata,
            )
        )
        index += 1
        start += step
    return chunks
