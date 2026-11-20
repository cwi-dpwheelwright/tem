import { describe, expect, it } from "vitest";
import {
  MAX_CHAT_HISTORY_IMPORT_FILE_BYTES,
  MAX_DATASET_UPLOAD_FILE_BYTES,
  buildChatHistoryIngestRequests,
  buildDatasetDocId,
  buildDatasetIngestRequest,
  isChatHistoryImportFile,
  isDatasetUploadFile,
} from "@/lib/dataset-ingest";

describe("dataset-ingest", () => {
  function buildStoredZip(entries: Array<{ name: string; content: string }>): Uint8Array {
    const encoder = new TextEncoder();
    const localParts: Uint8Array[] = [];
    const centralParts: Uint8Array[] = [];
    let offset = 0;

    const writeUint16 = (view: DataView, position: number, value: number) => view.setUint16(position, value, true);
    const writeUint32 = (view: DataView, position: number, value: number) => view.setUint32(position, value, true);

    for (const entry of entries) {
      const nameBytes = encoder.encode(entry.name);
      const contentBytes = encoder.encode(entry.content);
      const localHeader = new Uint8Array(30 + nameBytes.length);
      const localView = new DataView(localHeader.buffer);
      writeUint32(localView, 0, 0x04034b50);
      writeUint16(localView, 4, 20);
      writeUint16(localView, 6, 0);
      writeUint16(localView, 8, 0);
      writeUint16(localView, 10, 0);
      writeUint16(localView, 12, 0);
      writeUint32(localView, 14, 0);
      writeUint32(localView, 18, contentBytes.length);
      writeUint32(localView, 22, contentBytes.length);
      writeUint16(localView, 26, nameBytes.length);
      writeUint16(localView, 28, 0);
      localHeader.set(nameBytes, 30);
      localParts.push(localHeader, contentBytes);

      const centralHeader = new Uint8Array(46 + nameBytes.length);
      const centralView = new DataView(centralHeader.buffer);
      writeUint32(centralView, 0, 0x02014b50);
      writeUint16(centralView, 4, 20);
      writeUint16(centralView, 6, 20);
      writeUint16(centralView, 8, 0);
      writeUint16(centralView, 10, 0);
      writeUint16(centralView, 12, 0);
      writeUint16(centralView, 14, 0);
      writeUint32(centralView, 16, 0);
      writeUint32(centralView, 20, contentBytes.length);
      writeUint32(centralView, 24, contentBytes.length);
      writeUint16(centralView, 28, nameBytes.length);
      writeUint16(centralView, 30, 0);
      writeUint16(centralView, 32, 0);
      writeUint16(centralView, 34, 0);
      writeUint16(centralView, 36, 0);
      writeUint32(centralView, 38, 0);
      writeUint32(centralView, 42, offset);
      centralHeader.set(nameBytes, 46);
      centralParts.push(centralHeader);

      offset += localHeader.length + contentBytes.length;
    }

    const centralDirectoryOffset = offset;
    const centralDirectorySize = centralParts.reduce((sum, part) => sum + part.length, 0);
    const eocd = new Uint8Array(22);
    const eocdView = new DataView(eocd.buffer);
    writeUint32(eocdView, 0, 0x06054b50);
    writeUint16(eocdView, 8, entries.length);
    writeUint16(eocdView, 10, entries.length);
    writeUint32(eocdView, 12, centralDirectorySize);
    writeUint32(eocdView, 16, centralDirectoryOffset);
    writeUint16(eocdView, 20, 0);

    const totalLength = centralDirectoryOffset + centralDirectorySize + eocd.length;
    const archive = new Uint8Array(totalLength);
    let cursor = 0;
    for (const part of [...localParts, ...centralParts, eocd]) {
      archive.set(part, cursor);
      cursor += part.length;
    }
    return archive;
  }

  function buildZip64StoredZip(entries: Array<{ name: string; content: string }>): Uint8Array {
    const encoder = new TextEncoder();
    const localParts: Uint8Array[] = [];
    const centralParts: Uint8Array[] = [];
    let offset = 0;

    const writeUint16 = (view: DataView, position: number, value: number) => view.setUint16(position, value, true);
    const writeUint32 = (view: DataView, position: number, value: number) => view.setUint32(position, value, true);
    const writeUint64 = (view: DataView, position: number, value: number) => view.setBigUint64(position, BigInt(value), true);

    for (const entry of entries) {
      const nameBytes = encoder.encode(entry.name);
      const contentBytes = encoder.encode(entry.content);
      const localHeader = new Uint8Array(30 + nameBytes.length);
      const localView = new DataView(localHeader.buffer);
      writeUint32(localView, 0, 0x04034b50);
      writeUint16(localView, 4, 45);
      writeUint16(localView, 6, 0);
      writeUint16(localView, 8, 0);
      writeUint16(localView, 10, 0);
      writeUint16(localView, 12, 0);
      writeUint32(localView, 14, 0);
      writeUint32(localView, 18, contentBytes.length);
      writeUint32(localView, 22, contentBytes.length);
      writeUint16(localView, 26, nameBytes.length);
      writeUint16(localView, 28, 0);
      localHeader.set(nameBytes, 30);
      localParts.push(localHeader, contentBytes);

      const zip64Extra = new Uint8Array(28);
      const zip64ExtraView = new DataView(zip64Extra.buffer);
      writeUint16(zip64ExtraView, 0, 0x0001);
      writeUint16(zip64ExtraView, 2, 24);
      writeUint64(zip64ExtraView, 4, contentBytes.length);
      writeUint64(zip64ExtraView, 12, contentBytes.length);
      writeUint64(zip64ExtraView, 20, offset);

      const centralHeader = new Uint8Array(46 + nameBytes.length + zip64Extra.length);
      const centralView = new DataView(centralHeader.buffer);
      writeUint32(centralView, 0, 0x02014b50);
      writeUint16(centralView, 4, 45);
      writeUint16(centralView, 6, 45);
      writeUint16(centralView, 8, 0);
      writeUint16(centralView, 10, 0);
      writeUint16(centralView, 12, 0);
      writeUint16(centralView, 14, 0);
      writeUint32(centralView, 16, 0);
      writeUint32(centralView, 20, 0xffffffff);
      writeUint32(centralView, 24, 0xffffffff);
      writeUint16(centralView, 28, nameBytes.length);
      writeUint16(centralView, 30, zip64Extra.length);
      writeUint16(centralView, 32, 0);
      writeUint16(centralView, 34, 0);
      writeUint16(centralView, 36, 0);
      writeUint32(centralView, 38, 0);
      writeUint32(centralView, 42, 0xffffffff);
      centralHeader.set(nameBytes, 46);
      centralHeader.set(zip64Extra, 46 + nameBytes.length);
      centralParts.push(centralHeader);

      offset += localHeader.length + contentBytes.length;
    }

    const centralDirectoryOffset = offset;
    const centralDirectorySize = centralParts.reduce((sum, part) => sum + part.length, 0);
    const zip64EocdOffset = centralDirectoryOffset + centralDirectorySize;
    const zip64Eocd = new Uint8Array(56);
    const zip64EocdView = new DataView(zip64Eocd.buffer);
    writeUint32(zip64EocdView, 0, 0x06064b50);
    writeUint64(zip64EocdView, 4, 44);
    writeUint16(zip64EocdView, 12, 45);
    writeUint16(zip64EocdView, 14, 45);
    writeUint32(zip64EocdView, 16, 0);
    writeUint32(zip64EocdView, 20, 0);
    writeUint64(zip64EocdView, 24, entries.length);
    writeUint64(zip64EocdView, 32, entries.length);
    writeUint64(zip64EocdView, 40, centralDirectorySize);
    writeUint64(zip64EocdView, 48, centralDirectoryOffset);

    const zip64Locator = new Uint8Array(20);
    const zip64LocatorView = new DataView(zip64Locator.buffer);
    writeUint32(zip64LocatorView, 0, 0x07064b50);
    writeUint32(zip64LocatorView, 4, 0);
    writeUint64(zip64LocatorView, 8, zip64EocdOffset);
    writeUint32(zip64LocatorView, 16, 1);

    const eocd = new Uint8Array(22);
    const eocdView = new DataView(eocd.buffer);
    writeUint32(eocdView, 0, 0x06054b50);
    writeUint16(eocdView, 8, 0xffff);
    writeUint16(eocdView, 10, 0xffff);
    writeUint32(eocdView, 12, 0xffffffff);
    writeUint32(eocdView, 16, 0xffffffff);
    writeUint16(eocdView, 20, 0);

    const totalLength = zip64EocdOffset + zip64Eocd.length + zip64Locator.length + eocd.length;
    const archive = new Uint8Array(totalLength);
    let cursor = 0;
    for (const part of [...localParts, ...centralParts, zip64Eocd, zip64Locator, eocd]) {
      archive.set(part, cursor);
      cursor += part.length;
    }
    return archive;
  }

  function buildFileLike(name: string, type: string, bytes: Uint8Array): File {
    const decoder = new TextDecoder();
    return {
      name,
      type,
      size: bytes.byteLength,
      lastModified: Date.parse("2026-03-31T10:15:00Z"),
      text: async () => decoder.decode(bytes),
      slice: (start?: number, end?: number) => {
        const safeStart = start ?? 0;
        const safeEnd = end ?? bytes.byteLength;
        const sliced = bytes.slice(safeStart, safeEnd);
        return {
          arrayBuffer: async () => sliced.buffer.slice(sliced.byteOffset, sliced.byteOffset + sliced.byteLength),
          text: async () => decoder.decode(sliced),
          size: sliced.byteLength,
          type,
        } as Blob;
      },
    } as File;
  }

  it("accepts text-like dataset files", () => {
    expect(isDatasetUploadFile("chat-export.json", "application/json")).toBe(true);
    expect(isChatHistoryImportFile("chat-export.json", "application/json")).toBe(true);
    expect(isChatHistoryImportFile("chat-export.zip", "application/zip")).toBe(true);
    expect(isChatHistoryImportFile("screenshot.png", "image/png")).toBe(true);
    expect(isDatasetUploadFile("notes.md", "text/markdown")).toBe(true);
    expect(isDatasetUploadFile("snapshot.bin", "application/octet-stream")).toBe(false);
  });

  it("builds a stable document id from file metadata", () => {
    expect(buildDatasetDocId("Chat Export.json", "batch-01", 2)).toBe("web-upload-batch-01-003-chat-export-json");
  });

  it("normalizes structured chat exports for ingest", async () => {
    const body = JSON.stringify({
      mapping: {
        root: {
          create_time: 1,
          message: {
            author: { role: "user" },
            content: { parts: ["Summarize this outage."] },
          },
        },
        reply: {
          create_time: 2,
          message: {
            author: { role: "assistant" },
            content: { parts: ["The outage lasted 14 minutes."] },
          },
        },
      },
    });
    const file = {
      name: "chat-export.json",
      type: "application/json",
      size: body.length,
      lastModified: Date.parse("2026-03-31T10:15:00Z"),
      text: async () => body,
    } as File;

    const request = await buildDatasetIngestRequest(file, {
      tenantId: "tenant-1",
      userId: "user-1",
      uploadBatchId: "batch-xyz",
      index: 0,
    });

    expect(request.tenant_id).toBe("tenant-1");
    expect(request.user_id).toBe("user-1");
    expect(request.tags).toContain("dataset-upload");
    expect(request.tags).toContain("chat_history");
    expect(request.document.doc_id).toBe("web-upload-batch-xyz-001-chat-export-json");
    expect(request.document.content).toContain("User: Summarize this outage.");
    expect(request.document.content).toContain("Assistant: The outage lasted 14 minutes.");
    expect(request.document.metadata?.message_count).toBe(2);
  });

  it("uses a 20 MB guardrail for dataset uploads", () => {
    expect(MAX_DATASET_UPLOAD_FILE_BYTES).toBe(20 * 1024 * 1024);
  });

  it("supports multi-gigabyte chat history imports via chunked ingest", () => {
    expect(MAX_CHAT_HISTORY_IMPORT_FILE_BYTES).toBe(8 * 1024 * 1024 * 1024);
  });

  it("splits chat history imports into multiple ingest documents", async () => {
    const body =
      "User: Started a new migration.\n\nAssistant: Captured the schema diff.\n\nUser: Continue the migration notes.\n\nAssistant: Added the rollback plan.\n";
    const file = {
      name: "chat-history.txt",
      type: "text/plain",
      size: body.length,
      lastModified: Date.parse("2026-03-31T10:15:00Z"),
      text: async () => body,
    } as File;

    const parts = [];
    for await (const part of buildChatHistoryIngestRequests(file, {
      tenantId: "tenant-1",
      userId: "user-1",
      uploadBatchId: "batch-xyz",
      index: 0,
      chunkChars: 55,
    })) {
      parts.push(part);
    }

    expect(parts.length).toBeGreaterThan(1);
    expect(parts[0].request.user_id).toBe("user-1");
    expect(parts[0].request.document.doc_id).toContain("part-00001");
    expect(parts[1].request.document.doc_id).toContain("part-00002");
    expect(parts[0].request.document.metadata?.normalization_mode).toBe("raw_chunked");
  });

  it("normalizes small structured chat history imports before chunking", async () => {
    const body = JSON.stringify({
      mapping: {
        root: {
          create_time: 1,
          message: {
            author: { role: "user" },
            content: { parts: ["What changed in the deployment?"] },
          },
        },
        reply: {
          create_time: 2,
          message: {
            author: { role: "assistant" },
            content: { parts: ["Auth was restored and chat import was chunked."] },
          },
        },
      },
    });
    const file = {
      name: "chat-export.json",
      type: "application/json",
      size: body.length,
      lastModified: Date.parse("2026-03-31T10:15:00Z"),
      text: async () => body,
    } as File;

    const parts = [];
    for await (const part of buildChatHistoryIngestRequests(file, {
      tenantId: "tenant-1",
      userId: "user-1",
      uploadBatchId: "batch-xyz",
      index: 0,
      chunkChars: 500,
    })) {
      parts.push(part);
    }

    expect(parts).toHaveLength(1);
    expect(parts[0].normalizationMode).toBe("structured_transcript");
    expect(parts[0].request.user_id).toBe("user-1");
    expect(parts[0].request.document.content).toContain("User: What changed in the deployment?");
    expect(parts[0].request.document.content).toContain("Assistant: Auth was restored and chat import was chunked.");
  });

  it("extracts supported text files from ZIP chat exports", async () => {
    const archive = buildStoredZip([
      {
        name: "conversations.json",
        content: JSON.stringify({
          mapping: {
            root: {
              create_time: 1,
              message: {
                author: { role: "user" },
                content: { parts: ["What did we decide?"] },
              },
            },
            reply: {
              create_time: 2,
              message: {
                author: { role: "assistant" },
                content: { parts: ["Use account-level imports."] },
              },
            },
          },
        }),
      },
      { name: "assets/screenshot.png", content: "not really an image" },
      { name: "notes/readme.md", content: "Follow-up notes from the imported archive." },
    ]);
    const file = buildFileLike("chat-export.zip", "application/zip", archive);

    const parts = [];
    for await (const part of buildChatHistoryIngestRequests(file, {
      tenantId: "tenant-1",
      userId: "user-1",
      uploadBatchId: "batch-xyz",
      index: 0,
      chunkChars: 500,
    })) {
      parts.push(part);
    }

    expect(parts.length).toBeGreaterThanOrEqual(2);
    expect(parts.map((part) => part.request.document.title).join("\n")).toContain("chat-export.zip/conversations.json");
    expect(parts.map((part) => part.request.document.title).join("\n")).toContain("chat-export.zip/assets/screenshot.png");
    expect(parts.map((part) => part.request.document.content).join("\n")).toContain("User: What did we decide?");
    expect(parts.map((part) => part.request.document.content).join("\n")).toContain("Follow-up notes from the imported archive.");
    expect(parts.map((part) => part.request.document.content).join("\n")).toContain("No OCR or visual description was extracted");
  });

  it("extracts supported text files from ZIP64 chat exports", async () => {
    const archive = buildZip64StoredZip([
      {
        name: "conversations.json",
        content: JSON.stringify({
          mapping: {
            root: {
              create_time: 1,
              message: {
                author: { role: "user" },
                content: { parts: ["Why did the large export fail?"] },
              },
            },
            reply: {
              create_time: 2,
              message: {
                author: { role: "assistant" },
                content: { parts: ["The ZIP64 central directory needs to be parsed."] },
              },
            },
          },
        }),
      },
    ]);
    const file = buildFileLike("chat-export-large.zip", "application/zip", archive);

    const parts = [];
    for await (const part of buildChatHistoryIngestRequests(file, {
      tenantId: "tenant-1",
      userId: "user-1",
      uploadBatchId: "batch-xyz",
      index: 0,
      chunkChars: 500,
    })) {
      parts.push(part);
    }

    expect(parts).toHaveLength(1);
    expect(parts[0].request.document.title).toContain("chat-export-large.zip/conversations.json");
    expect(parts[0].request.document.content).toContain("User: Why did the large export fail?");
    expect(parts[0].request.document.content).toContain("Assistant: The ZIP64 central directory needs to be parsed.");
  });

  it("imports standalone images as metadata documents", async () => {
    const bytes = new TextEncoder().encode("fake-image-bytes");
    const file = buildFileLike("image.png", "image/png", bytes);

    const parts = [];
    for await (const part of buildChatHistoryIngestRequests(file, {
      tenantId: "tenant-1",
      userId: "user-1",
      uploadBatchId: "batch-xyz",
      index: 0,
      chunkChars: 500,
    })) {
      parts.push(part);
    }

    expect(parts).toHaveLength(1);
    expect(parts[0].request.document.mime_type).toBe("image/png");
    expect(parts[0].request.document.content).toContain("Image imported into account chat history: image.png.");
    expect(parts[0].request.document.content).toContain("No OCR or visual description was extracted");
  });
});
