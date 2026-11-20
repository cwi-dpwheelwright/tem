from __future__ import annotations

import io
import os
import re
import zipfile
from dataclasses import dataclass, field
from typing import Dict, Iterable, List, Optional
from xml.etree import ElementTree


@dataclass(frozen=True)
class ParsedDocument:
    doc_id: str
    title: str
    mime_type: str
    text: str
    source_path: str
    metadata: Dict[str, str] = field(default_factory=dict)


class DocumentParser:
    supported_extensions: Iterable[str] = ()
    supported_mime_types: Iterable[str] = ()

    def can_parse_path(self, path: str) -> bool:
        ext = os.path.splitext(path)[1].lower()
        return ext in self.supported_extensions

    def parse(self, path: str) -> ParsedDocument:
        raise NotImplementedError


class ParserRegistry:
    def __init__(self) -> None:
        self._parsers: List[DocumentParser] = []

    def register(self, parser: DocumentParser) -> None:
        self._parsers.append(parser)

    def parser_for_path(self, path: str) -> Optional[DocumentParser]:
        for parser in self._parsers:
            if parser.can_parse_path(path):
                return parser
        return None

    def parse(self, path: str, doc_id: Optional[str] = None) -> ParsedDocument:
        parser = self.parser_for_path(path)
        if not parser:
            raise ValueError(f"No parser registered for {path}")
        parsed = parser.parse(path)
        if doc_id:
            return ParsedDocument(
                doc_id=doc_id,
                title=parsed.title,
                mime_type=parsed.mime_type,
                text=parsed.text,
                source_path=parsed.source_path,
                metadata=parsed.metadata,
            )
        return parsed


def _read_text(path: str) -> str:
    with open(path, "r", encoding="utf-8") as handle:
        return handle.read()


def _strip_markdown(text: str) -> str:
    text = re.sub(r"```[\s\S]*?```", " ", text)
    text = re.sub(r"`([^`]+)`", r"\1", text)
    text = re.sub(r"^#{1,6}\s+", "", text, flags=re.MULTILINE)
    text = re.sub(r"\*\*([^*]+)\*\*", r"\1", text)
    text = re.sub(r"\*([^*]+)\*", r"\1", text)
    text = re.sub(r"_([^_]+)_", r"\1", text)
    text = re.sub(r"\[(.*?)\]\([^)]*\)", r"\1", text)
    return text


class TextParser(DocumentParser):
    supported_extensions = {".txt"}

    def parse(self, path: str) -> ParsedDocument:
        text = _read_text(path)
        title = os.path.basename(path)
        return ParsedDocument(
            doc_id=os.path.splitext(title)[0],
            title=title,
            mime_type="text/plain",
            text=text,
            source_path=path,
        )


class MarkdownParser(DocumentParser):
    supported_extensions = {".md", ".markdown"}

    def parse(self, path: str) -> ParsedDocument:
        raw = _read_text(path)
        text = _strip_markdown(raw)
        title = os.path.basename(path)
        return ParsedDocument(
            doc_id=os.path.splitext(title)[0],
            title=title,
            mime_type="text/markdown",
            text=text,
            source_path=path,
        )


class DocxParser(DocumentParser):
    supported_extensions = {".docx"}

    def parse(self, path: str) -> ParsedDocument:
        with open(path, "rb") as handle:
            data = handle.read()
        text = extract_docx_text(data)
        title = os.path.basename(path)
        return ParsedDocument(
            doc_id=os.path.splitext(title)[0],
            title=title,
            mime_type="application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            text=text,
            source_path=path,
        )


def extract_docx_text(data: bytes) -> str:
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        xml_bytes = archive.read("word/document.xml")
    root = ElementTree.fromstring(xml_bytes)
    namespace = {"w": "http://schemas.openxmlformats.org/wordprocessingml/2006/main"}
    paragraphs: List[str] = []
    for paragraph in root.findall(".//w:p", namespace):
        runs = [node.text or "" for node in paragraph.findall(".//w:t", namespace)]
        if runs:
            paragraphs.append("".join(runs))
    return "\n".join(paragraphs).strip()


def default_registry() -> ParserRegistry:
    registry = ParserRegistry()
    registry.register(TextParser())
    registry.register(MarkdownParser())
    registry.register(DocxParser())
    return registry
