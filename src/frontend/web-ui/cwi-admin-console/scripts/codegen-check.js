/* eslint-disable @typescript-eslint/no-require-imports */
const { execFileSync } = require("node:child_process");
const crypto = require("node:crypto");
const { existsSync, readdirSync, readFileSync, statSync } = require("node:fs");
const { join, resolve } = require("node:path");

function listFiles(dir) {
  const entries = readdirSync(dir);
  const files = [];
  for (const entry of entries) {
    const fullPath = join(dir, entry);
    const stats = statSync(fullPath);
    if (stats.isDirectory()) {
      files.push(...listFiles(fullPath));
    } else {
      files.push(fullPath);
    }
  }
  return files;
}

function hashFile(path) {
  const contents = readFileSync(path);
  return crypto.createHash("sha256").update(contents).digest("hex");
}

function snapshot(dir) {
  const files = listFiles(dir).filter((file) => file.endsWith(".ts"));
  const hashes = new Map();
  for (const file of files) {
    hashes.set(file, hashFile(file));
  }
  return hashes;
}

const repoRoot = resolve(__dirname, "../../../../..");
const codegenPath = join(repoRoot, "tools", "codegen", "src", "codegen.js");
const codegenRoot = join(repoRoot, "tools", "codegen");
const generatedDir = join(repoRoot, "contracts", "generated");

if (!existsSync(codegenPath)) {
  console.warn("Codegen tool not found; skipping contract drift check.");
  process.exit(0);
}

if (!existsSync(generatedDir)) {
  console.error("contracts/generated is missing; run tools/codegen to generate types.");
  process.exit(1);
}

const before = snapshot(generatedDir);
execFileSync("node", [codegenPath], { cwd: codegenRoot, stdio: "inherit" });
const after = snapshot(generatedDir);

const drift = [];
for (const [file, hash] of after.entries()) {
  if (!before.has(file)) {
    drift.push(`NEW ${file}`);
    continue;
  }
  if (before.get(file) !== hash) {
    drift.push(`CHANGED ${file}`);
  }
}

for (const file of before.keys()) {
  if (!after.has(file)) {
    drift.push(`REMOVED ${file}`);
  }
}

if (drift.length > 0) {
  console.error("Contract codegen drift detected:");
  for (const entry of drift) {
    console.error(`- ${entry}`);
  }
  process.exit(1);
}

console.log("Contract codegen check passed.");
