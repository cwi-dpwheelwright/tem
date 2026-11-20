import { describe, expect, it } from "vitest";
import { takeSelectedFiles, type FileInputLike } from "@/lib/file-input";

function createLiveFileInput(files: File[]): FileInputLike & { remainingFiles: () => File[] } {
  let currentFiles = [...files];

  return {
    get files() {
      const fileListLike = [...currentFiles] as File[];
      Object.defineProperty(fileListLike, "item", {
        value: (index: number) => currentFiles[index] ?? null,
        enumerable: false,
      });
      return fileListLike as unknown as FileList;
    },
    get value() {
      return currentFiles.length > 0 ? `C:\\fakepath\\${currentFiles[0].name}` : "";
    },
    set value(nextValue: string) {
      if (nextValue === "") {
        currentFiles = [];
      }
    },
    remainingFiles() {
      return [...currentFiles];
    },
  };
}

describe("file-input", () => {
  it("clones selected files before clearing a live file input", () => {
    const selectedFile = new File(["hello"], "attachment.txt", { type: "text/plain" });
    const input = createLiveFileInput([selectedFile]);

    const files = takeSelectedFiles(input);

    expect(files).toHaveLength(1);
    expect(files[0]?.name).toBe("attachment.txt");
    expect(input.remainingFiles()).toHaveLength(0);
    expect(input.value).toBe("");
  });
});
