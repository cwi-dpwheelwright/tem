import { describe, expect, it } from "vitest";
import fs from "fs";
import path from "path";

const repoRoot = path.resolve(__dirname, "..", "..");
const srcRoot = path.join(repoRoot, "src");

function listSourceFiles(dir: string): string[] {
  const entries = fs.readdirSync(dir, { withFileTypes: true });
  const files: string[] = [];
  for (const entry of entries) {
    const fullPath = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      if (entry.name === "__tests__") {
        continue;
      }
      files.push(...listSourceFiles(fullPath));
      continue;
    }
    if (entry.isFile() && entry.name.endsWith(".ts")) {
      files.push(fullPath);
    }
  }
  return files;
}

describe("architecture boundaries", () => {
  it("restricts http client usage to services layer", () => {
    const files = listSourceFiles(srcRoot);
    const offenders: string[] = [];

    for (const file of files) {
      const relPath = path.relative(srcRoot, file);
      const content = fs.readFileSync(file, "utf8");
      const importsHttp = content.includes("../http/") || content.includes("./http/");
      const isService = relPath.startsWith(`services${path.sep}`) || relPath.startsWith("services/");

      if (importsHttp && !isService) {
        offenders.push(relPath);
      }
    }

    expect(offenders).toEqual([]);
  });
});
