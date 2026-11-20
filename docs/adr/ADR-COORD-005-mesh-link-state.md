# ADR-COORD-005: Mesh Link-State Model

## Status
Accepted

## Context
Iteration-05 introduces multi-interface mesh orchestration for the coordination layer.
We need a consistent model for link quality across WiFi/cellular/ethernet/satellite
interfaces to drive routing decisions and health signals.

## Decision
Define a link-state payload with:
- node_id
- interface (wifi/cellular/ethernet/satellite/lora/unknown)
- signal_quality (0-1)
- latency_ms
- jitter_ms
- packet_loss (0-1)
- bandwidth_kbps
- throughput_kbps
- rssi_dbm
- snr_db
- status (healthy/degraded/down)
- updated_utc

The coordination layer will store the latest link-state per node/interface and expose
list endpoints for routing consumers.

Normalization rules:
- latency_ms: round to integer milliseconds, clamp to >= 0.
- jitter_ms: round to integer milliseconds when provided.
- packet_loss/loss_ratio: clamp to [0, 1].
- bandwidth_kbps: derived from throughput_mbps or max(rx_mbps, tx_mbps), clamp to >= 0.
- throughput_kbps: derived from throughput_mbps, clamp to >= 0.
- rssi_dbm: retain raw RSSI (negative dBm), use for signal_quality if provided.
- snr_db: retain raw SNR, normalize to signal_quality via snr_db / 30.
- signal_quality: normalized to [0,1] using snr_db, rssi_dbm, or bandwidth fallback.

## Consequences
- Pros: consistent telemetry across interfaces, simple comparisons for degradation.
- Cons: coarse metrics; advanced radios may need richer fields later.
