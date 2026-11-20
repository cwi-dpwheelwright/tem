export type FileInputLike = Pick<HTMLInputElement, "files" | "value">;

export function takeSelectedFiles(input: FileInputLike): File[] {
  const selectedFiles = Array.from(input.files ?? []);
  input.value = "";
  return selectedFiles;
}
