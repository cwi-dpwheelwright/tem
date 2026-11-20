# Incident Response: SEV Levels + Escalation + AAR

## Severity levels
- **SEV-1**: Total outage or safety/guardrail breach.
- **SEV-2**: Major functionality degraded for many users.
- **SEV-3**: Partial degradation or elevated error rates.
- **SEV-4**: Minor issue, no immediate user impact.

## Escalation path
1. On-call engineer acknowledges and declares SEV.
2. Incident commander (IC) assigned for SEV-1/2.
3. Communications lead updates stakeholders every 30–60 minutes.
4. Escalate to platform/security leads for SEV-1 or suspected breach.

## AAR template (48–72h)
- Summary: what happened and impact window.
- Timeline: key events and decision points.
- Root cause: technical + contributing factors.
- Detection: how issue was detected and time to detect.
- Response: actions taken and time to mitigate.
- Preventative actions: short-term and long-term fixes.
- Evidence: logs, metrics, dashboards, and tickets.
