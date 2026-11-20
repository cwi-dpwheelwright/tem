import type { IngestDocumentRequest } from "@/lib/api/types";
import { isTextLikeFile, parseImportedChatHistory } from "@/lib/prompt-composer";

export const MAX_DATASET_UPLOAD_FILE_BYTES = 20 * 1024 * 1024;
export const MAX_CHAT_HISTORY_IMPORT_FILE_BYTES = 8 * 1024 * 1024 * 1024;
export const MAX_CHAT_HISTORY_WHOLE_FILE_BYTES = 8 * 1024 * 1024;
export const CHAT_HISTORY_IMPORT_CHUNK_CHARS = 500_000;
export const CHAT_HISTORY_IMPORT_READ_CHUNK_BYTES = 1024 * 1024;

const EXTENSION_MIME_TYPES: Record<string, string> = {
  bmp: "image/bmp",
  csv: "text/csv",
  gif: "image/gif",
  heic: "image/heic",
  heif: "image/heif",
  htm: "text/html",
  html: "text/html",
  jpeg: "image/jpeg",
  jpg: "image/jpeg",
  json: "application/json",
  jsonl: "application/json",
  log: "text/plain",
  markdown: "text/markdown",
  md: "text/markdown",
  png: "image/png",
  svg: "image/svg+xml",
  tif: "image/tiff",
  tiff: "image/tiff",
  txt: "text/plain",
  tsv: "text/tab-separated-values",
  webp: "image/webp",
  xml: "application/xml",
  yaml: "application/yaml",
  yml: "application/yaml",
  zip: "application/zip",
};

const ZIP_MIME_TYPES = new Set([
  "application/zip",
  "application/x-zip",
  "application/x-zip-compressed",
  "application/octet-stream",
  "multipart/x-zip",
]);

const ZIP_EOCD_SIGNATURE = 0x06054b50;
const ZIP_CENTRAL_DIRECTORY_SIGNATURE = 0x02014b50;
const ZIP_LOCAL_FILE_HEADER_SIGNATURE = 0x04034b50;
const ZIP64_EOCD_LOCATOR_SIGNATURE = 0x07064b50;
const ZIP64_EOCD_SIGNATURE = 0x06064b50;
const ZIP64_EXTRA_FIELD_ID = 0x0001;
const ZIP32_MAX_UINT16 = 0xffff;
const ZIP32_MAX_UINT32 = 0xffffffff;
const ZIP_MAX_EOCD_SEARCH_BYTES = 66_000;

const IMAGE_EXTENSIONS = new Set([
  "bmp",
  "gif",
  "heic",
  "heif",
  "jpeg",
  "jpg",
  "png",
  "svg",
  "tif",
  "tiff",
  "webp",
]);

export type DatasetImportKind = "chat_history" | "document";
export type ChatHistoryNormalizationMode = "structured_transcript" | "raw_chunked";

export interface DatasetIngestBuildOptions {
  tenantId: string;
  userId: string;
  uploadBatchId: string;
  index: number;
}

export interface ChatHistoryIngestBuildOptions extends DatasetIngestBuildOptions {
  chunkChars?: number;
}

export interface ChatHistoryIngestRequestPart {
  request: IngestDocumentRequest;
  chunkIndex: number;
  normalizationMode: ChatHistoryNormalizationMode;
}

interface ImportableFileLike {
  name: string;
  type: string;
  size: number;
  lastModified: number;
  text(): Promise<string>;
  slice(start?: number, end?: number): Blob;
}

interface ZipEntryMetadata {
  name: string;
  compressedSize: number;
  uncompressedSize: number;
  compressionMethod: number;
  generalPurposeFlag: number;
  localHeaderOffset: number;
}

function readZipUint64(view: DataView, offset: number, label: string): number {
  const value = Number(view.getBigUint64(offset, true));
  if (!Number.isSafeInteger(value)) {
    throw new Error(`This ZIP export is too large to import safely: ${label}.`);
  }
  return value;
}

async function readZip64CentralDirectoryInfo(
  file: File,
  eocdAbsoluteOffset: number,
): Promise<{ entryCount: number; centralDirectorySize: number; centralDirectoryOffset: number } | null> {
  const locatorOffset = eocdAbsoluteOffset - 20;
  if (locatorOffset < 0) {
    return null;
  }

  const locatorBuffer = await file.slice(locatorOffset, locatorOffset + 20).arrayBuffer();
  if (locatorBuffer.byteLength < 20) {
    return null;
  }

  const locatorView = new DataView(locatorBuffer);
  if (locatorView.getUint32(0, true) !== ZIP64_EOCD_LOCATOR_SIGNATURE) {
    return null;
  }

  const zip64EocdOffset = readZipUint64(locatorView, 8, "ZIP64 central directory locator");
  const zip64HeaderBuffer = await file.slice(zip64EocdOffset, zip64EocdOffset + 56).arrayBuffer();
  if (zip64HeaderBuffer.byteLength < 56) {
    throw new Error("This ZIP64 export is missing its central directory record.");
  }

  const zip64HeaderView = new DataView(zip64HeaderBuffer);
  if (zip64HeaderView.getUint32(0, true) !== ZIP64_EOCD_SIGNATURE) {
    throw new Error("This ZIP64 export has an invalid central directory record.");
  }

  return {
    entryCount: readZipUint64(zip64HeaderView, 32, "ZIP64 entry count"),
    centralDirectorySize: readZipUint64(zip64HeaderView, 40, "ZIP64 central directory size"),
    centralDirectoryOffset: readZipUint64(zip64HeaderView, 48, "ZIP64 central directory offset"),
  };
}

function resolveZip64EntryFieldValues(
  centralView: DataView,
  extraStart: number,
  extraFieldLength: number,
  compressedSize: number,
  uncompressedSize: number,
  localHeaderOffset: number,
): { compressedSize: number; uncompressedSize: number; localHeaderOffset: number } {
  let resolvedCompressedSize = compressedSize;
  let resolvedUncompressedSize = uncompressedSize;
  let resolvedLocalHeaderOffset = localHeaderOffset;
  let offset = extraStart;
  const extraEnd = extraStart + extraFieldLength;

  while (offset + 4 <= extraEnd) {
    const headerId = centralView.getUint16(offset, true);
    const fieldSize = centralView.getUint16(offset + 2, true);
    let valueOffset = offset + 4;
    const fieldEnd = valueOffset + fieldSize;
    if (fieldEnd > extraEnd) {
      break;
    }

    if (headerId === ZIP64_EXTRA_FIELD_ID) {
      if (uncompressedSize === ZIP32_MAX_UINT32 && valueOffset + 8 <= fieldEnd) {
        resolvedUncompressedSize = readZipUint64(centralView, valueOffset, "ZIP64 uncompressed entry size");
        valueOffset += 8;
      }
      if (compressedSize === ZIP32_MAX_UINT32 && valueOffset + 8 <= fieldEnd) {
        resolvedCompressedSize = readZipUint64(centralView, valueOffset, "ZIP64 compressed entry size");
        valueOffset += 8;
      }
      if (localHeaderOffset === ZIP32_MAX_UINT32 && valueOffset + 8 <= fieldEnd) {
        resolvedLocalHeaderOffset = readZipUint64(centralView, valueOffset, "ZIP64 local header offset");
      }
      break;
    }

    offset = fieldEnd;
  }

  return {
    compressedSize: resolvedCompressedSize,
    uncompressedSize: resolvedUncompressedSize,
    localHeaderOffset: resolvedLocalHeaderOffset,
  };
}

function getExtension(name: string): string {
  if (!name.includes(".")) {
    return "";
  }
  return name.split(".").pop()?.trim().toLowerCase() ?? "";
}

function sanitizeDocIdSegment(value: string): string {
  const normalized = value
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");

  return normalized || "upload";
}

function inferMimeType(name: string, mimeType: string | null | undefined): string {
  if (mimeType?.trim()) {
    return mimeType.trim();
  }
  return EXTENSION_MIME_TYPES[getExtension(name)] ?? "text/plain";
}

function isZipFile(name: string, mimeType: string | null | undefined): boolean {
  return getExtension(name) === "zip" || (mimeType ? ZIP_MIME_TYPES.has(mimeType.trim().toLowerCase()) : false);
}

function isImageFile(name: string, mimeType: string | null | undefined): boolean {
  const normalizedType = mimeType?.trim().toLowerCase() ?? "";
  return normalizedType.startsWith("image/") || IMAGE_EXTENSIONS.has(getExtension(name));
}

function isMacOsMetadataEntry(name: string): boolean {
  return name.split("/").some((segment) => segment === "__MACOSX" || segment.startsWith("._"));
}

function inferImportKind(fileName: string, messageCount: number | null): DatasetImportKind {
  if (messageCount !== null) {
    return "chat_history";
  }

  return /(chat|conversation|transcript|history|export)/i.test(fileName)
    ? "chat_history"
    : "document";
}

function shouldReadWholeFile(file: Pick<File, "size">): boolean {
  return file.size <= MAX_CHAT_HISTORY_WHOLE_FILE_BYTES;
}

function normalizeChunkText(value: string): string {
  return value.replace(/\u0000/g, "").replace(/\r\n/g, "\n");
}

function findSplitIndex(value: string, maxChars: number): number {
  if (value.length <= maxChars) {
    return value.length;
  }

  const searchFloor = Math.floor(maxChars * 0.6);
  const separators = ["\n\n", "\n", ". ", " "];
  for (const separator of separators) {
    const position = value.lastIndexOf(separator, maxChars);
    if (position >= searchFloor) {
      return position + separator.length;
    }
  }

  return maxChars;
}

function* splitTextIntoSegments(value: string, maxChars: number): Generator<string> {
  let remaining = normalizeChunkText(value).trim();
  while (remaining) {
    if (remaining.length <= maxChars) {
      yield remaining;
      return;
    }

    const splitIndex = findSplitIndex(remaining, maxChars);
    const segment = remaining.slice(0, splitIndex).trim();
    if (segment) {
      yield segment;
    }
    remaining = remaining.slice(splitIndex).trim();
  }
}

async function* streamBlobText(file: ImportableFileLike, readChunkBytes = CHAT_HISTORY_IMPORT_READ_CHUNK_BYTES): AsyncGenerator<string> {
  const decoder = new TextDecoder();
  let offset = 0;

  while (offset < file.size) {
    const nextOffset = Math.min(offset + readChunkBytes, file.size);
    const buffer = await file.slice(offset, nextOffset).arrayBuffer();
    offset = nextOffset;
    const decoded = decoder.decode(buffer, { stream: offset < file.size });
    if (decoded) {
      yield decoded;
    }
  }

  const tail = decoder.decode();
  if (tail) {
    yield tail;
  }
}

async function readBlobText(blob: Blob): Promise<string> {
  return new TextDecoder().decode(await blob.arrayBuffer());
}

async function inflateRawZipEntry(blob: Blob): Promise<string> {
  if (typeof DecompressionStream === "undefined") {
    throw new Error("Compressed ZIP entries are not supported by this browser.");
  }

  const stream = blob.stream().pipeThrough(new DecompressionStream("deflate-raw"));
  return readBlobText(await new Response(stream).blob());
}

function decodeZipName(bytes: Uint8Array): string {
  try {
    return new TextDecoder("utf-8", { fatal: false }).decode(bytes);
  } catch {
    return Array.from(bytes, (byte) => String.fromCharCode(byte)).join("");
  }
}

async function readZipCentralDirectory(file: File): Promise<ZipEntryMetadata[]> {
  const tailSize = Math.min(file.size, ZIP_MAX_EOCD_SEARCH_BYTES);
  const tailOffset = file.size - tailSize;
  const tailBuffer = await file.slice(tailOffset).arrayBuffer();
  const tailView = new DataView(tailBuffer);
  let eocdOffset = -1;

  for (let offset = tailView.byteLength - 22; offset >= 0; offset -= 1) {
    if (tailView.getUint32(offset, true) === ZIP_EOCD_SIGNATURE) {
      eocdOffset = offset;
      break;
    }
  }

  if (eocdOffset < 0) {
    throw new Error("This ZIP file could not be read. Please upload a standard ZIP export.");
  }

  const entryCount = tailView.getUint16(eocdOffset + 10, true);
  const centralDirectorySize = tailView.getUint32(eocdOffset + 12, true);
  const centralDirectoryOffset = tailView.getUint32(eocdOffset + 16, true);
  const zip64Info =
    entryCount === ZIP32_MAX_UINT16
    || centralDirectorySize === ZIP32_MAX_UINT32
    || centralDirectoryOffset === ZIP32_MAX_UINT32
      ? await readZip64CentralDirectoryInfo(file, tailOffset + eocdOffset)
      : null;
  if (
    (entryCount === ZIP32_MAX_UINT16
      || centralDirectorySize === ZIP32_MAX_UINT32
      || centralDirectoryOffset === ZIP32_MAX_UINT32)
    && zip64Info === null
  ) {
    throw new Error("This ZIP64 export is missing its central directory locator.");
  }

  const resolvedEntryCount = zip64Info?.entryCount ?? entryCount;
  const resolvedCentralDirectorySize = zip64Info?.centralDirectorySize ?? centralDirectorySize;
  const resolvedCentralDirectoryOffset = zip64Info?.centralDirectoryOffset ?? centralDirectoryOffset;
  const centralBuffer = await file
    .slice(resolvedCentralDirectoryOffset, resolvedCentralDirectoryOffset + resolvedCentralDirectorySize)
    .arrayBuffer();
  const centralView = new DataView(centralBuffer);
  const entries: ZipEntryMetadata[] = [];
  let offset = 0;

  for (let entryIndex = 0; entryIndex < resolvedEntryCount && offset + 46 <= centralView.byteLength; entryIndex += 1) {
    if (centralView.getUint32(offset, true) !== ZIP_CENTRAL_DIRECTORY_SIGNATURE) {
      break;
    }

    const generalPurposeFlag = centralView.getUint16(offset + 8, true);
    const compressionMethod = centralView.getUint16(offset + 10, true);
    const rawCompressedSize = centralView.getUint32(offset + 20, true);
    const rawUncompressedSize = centralView.getUint32(offset + 24, true);
    const fileNameLength = centralView.getUint16(offset + 28, true);
    const extraFieldLength = centralView.getUint16(offset + 30, true);
    const fileCommentLength = centralView.getUint16(offset + 32, true);
    const rawLocalHeaderOffset = centralView.getUint32(offset + 42, true);
    const nameStart = offset + 46;
    const nameEnd = nameStart + fileNameLength;
    const extraStart = nameEnd;
    const name = decodeZipName(new Uint8Array(centralBuffer.slice(nameStart, nameEnd)));
    const resolved = resolveZip64EntryFieldValues(
      centralView,
      extraStart,
      extraFieldLength,
      rawCompressedSize,
      rawUncompressedSize,
      rawLocalHeaderOffset,
    );

    entries.push({
      name,
      compressedSize: resolved.compressedSize,
      uncompressedSize: resolved.uncompressedSize,
      compressionMethod,
      generalPurposeFlag,
      localHeaderOffset: resolved.localHeaderOffset,
    });

    offset = nameEnd + extraFieldLength + fileCommentLength;
  }

  return entries;
}

async function readZipEntryText(file: File, entry: ZipEntryMetadata): Promise<string> {
  const localHeaderBuffer = await file.slice(entry.localHeaderOffset, entry.localHeaderOffset + 30).arrayBuffer();
  const localHeaderView = new DataView(localHeaderBuffer);
  if (localHeaderView.getUint32(0, true) !== ZIP_LOCAL_FILE_HEADER_SIGNATURE) {
    throw new Error(`Unable to read ${entry.name} from the ZIP export.`);
  }

  if ((entry.generalPurposeFlag & 0x1) === 0x1) {
    throw new Error(`Encrypted ZIP entry skipped: ${entry.name}`);
  }

  const fileNameLength = localHeaderView.getUint16(26, true);
  const extraFieldLength = localHeaderView.getUint16(28, true);
  const dataStart = entry.localHeaderOffset + 30 + fileNameLength + extraFieldLength;
  const dataBlob = file.slice(dataStart, dataStart + entry.compressedSize);

  if (entry.compressionMethod === 0) {
    return readBlobText(dataBlob);
  }

  if (entry.compressionMethod === 8) {
    return inflateRawZipEntry(dataBlob);
  }

  throw new Error(`Unsupported ZIP compression method ${entry.compressionMethod} for ${entry.name}.`);
}

async function* extractZipImportableFiles(file: File): AsyncGenerator<ImportableFileLike> {
  const entries = await readZipCentralDirectory(file);
  if (entries.length === 0) {
    throw new Error("No files were found inside this ZIP archive.");
  }

  let yieldedEntries = 0;
  for (const entry of entries) {
    if (!entry.name || entry.name.endsWith("/") || entry.uncompressedSize === 0 || isMacOsMetadataEntry(entry.name)) {
      continue;
    }

    const mimeType = inferMimeType(entry.name, null);
    const isTextEntry = isDatasetUploadFile(entry.name, null);
    const isImageEntry = isImageFile(entry.name, mimeType);
    if (!isTextEntry && !isImageEntry) {
      continue;
    }

    const text = isImageEntry
      ? buildImageImportContent(entry.name, mimeType, entry.uncompressedSize, file.name)
      : await readZipEntryText(file, entry);
    if (!text.trim()) {
      continue;
    }

    const entryName = `${file.name}/${entry.name}`;
    yieldedEntries += 1;
    yield {
      name: entryName,
      type: mimeType,
      size: entry.uncompressedSize,
      lastModified: file.lastModified,
      text: async () => text,
      slice: (start?: number, end?: number) => new Blob([text]).slice(start, end),
    };
  }

  if (yieldedEntries === 0) {
    throw new Error("This ZIP archive did not contain supported text, JSON, image, or transcript files.");
  }
}

function buildImageImportContent(fileName: string, mimeType: string, size: number, archiveName?: string): string {
  const sourceLine = archiveName
    ? `Image imported from ZIP archive: ${archiveName}/${fileName}.`
    : `Image imported into account chat history: ${fileName}.`;

  return [
    sourceLine,
    `MIME type: ${mimeType || "image/unknown"}.`,
    `File size: ${size} bytes.`,
    "No OCR or visual description was extracted during this import. Use prompt image upload when you need the model to inspect the image contents directly.",
  ].join("\n");
}

async function* streamTextSegments(file: ImportableFileLike, maxChars: number): AsyncGenerator<string> {
  let buffer = "";

  for await (const chunk of streamBlobText(file)) {
    buffer += normalizeChunkText(chunk);
    while (buffer.trim().length > maxChars) {
      const splitIndex = findSplitIndex(buffer, maxChars);
      const segment = buffer.slice(0, splitIndex).trim();
      if (segment) {
        yield segment;
      }
      buffer = buffer.slice(splitIndex).trim();
    }
  }

  const tail = buffer.trim();
  if (tail) {
    yield tail;
  }
}

function buildChunkDocId(fileName: string, uploadBatchId: string, fileIndex: number, chunkIndex: number): string {
  return [
    "web-upload",
    sanitizeDocIdSegment(uploadBatchId),
    `file-${String(fileIndex + 1).padStart(3, "0")}`,
    sanitizeDocIdSegment(fileName),
    `part-${String(chunkIndex + 1).padStart(5, "0")}`,
  ].join("-");
}

function buildChunkTitle(fileName: string, chunkIndex: number, totalChunks: number | null): string {
  if (totalChunks !== null) {
    return `${fileName} (Part ${chunkIndex + 1} of ${totalChunks})`;
  }
  return `${fileName} (Part ${chunkIndex + 1})`;
}

function buildChunkRequest(
  file: Pick<File, "name" | "type" | "size" | "lastModified">,
  options: ChatHistoryIngestBuildOptions,
  content: string,
  importKind: DatasetImportKind,
  chunkIndex: number,
  totalChunks: number | null,
  normalizationMode: ChatHistoryNormalizationMode,
  messageCount: number | null,
): IngestDocumentRequest {
  const uploadedAt = new Date().toISOString();
  const createdUtc = file.lastModified > 0 ? new Date(file.lastModified).toISOString() : uploadedAt;
  const docId = buildChunkDocId(file.name, options.uploadBatchId, options.index, chunkIndex);

  const metadata: Record<string, unknown> = {
    original_file_name: file.name,
    original_size_bytes: file.size,
    uploaded_at_utc: uploadedAt,
    uploaded_via: "cwi-gpt-web-ui",
    upload_batch_id: options.uploadBatchId,
    uploaded_by: options.userId,
    import_kind: importKind,
    normalized_for_ingest: normalizationMode === "structured_transcript",
    normalization_mode: normalizationMode,
    chunk_index: chunkIndex + 1,
    source_file_index: options.index + 1,
    chunk_character_count: content.length,
  };

  if (totalChunks !== null) {
    metadata.total_chunks = totalChunks;
  }

  if (messageCount !== null) {
    metadata.message_count = messageCount;
  }

  if (file.lastModified > 0) {
    metadata.original_modified_utc = createdUtc;
  }

  return {
    version: "v1",
    tenant_id: options.tenantId,
    user_id: options.userId,
    idempotency_key: `${options.uploadBatchId}:${docId}`,
    tags: ["gpt-web-ui", "chat-history-import", importKind, normalizationMode],
    document: {
      doc_id: docId,
      title: buildChunkTitle(file.name, chunkIndex, totalChunks),
      mime_type: inferMimeType(file.name, file.type),
      content,
      content_encoding: "plain",
      source: "cwi-gpt-web-ui",
      created_utc: createdUtc,
      metadata,
    },
  };
}

export function isDatasetUploadFile(name: string, mimeType: string | null | undefined): boolean {
  return mimeType === "application/json" || isTextLikeFile(name, mimeType);
}

export function isChatHistoryImportFile(name: string, mimeType: string | null | undefined): boolean {
  return isZipFile(name, mimeType) || isImageFile(name, mimeType) || isDatasetUploadFile(name, mimeType);
}

export function buildDatasetDocId(fileName: string, uploadBatchId: string, index: number): string {
  return `web-upload-${sanitizeDocIdSegment(uploadBatchId)}-${String(index + 1).padStart(3, "0")}-${sanitizeDocIdSegment(fileName)}`;
}

export async function buildDatasetIngestRequest(
  file: File,
  options: DatasetIngestBuildOptions,
): Promise<IngestDocumentRequest> {
  const rawText = await file.text();
  const parsedHistory = parseImportedChatHistory(file.name, rawText, { maxChars: null });
  const importKind = inferImportKind(file.name, parsedHistory.messageCount);
  const uploadedAt = new Date().toISOString();
  const createdUtc = file.lastModified > 0 ? new Date(file.lastModified).toISOString() : uploadedAt;
  const docId = buildDatasetDocId(file.name, options.uploadBatchId, options.index);

  const metadata: Record<string, unknown> = {
    original_file_name: file.name,
    original_size_bytes: file.size,
    uploaded_at_utc: uploadedAt,
    uploaded_via: "cwi-gpt-web-ui",
    upload_batch_id: options.uploadBatchId,
    uploaded_by: options.userId,
    import_kind: importKind,
    normalized_for_ingest: parsedHistory.messageCount !== null,
    normalization_mode: parsedHistory.messageCount !== null ? "structured_transcript" : "raw_chunked",
  };

  if (parsedHistory.messageCount !== null) {
    metadata.message_count = parsedHistory.messageCount;
  }

  if (file.lastModified > 0) {
    metadata.original_modified_utc = createdUtc;
  }

  return {
    version: "v1",
    tenant_id: options.tenantId,
    user_id: options.userId,
    idempotency_key: `${options.uploadBatchId}:${docId}`,
    tags: ["gpt-web-ui", "dataset-upload", importKind],
    document: {
      doc_id: docId,
      title: file.name,
      mime_type: inferMimeType(file.name, file.type),
      content: parsedHistory.content,
      content_encoding: "plain",
      source: "cwi-gpt-web-ui",
      created_utc: createdUtc,
      metadata,
    },
  };
}

export async function* buildChatHistoryIngestRequests(
  file: ImportableFileLike,
  options: ChatHistoryIngestBuildOptions,
): AsyncGenerator<ChatHistoryIngestRequestPart> {
  const chunkChars = options.chunkChars ?? CHAT_HISTORY_IMPORT_CHUNK_CHARS;

  if (isZipFile(file.name, file.type)) {
    let entryIndex = 0;
    for await (const entryFile of extractZipImportableFiles(file as File)) {
      for await (const part of buildChatHistoryIngestRequests(entryFile, {
        ...options,
        index: options.index + entryIndex,
        chunkChars,
      })) {
        yield part;
      }
      entryIndex += 1;
    }
    return;
  }

  if (isImageFile(file.name, file.type)) {
    const content = buildImageImportContent(file.name, inferMimeType(file.name, file.type), file.size);
    yield {
      request: buildChunkRequest(
        file,
        options,
        content,
        "document",
        0,
        1,
        "raw_chunked",
        null,
      ),
      chunkIndex: 0,
      normalizationMode: "raw_chunked",
    };
    return;
  }

  if (shouldReadWholeFile(file)) {
    const rawText = await file.text();
    const parsedHistory = parseImportedChatHistory(file.name, rawText, { maxChars: null });
    const importKind = inferImportKind(file.name, parsedHistory.messageCount);
    const segments = Array.from(splitTextIntoSegments(parsedHistory.content, chunkChars));
    const totalChunks = segments.length;
    const normalizationMode: ChatHistoryNormalizationMode =
      parsedHistory.messageCount !== null ? "structured_transcript" : "raw_chunked";

    for (const [chunkIndex, content] of segments.entries()) {
      yield {
        request: buildChunkRequest(
          file,
          options,
          content,
          importKind,
          chunkIndex,
          totalChunks,
          normalizationMode,
          parsedHistory.messageCount,
        ),
        chunkIndex,
        normalizationMode,
      };
    }
    return;
  }

  const importKind = inferImportKind(file.name, null);
  let chunkIndex = 0;
  for await (const content of streamTextSegments(file, chunkChars)) {
    yield {
      request: buildChunkRequest(
        file,
        options,
        content,
        importKind,
        chunkIndex,
        null,
        "raw_chunked",
        null,
      ),
      chunkIndex,
      normalizationMode: "raw_chunked",
    };
    chunkIndex += 1;
  }
}
