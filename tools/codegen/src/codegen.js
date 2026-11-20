import { execSync } from 'node:child_process';
import { existsSync, mkdirSync, readdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join, relative } from 'node:path';

const toolRoot = dirname(dirname(fileURLToPath(import.meta.url)));
const repoRoot = dirname(dirname(toolRoot));
const openapiDir = join(repoRoot, 'contracts', 'openapi');
const schemaDir = join(repoRoot, 'contracts', 'json-schema');
const outputDir = join(repoRoot, 'contracts', 'generated');

function ensureDir(path) {
  if (!existsSync(path)) {
    mkdirSync(path, { recursive: true });
  }
}

function listFiles(dir, ext) {
  if (!existsSync(dir)) {
    return [];
  }
  return readdirSync(dir).filter((name) => name.endsWith(ext));
}

function runOpenApi() {
  const files = listFiles(openapiDir, '.yaml').concat(listFiles(openapiDir, '.yml'));
  for (const file of files) {
    const input = join(openapiDir, file);
    const base = file.replace(/\.(yaml|yml)$/i, '');
    const out = join(outputDir, `${base}.types.ts`);
    const inputArg = relative(repoRoot, input).replace(/\\/g, '/');
    const outArg = relative(repoRoot, out).replace(/\\/g, '/');
    execSync(`npx --prefix "${toolRoot}" openapi-typescript "${inputArg}" -o "${outArg}"`, {
      stdio: 'inherit',
      cwd: repoRoot,
    });
  }
}

function runJsonSchema() {
  const files = listFiles(schemaDir, '.json');
  for (const file of files) {
    const input = join(schemaDir, file);
    const base = file.replace(/\.json$/i, '');
    const out = join(outputDir, `${base}.schema.ts`);
    const inputArg = relative(repoRoot, input).replace(/\\/g, '/');
    const outArg = relative(repoRoot, out).replace(/\\/g, '/');
    const cmd = `npx --prefix "${toolRoot}" json2ts -i "${inputArg}" -o "${outArg}"`;
    try {
      execSync(cmd, { stdio: 'inherit', cwd: repoRoot });
    } catch (err) {
      // json-schema-to-ts does not ship a stable CLI; keep codegen resilient.
      console.warn(`Skipping JSON schema typegen for ${file}: ${err.message}`);
    }
  }
}

ensureDir(outputDir);
runOpenApi();
runJsonSchema();
