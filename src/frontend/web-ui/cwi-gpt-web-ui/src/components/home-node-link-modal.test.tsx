import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { HomeNodeLinkModal } from "@/components/home-node-link-modal";

const qrPayload = JSON.stringify({
  identifier_version: "machine-id-sha256-v1",
  node_id: "home-032c311b95ac9a14a81f0b3fa2520ae9",
});

describe("HomeNodeLinkModal", () => {
  const originalCreateObjectUrl = URL.createObjectURL;
  const originalRevokeObjectUrl = URL.revokeObjectURL;
  let naturalWidthDescriptor: PropertyDescriptor | undefined;
  let naturalHeightDescriptor: PropertyDescriptor | undefined;
  let srcDescriptor: PropertyDescriptor | undefined;
  let getContextSpy: { mockRestore: () => void };

  beforeEach(() => {
    naturalWidthDescriptor = Object.getOwnPropertyDescriptor(HTMLImageElement.prototype, "naturalWidth");
    naturalHeightDescriptor = Object.getOwnPropertyDescriptor(HTMLImageElement.prototype, "naturalHeight");
    srcDescriptor = Object.getOwnPropertyDescriptor(HTMLImageElement.prototype, "src");

    Object.defineProperty(URL, "createObjectURL", {
      configurable: true,
      value: vi.fn(() => "blob:node-qr"),
    });
    Object.defineProperty(URL, "revokeObjectURL", {
      configurable: true,
      value: vi.fn(),
    });
    Object.defineProperty(HTMLImageElement.prototype, "naturalWidth", {
      configurable: true,
      get: () => 640,
    });
    Object.defineProperty(HTMLImageElement.prototype, "naturalHeight", {
      configurable: true,
      get: () => 480,
    });
    Object.defineProperty(HTMLImageElement.prototype, "src", {
      configurable: true,
      get: () => "blob:node-qr",
      set(value: string) {
        void value;
        queueMicrotask(() => this.onload?.(new Event("load")));
      },
    });

    getContextSpy = vi.spyOn(HTMLCanvasElement.prototype, "getContext").mockReturnValue({
      drawImage: vi.fn(),
      getImageData: vi.fn(() => ({
        data: new Uint8ClampedArray(640 * 480 * 4),
        width: 640,
        height: 480,
      })),
    } as unknown as CanvasRenderingContext2D);

    class MockBarcodeDetector {
      static getSupportedFormats = vi.fn(async () => ["qr_code"]);
      detect = vi.fn(async () => [{ rawValue: qrPayload }]);
    }

    Object.defineProperty(window, "BarcodeDetector", {
      configurable: true,
      value: MockBarcodeDetector,
    });
  });

  afterEach(() => {
    if (originalCreateObjectUrl) {
      Object.defineProperty(URL, "createObjectURL", {
        configurable: true,
        value: originalCreateObjectUrl,
      });
    }
    if (originalRevokeObjectUrl) {
      Object.defineProperty(URL, "revokeObjectURL", {
        configurable: true,
        value: originalRevokeObjectUrl,
      });
    }
    if (naturalWidthDescriptor) {
      Object.defineProperty(HTMLImageElement.prototype, "naturalWidth", naturalWidthDescriptor);
    }
    if (naturalHeightDescriptor) {
      Object.defineProperty(HTMLImageElement.prototype, "naturalHeight", naturalHeightDescriptor);
    }
    if (srcDescriptor) {
      Object.defineProperty(HTMLImageElement.prototype, "src", srcDescriptor);
    }
    delete (window as Window & { BarcodeDetector?: unknown }).BarcodeDetector;
    getContextSpy.mockRestore();
    vi.restoreAllMocks();
  });

  it("links the node from a camera photo QR capture", async () => {
    const onLink = vi.fn(async () => true);
    const { container } = render(
      <HomeNodeLinkModal
        onClose={vi.fn()}
        onLink={onLink}
        isLinking={false}
        error={null}
        expectedTenantId="tenant-1"
      />,
    );

    fireEvent.click(screen.getByRole("button", { name: /scan from photo/i }));

    const fileInput = container.querySelector<HTMLInputElement>("input[type='file'][accept='image/*']");
    expect(fileInput).not.toBeNull();
    expect(fileInput?.getAttribute("capture")).toBe("environment");

    fireEvent.change(fileInput!, {
      target: {
        files: [new File(["fake image bytes"], "node-qr.png", { type: "image/png" })],
      },
    });

    await waitFor(() => {
      expect(onLink).toHaveBeenCalledWith({ qr_code_payload: qrPayload });
    });
    expect(screen.getByText(/QR code detected from the photo/i)).toBeTruthy();
  });
});
