"use client";

import { useCallback, useEffect, useMemo, useRef, useState, type ChangeEvent } from "react";
import type { AccountHomeNodeLinkRequest } from "@/lib/api/types";
import { Modal } from "@/components/modal";
import { parseHomeNodeLinkPayload } from "@/lib/home-node-linking";

type CameraState = "idle" | "starting" | "ready" | "blocked" | "unavailable";
type CameraPermissionState = PermissionState | "unsupported" | "unknown";
type ScanEngine = "barcode-detector" | "jsqr" | "none";

interface HomeNodeLinkModalProps {
  onClose: () => void;
  onLink: (request: AccountHomeNodeLinkRequest) => Promise<boolean>;
  isLinking: boolean;
  error: string | null;
  expectedTenantId?: string | null;
}

interface DetectedBarcode {
  rawValue?: string;
}

interface BarcodeDetectorInstance {
  detect: (source: ImageBitmapSource) => Promise<DetectedBarcode[]>;
}

interface BarcodeDetectorConstructor {
  new (options?: { formats?: string[] }): BarcodeDetectorInstance;
  getSupportedFormats?: () => Promise<string[]>;
}

interface JsQrScanResult {
  data: string;
}

interface JsQrDecoder {
  (
    imageData: Uint8ClampedArray,
    width: number,
    height: number,
    options?: { inversionAttempts?: "attemptBoth" | "dontInvert" | "onlyInvert" | "invertFirst" },
  ): JsQrScanResult | null;
}

function resolveBarcodeDetector(): BarcodeDetectorConstructor | null {
  if (typeof window === "undefined") {
    return null;
  }
  return (window as Window & { BarcodeDetector?: BarcodeDetectorConstructor }).BarcodeDetector ?? null;
}

type HomeNodeLinkWindow = Window & {
  BarcodeDetector?: BarcodeDetectorConstructor;
  jsQR?: JsQrDecoder;
};

type DecodableSource = HTMLVideoElement | HTMLImageElement | HTMLCanvasElement | ImageBitmap;

function getDecodableSourceDimensions(source: DecodableSource): { width: number; height: number } | null {
  if (source instanceof HTMLVideoElement) {
    return source.videoWidth > 0 && source.videoHeight > 0
      ? { width: source.videoWidth, height: source.videoHeight }
      : null;
  }

  if (source instanceof HTMLImageElement) {
    return source.naturalWidth > 0 && source.naturalHeight > 0
      ? { width: source.naturalWidth, height: source.naturalHeight }
      : null;
  }

  return source.width > 0 && source.height > 0
    ? { width: source.width, height: source.height }
    : null;
}

function scaleDimensions(width: number, height: number, maxEdge: number): { width: number; height: number } {
  const largestEdge = Math.max(width, height);
  if (largestEdge <= maxEdge) {
    return { width, height };
  }

  const scale = maxEdge / largestEdge;
  return {
    width: Math.max(1, Math.round(width * scale)),
    height: Math.max(1, Math.round(height * scale)),
  };
}

export function HomeNodeLinkModal({
  onClose,
  onLink,
  isLinking,
  error,
  expectedTenantId,
}: HomeNodeLinkModalProps) {
  const [manualPayload, setManualPayload] = useState("");
  const [cameraState, setCameraState] = useState<CameraState>("idle");
  const [cameraPermission, setCameraPermission] = useState<CameraPermissionState>("unknown");
  const [isAutoScanAvailable, setIsAutoScanAvailable] = useState(false);
  const [scanEngine, setScanEngine] = useState<ScanEngine>("none");
  const [scanMessage, setScanMessage] = useState("Camera is off. Select Enable camera to scan your node QR code.");
  const videoRef = useRef<HTMLVideoElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const detectorRef = useRef<BarcodeDetectorInstance | null>(null);
  const jsQrDecoderRef = useRef<JsQrDecoder | null>(null);
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const scanIntervalRef = useRef<number | null>(null);
  const scriptPromiseRef = useRef<Promise<JsQrDecoder | null> | null>(null);
  const isDetectingRef = useRef(false);
  const isMountedRef = useRef(true);
  const lastScannedValueRef = useRef<string | null>(null);

  const stopCamera = useCallback(() => {
    if (scanIntervalRef.current) {
      window.clearInterval(scanIntervalRef.current);
      scanIntervalRef.current = null;
    }
    isDetectingRef.current = false;
    const stream = streamRef.current;
    if (stream) {
      stream.getTracks().forEach((track) => track.stop());
      streamRef.current = null;
    }
    const video = videoRef.current;
    if (video) {
      video.srcObject = null;
    }
  }, []);

  const getCanvas = useCallback(() => {
    if (!canvasRef.current) {
      canvasRef.current = document.createElement("canvas");
    }

    return canvasRef.current;
  }, []);

  const resolvePermissionState = useCallback(async (): Promise<CameraPermissionState> => {
    if (!navigator.permissions?.query) {
      return "unsupported";
    }

    try {
      const permission = await navigator.permissions.query({ name: "camera" as PermissionName });
      return permission.state;
    } catch {
      return "unsupported";
    }
  }, []);

  const loadJsQrDecoder = useCallback(async (): Promise<JsQrDecoder | null> => {
    if (typeof window === "undefined") {
      return null;
    }

    const currentWindow = window as HomeNodeLinkWindow;
    if (currentWindow.jsQR) {
      jsQrDecoderRef.current = currentWindow.jsQR;
      return currentWindow.jsQR;
    }

    if (scriptPromiseRef.current) {
      return scriptPromiseRef.current;
    }

    scriptPromiseRef.current = new Promise<JsQrDecoder | null>((resolve) => {
      const script = document.createElement("script");
      script.src = "/vendor/jsQR.js";
      script.async = true;
      script.onload = () => {
        const decoder = (window as HomeNodeLinkWindow).jsQR ?? null;
        jsQrDecoderRef.current = decoder;
        resolve(decoder);
      };
      script.onerror = () => {
        resolve(null);
      };
      document.head.appendChild(script);
    });

    return scriptPromiseRef.current;
  }, []);

  const initializeScannerEngine = useCallback(async () => {
    const BarcodeDetectorApi = resolveBarcodeDetector();
    if (!BarcodeDetectorApi) {
      const jsQrDecoder = await loadJsQrDecoder();
      detectorRef.current = null;
      jsQrDecoderRef.current = jsQrDecoder;
      if (isMountedRef.current) {
        setIsAutoScanAvailable(Boolean(jsQrDecoder));
        setScanEngine(jsQrDecoder ? "jsqr" : "none");
      }
      return jsQrDecoder ? "jsqr" : "none";
    }

    try {
      const supportedFormats = typeof BarcodeDetectorApi.getSupportedFormats === "function"
        ? await BarcodeDetectorApi.getSupportedFormats()
        : null;
      const supportsQr = !supportedFormats || supportedFormats.includes("qr_code");
      if (!supportsQr) {
        const jsQrDecoder = await loadJsQrDecoder();
        detectorRef.current = null;
        jsQrDecoderRef.current = jsQrDecoder;
        if (isMountedRef.current) {
          setIsAutoScanAvailable(Boolean(jsQrDecoder));
          setScanEngine(jsQrDecoder ? "jsqr" : "none");
        }
        return jsQrDecoder ? "jsqr" : "none";
      }

      detectorRef.current = new BarcodeDetectorApi({ formats: ["qr_code"] });
      jsQrDecoderRef.current = null;
      if (isMountedRef.current) {
        setIsAutoScanAvailable(true);
        setScanEngine("barcode-detector");
      }
      return "barcode-detector";
    } catch {
      const jsQrDecoder = await loadJsQrDecoder();
      detectorRef.current = null;
      jsQrDecoderRef.current = jsQrDecoder;
      if (isMountedRef.current) {
        setIsAutoScanAvailable(Boolean(jsQrDecoder));
        setScanEngine(jsQrDecoder ? "jsqr" : "none");
      }
      return jsQrDecoder ? "jsqr" : "none";
    }
  }, [loadJsQrDecoder]);

  const decodeFromSource = useCallback(async (source: DecodableSource): Promise<string | null> => {
    const sourceDimensions = getDecodableSourceDimensions(source);
    if (!sourceDimensions) {
      return null;
    }

    const scaled = scaleDimensions(sourceDimensions.width, sourceDimensions.height, 960);
    const canvas = getCanvas();
    canvas.width = scaled.width;
    canvas.height = scaled.height;

    const context = canvas.getContext("2d", { willReadFrequently: true });
    if (!context) {
      return null;
    }

    context.drawImage(source, 0, 0, scaled.width, scaled.height);

    const detector = detectorRef.current;
    if (detector) {
      try {
        const barcodes = await detector.detect(canvas);
        const payload = barcodes.find((item) => typeof item.rawValue === "string" && item.rawValue.trim())?.rawValue?.trim();
        if (payload) {
          return payload;
        }
      } catch {
      }
    }

    const decoder = jsQrDecoderRef.current ?? await loadJsQrDecoder();
    if (!decoder) {
      return null;
    }

    const imageData = context.getImageData(0, 0, scaled.width, scaled.height);
    return decoder(imageData.data, imageData.width, imageData.height, { inversionAttempts: "attemptBoth" })?.data?.trim() ?? null;
  }, [getCanvas, loadJsQrDecoder]);

  const loadImageFile = useCallback(async (file: File): Promise<HTMLImageElement> => {
    const imageUrl = URL.createObjectURL(file);
    try {
      const image = new Image();
      await new Promise<void>((resolve, reject) => {
        image.onload = () => resolve();
        image.onerror = () => reject(new Error("Unable to load image."));
        image.src = imageUrl;
      });
      return image;
    } finally {
      URL.revokeObjectURL(imageUrl);
    }
  }, []);

  const startCamera = useCallback(async () => {
    stopCamera();
    if (isMountedRef.current) {
      detectorRef.current = null;
      jsQrDecoderRef.current = null;
      setIsAutoScanAvailable(false);
      setScanEngine("none");
      setCameraState("starting");
      setScanMessage("Starting your camera...");
    }

    if (typeof window !== "undefined" && !window.isSecureContext) {
      if (isMountedRef.current) {
        setCameraState("unavailable");
        setScanMessage("Camera access requires HTTPS or localhost. Paste the QR payload below or open this site from a secure address.");
      }
      return;
    }

    if (!navigator.mediaDevices?.getUserMedia) {
      if (isMountedRef.current) {
        setCameraPermission("unsupported");
        setCameraState("unavailable");
        setScanMessage("Camera access is not available in this browser. Paste the QR payload below.");
      }
      return;
    }

    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: false,
        video: {
          facingMode: { exact: "environment" },
        },
      }).catch(async () => navigator.mediaDevices.getUserMedia({
        audio: false,
        video: {
          facingMode: { ideal: "environment" },
        },
      }));

      if (!isMountedRef.current) {
        stream.getTracks().forEach((track) => track.stop());
        return;
      }

      streamRef.current = stream;
      const video = videoRef.current;
      if (video) {
        video.srcObject = stream;
        await video.play().catch(() => undefined);
      }

      const engine = await initializeScannerEngine();
      if (!isMountedRef.current) {
        stream.getTracks().forEach((track) => track.stop());
        return;
      }

      setCameraPermission("granted");
      setCameraState("ready");
      setScanMessage(
        engine === "barcode-detector"
          ? "Point your camera at the node QR code to link automatically."
          : engine === "jsqr"
            ? "Camera preview is ready. QR decoding is running in-browser for this device."
            : "Camera preview is ready, but QR decoding is unavailable here. Use Scan from photo or paste the payload below.",
      );
    } catch (cameraError) {
      const refreshedPermission = await resolvePermissionState();
      if (!isMountedRef.current) {
        return;
      }

      setCameraPermission(refreshedPermission);
      if (refreshedPermission === "denied") {
        setCameraState("blocked");
        setScanMessage("Camera access is blocked in your browser settings. Allow camera access for this site, then retry.");
        return;
      }

      const errorName = cameraError instanceof DOMException ? cameraError.name : "";
      if (errorName === "NotFoundError" || errorName === "DevicesNotFoundError") {
        setCameraState("unavailable");
        setScanMessage("No camera was found on this device. Paste the QR payload below.");
        return;
      }

      if (errorName === "NotReadableError" || errorName === "TrackStartError") {
        setCameraState("unavailable");
        setScanMessage("The camera could not start. Close other apps using the camera, then try again.");
        return;
      }

      setCameraState("blocked");
      setScanMessage("Allow camera access in the browser prompt to scan automatically, or use Scan from photo below.");
    }
  }, [initializeScannerEngine, resolvePermissionState, stopCamera]);

  useEffect(() => {
    isMountedRef.current = true;

    return () => {
      isMountedRef.current = false;
      stopCamera();
    };
  }, [stopCamera]);

  useEffect(() => {
    if (!isLinking && error?.trim()) {
      setScanMessage(error.trim());
    }
  }, [error, isLinking]);

  const attemptLinkPayload = useCallback(async (
    payload: string,
    options?: { source?: "camera" | "photo" | "manual" },
  ): Promise<boolean> => {
    const trimmedPayload = payload.trim();
    if (!trimmedPayload || isLinking) {
      return false;
    }

    const preview = parseHomeNodeLinkPayload(trimmedPayload);
    setManualPayload(trimmedPayload);

    if (preview.kind === "invalid") {
      setScanMessage(preview.reason);
      return false;
    }

    const sourceLabel = options?.source === "photo"
      ? "photo"
      : options?.source === "manual"
        ? "payload"
        : "camera";
    setScanMessage(`QR code detected from the ${sourceLabel}. Linking your node...`);
    const linked = await onLink({ qr_code_payload: trimmedPayload });
    if (!linked) {
      setScanMessage("We found a QR code but could not link the node. You can try again or paste the payload below.");
    }

    return linked;
  }, [isLinking, onLink]);

  useEffect(() => {
    let disposed = false;
    let permissionStatus: PermissionStatus | null = null;

    const syncPermission = async () => {
      if (!navigator.permissions?.query) {
        return;
      }

      try {
        permissionStatus = await navigator.permissions.query({ name: "camera" as PermissionName });
        if (disposed || !isMountedRef.current) {
          return;
        }

        setCameraPermission(permissionStatus.state);
        if (permissionStatus.state === "denied") {
          setCameraState((current) => current === "idle" ? "blocked" : current);
          setScanMessage((current) =>
            current.startsWith("Camera is off")
              ? "Camera access is blocked in your browser settings. Allow camera access for this site, then retry."
              : current);
        }

        permissionStatus.onchange = () => {
          if (!isMountedRef.current) {
            return;
          }

          setCameraPermission(permissionStatus?.state ?? "unknown");
        };
      } catch {
      }
    };

    void syncPermission();

    return () => {
      disposed = true;
      if (permissionStatus) {
        permissionStatus.onchange = null;
      }
    };
  }, []);

  useEffect(() => {
    if (cameraState !== "ready" || scanEngine === "none") {
      return;
    }

    const detectCode = async () => {
      if (isLinking || isDetectingRef.current) {
        return;
      }

      const video = videoRef.current;
      if (!video || video.readyState < HTMLMediaElement.HAVE_CURRENT_DATA) {
        return;
      }

      isDetectingRef.current = true;
      try {
        const payload = await decodeFromSource(video);
        if (!payload || payload === lastScannedValueRef.current) {
          return;
        }

        lastScannedValueRef.current = payload;
        const linked = await attemptLinkPayload(payload, { source: "camera" });
        if (!linked) {
          lastScannedValueRef.current = null;
        }
      } finally {
        isDetectingRef.current = false;
      }
    };

    scanIntervalRef.current = window.setInterval(() => {
      void detectCode();
    }, 500);

    return () => {
      if (scanIntervalRef.current) {
        window.clearInterval(scanIntervalRef.current);
        scanIntervalRef.current = null;
      }
    };
  }, [attemptLinkPayload, cameraState, decodeFromSource, isLinking, scanEngine]);

  const handleStopCamera = useCallback(() => {
    stopCamera();
    setCameraState("idle");
    setScanMessage("Camera is off. Select Enable camera or Scan from photo to read your node QR code.");
  }, [stopCamera]);

  const triggerPhotoScan = useCallback(() => {
    fileInputRef.current?.click();
  }, []);

  const handlePhotoSelection = useCallback(async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file || isLinking) {
      return;
    }

    setScanMessage("Scanning the selected photo...");
    const currentEngine = scanEngine === "none"
      ? await initializeScannerEngine()
      : scanEngine;

    if (currentEngine === "none") {
      setScanMessage("This browser cannot decode QR codes automatically. Paste the payload below instead.");
      return;
    }

    try {
      const image = await loadImageFile(file);
      const payload = await decodeFromSource(image);
      if (!payload) {
        setScanMessage("We couldn't find a QR code in that photo. Try again with better lighting or paste the payload below.");
        return;
      }

      lastScannedValueRef.current = payload;
      const linked = await attemptLinkPayload(payload, { source: "photo" });
      if (!linked) {
        lastScannedValueRef.current = null;
      }
    } catch {
      setScanMessage("We couldn't read that image. Try another photo or paste the payload below.");
    }
  }, [attemptLinkPayload, decodeFromSource, initializeScannerEngine, isLinking, loadImageFile, scanEngine]);

  const cameraSummary = useMemo(() => {
    if (cameraState === "idle") {
      return "Camera permission has not been requested yet.";
    }
    if (cameraState === "blocked" && cameraPermission === "denied") {
      return "Camera permission is denied for this site until it is changed in browser site settings.";
    }
    if (cameraState === "blocked") {
      return "Camera permission has not been granted yet. You can trigger the browser prompt again.";
    }
    if (cameraState === "unavailable") {
      return "This device or browser cannot expose a camera stream here.";
    }
    if (cameraState === "ready" && !isAutoScanAvailable) {
      return "This browser can show the camera, but automatic QR scanning is not available.";
    }
    if (cameraState === "ready" && scanEngine === "jsqr") {
      return "Live QR scanning is active using the in-browser decoder.";
    }
    if (cameraState === "ready" && isAutoScanAvailable) {
      return "Live QR scanning is active.";
    }
    return "Preparing the scanner...";
  }, [cameraPermission, cameraState, isAutoScanAvailable, scanEngine]);

  const permissionHelp = useMemo(() => {
    if (cameraState === "idle") {
      return "Select Enable camera to ask the browser for camera access, or use Scan from photo if you prefer a one-time camera capture.";
    }
    if (cameraState === "blocked" && cameraPermission === "denied") {
      return "Use your browser's site settings or the camera icon in the address bar to allow camera access for this site, then select Retry camera. You can still use Scan from photo below.";
    }
    if (cameraState === "blocked") {
      return "Select Enable camera to ask the browser for camera access again, or use Scan from photo if the prompt does not appear.";
    }
    if (cameraState === "unavailable") {
      return "If live camera access is unavailable here, try Scan from photo or paste the QR payload manually to complete node linking.";
    }
    return null;
  }, [cameraPermission, cameraState]);

  const previewOverlayLabel = useMemo(() => {
    if (cameraState === "idle") {
      return "Camera off";
    }
    if (cameraState === "starting") {
      return "Starting camera...";
    }
    if (cameraState === "blocked") {
      return cameraPermission === "denied" ? "Camera blocked" : "Camera permission needed";
    }
    if (cameraState === "unavailable") {
      return "Camera unavailable";
    }
    return null;
  }, [cameraPermission, cameraState]);

  const retryLabel = cameraPermission === "denied"
    ? "Retry after allowing camera"
    : cameraState === "starting"
      ? "Starting..."
      : cameraState === "ready"
        ? "Restart camera"
        : "Enable camera";

  const payloadPreview = useMemo(() => parseHomeNodeLinkPayload(manualPayload), [manualPayload]);

  const submitManualPayload = async () => {
    const payload = manualPayload.trim();
    if (!payload || isLinking || payloadPreview.kind === "invalid") {
      return;
    }

    await attemptLinkPayload(payload, { source: "manual" });
  };

  return (
    <Modal ariaLabel="Link my node" onClose={onClose}>
      <div className="card-light home-node-link-modal">
        <div className="home-node-link-header">
          <h2 className="card-title">Link My Node</h2>
          <p className="card-subtitle">
            Scan the QR code displayed by your node. Canonical node JSON is preferred, and the server will verify whether the signed-in account can claim or link that node.
          </p>
          {expectedTenantId ? (
            <p className="card-text-muted-sm">
              Signed in tenant: <strong>{expectedTenantId}</strong>
            </p>
          ) : null}
        </div>

        <div className="home-node-link-scanner">
          <div className="home-node-link-preview">
            <video
              ref={videoRef}
              className="home-node-link-video"
              autoPlay
              muted
              playsInline
            />
            <div className="home-node-link-frame" aria-hidden="true" />
            {previewOverlayLabel ? (
              <div className="home-node-link-preview-message">
                {previewOverlayLabel}
              </div>
            ) : null}
          </div>

          <div className="home-node-link-status">
            <div className="home-node-link-status-title">Scanner status</div>
            <div className="home-node-link-status-copy">{scanMessage}</div>
            <div className="home-node-link-status-meta">{cameraSummary}</div>
            <div className="home-node-link-status-actions">
              <div className="home-node-link-status-button-row">
                {cameraState !== "unavailable" || cameraPermission !== "unsupported" ? (
                  <button
                    className="btn-secondary home-node-link-status-button"
                    type="button"
                    onClick={() => void startCamera()}
                    disabled={cameraState === "starting" || isLinking}
                  >
                    {retryLabel}
                  </button>
                ) : null}
                <button
                  className="btn-secondary home-node-link-status-button"
                  type="button"
                  onClick={triggerPhotoScan}
                  disabled={isLinking}
                >
                  Scan from photo
                </button>
                {cameraState === "ready" ? (
                  <button
                    className="btn-secondary home-node-link-status-button"
                    type="button"
                    onClick={handleStopCamera}
                    disabled={isLinking}
                  >
                    Stop camera
                  </button>
                ) : null}
              </div>
              {permissionHelp ? (
                <div className="home-node-link-helper">{permissionHelp}</div>
              ) : null}
            </div>
            <input
              ref={fileInputRef}
              type="file"
              accept="image/*"
              capture="environment"
              hidden
              onChange={(event) => void handlePhotoSelection(event)}
            />
          </div>
        </div>

        {error ? (
          <div className="alert alert-error" role="alert">
            {error}
          </div>
        ) : null}

        {!isAutoScanAvailable || cameraState !== "ready" ? (
          <div className="alert alert-warning" role="status">
            If live scanning is unavailable on this browser, use Scan from photo or paste the QR contents manually.
          </div>
        ) : null}

        <label className="label-stack" htmlFor="node-link-manual-payload">
          <span className="label-title">Manual entry</span>
          <span className="card-text-muted-sm">
            Paste the JSON from <code>/payload.json</code> if you prefer not to use the live scanner. Raw node IDs still work, and legacy Tailscale IP labels remain supported.
          </span>
          <textarea
            id="node-link-manual-payload"
            className="textarea-input home-node-link-textarea"
            value={manualPayload}
            onChange={(event) => setManualPayload(event.target.value)}
            placeholder='{"node_id":"home-node-01","identifier_version":"machine-id-sha256-v1"}'
            disabled={isLinking}
          />
        </label>

        {payloadPreview.kind === "invalid" ? (
          <div className="alert alert-error" role="alert">
            {payloadPreview.reason}
          </div>
        ) : null}

        {payloadPreview.kind !== "empty" && payloadPreview.kind !== "invalid" ? (
          <div className="home-node-link-detected">
            <div className="home-node-link-detected-title">Detected from payload</div>
            <div className="home-node-link-detected-grid">
              <div className="home-node-link-detected-item">
                <span className="home-node-link-detected-label">Match type</span>
                <span className="home-node-link-detected-value">{payloadPreview.matchedVia}</span>
              </div>
              {"nodeId" in payloadPreview && payloadPreview.nodeId ? (
                <div className="home-node-link-detected-item">
                  <span className="home-node-link-detected-label">Node ID</span>
                  <span className="home-node-link-detected-value home-node-link-detected-value--break">
                    {payloadPreview.nodeId}
                  </span>
                </div>
              ) : null}
              {"tailscaleIp" in payloadPreview && payloadPreview.tailscaleIp ? (
                <div className="home-node-link-detected-item">
                  <span className="home-node-link-detected-label">Tailscale IP</span>
                  <span className="home-node-link-detected-value">{payloadPreview.tailscaleIp}</span>
                </div>
              ) : null}
              {"identifierVersion" in payloadPreview && payloadPreview.identifierVersion ? (
                <div className="home-node-link-detected-item">
                  <span className="home-node-link-detected-label">Identifier version</span>
                  <span className="home-node-link-detected-value">{payloadPreview.identifierVersion}</span>
                </div>
              ) : null}
            </div>
          </div>
        ) : null}

        <div className="panel-actions">
          <button
            className="btn-primary"
            type="button"
            onClick={() => void submitManualPayload()}
            disabled={isLinking || !manualPayload.trim() || payloadPreview.kind === "invalid"}
          >
            {isLinking ? "Linking..." : "Link node"}
          </button>
          <button className="btn-secondary" type="button" onClick={onClose} disabled={isLinking}>
            Cancel
          </button>
        </div>
      </div>
    </Modal>
  );
}
