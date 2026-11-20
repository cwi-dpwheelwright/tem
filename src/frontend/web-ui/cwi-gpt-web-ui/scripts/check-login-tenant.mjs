import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const root = path.resolve(__dirname, "..");
const files = [
  "src/components/login-panel.tsx",
  "src/app/login/page.tsx",
  "src/components/auth-gate.tsx",
];

const patterns = [
  /tenant id/i,
  /name\s*=\s*["']tenant/i,
  /tenantId/,
];

let failed = false;
for (const file of files) {
  const fullPath = path.join(root, file);
  if (!fs.existsSync(fullPath)) {
    continue;
  }
  const content = fs.readFileSync(fullPath, "utf8");
  for (const pattern of patterns) {
    if (pattern.test(content)) {
      console.error(`Blocked tenant reference: ${pattern} in ${file}`);
      failed = true;
    }
  }
}

if (failed) {
  process.exit(1);
}
