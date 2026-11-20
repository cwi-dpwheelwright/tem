import zipfile
from io import BytesIO

from cwi_etl.parsers import DocxParser, MarkdownParser, TextParser, extract_docx_text


DOCX_CONTENT_TYPES = """<?xml version="1.0" encoding="UTF-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
</Types>
"""

DOCX_RELS = """<?xml version="1.0" encoding="UTF-8"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
</Relationships>
"""


def _docx_xml(text: str) -> str:
    return f"""<?xml version="1.0" encoding="UTF-8"?>
<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
  <w:body>
    <w:p><w:r><w:t>{text}</w:t></w:r></w:p>
  </w:body>
</w:document>
"""


def test_text_parser(tmp_path) -> None:
    path = tmp_path / "sample.txt"
    path.write_text("hello", encoding="utf-8")
    parser = TextParser()
    parsed = parser.parse(str(path))
    assert parsed.text == "hello"
    assert parsed.mime_type == "text/plain"


def test_markdown_parser(tmp_path) -> None:
    path = tmp_path / "sample.md"
    path.write_text("# Title\nSome *text*", encoding="utf-8")
    parser = MarkdownParser()
    parsed = parser.parse(str(path))
    assert "Title" in parsed.text
    assert "text" in parsed.text


def test_docx_parser(tmp_path) -> None:
    path = tmp_path / "sample.docx"
    with zipfile.ZipFile(path, "w") as archive:
        archive.writestr("[Content_Types].xml", DOCX_CONTENT_TYPES)
        archive.writestr("_rels/.rels", DOCX_RELS)
        archive.writestr("word/document.xml", _docx_xml("Hello DOCX"))
    parser = DocxParser()
    parsed = parser.parse(str(path))
    assert "Hello DOCX" in parsed.text


def test_extract_docx_text() -> None:
    payload = BytesIO()
    with zipfile.ZipFile(payload, "w") as archive:
        archive.writestr("[Content_Types].xml", DOCX_CONTENT_TYPES)
        archive.writestr("_rels/.rels", DOCX_RELS)
        archive.writestr("word/document.xml", _docx_xml("Hello"))
    text = extract_docx_text(payload.getvalue())
    assert text == "Hello"
