type CounterKey = string;

const counters = new Map<CounterKey, number>();

function inc(key: CounterKey, value = 1) {
  counters.set(key, (counters.get(key) ?? 0) + value);
}

export function recordRequest(path: string, status: number) {
  inc(`http_requests_total|path=${path}|status=${status}`);
}

export function recordApprovalAction(decision: string) {
  inc(`approval_actions_total|decision=${decision}`);
}

export function recordBreakGlassNotification(eventType: string) {
  inc(`break_glass_notifications_total|event_type=${eventType}`);
}

export function recordApprovalNotification(status: string) {
  inc(`approval_notifications_total|status=${status}`);
}

export function recordReportNotification(eventType: string) {
  inc(`report_notifications_total|event_type=${eventType}`);
}

export function recordBreakGlassAuditQuery() {
  inc("break_glass_audit_queries_total|result=ok");
}

export function renderMetrics(): string {
  const lines: string[] = [];
  lines.push("# TYPE http_requests_total counter");
  lines.push("# TYPE approval_actions_total counter");
  lines.push("# TYPE break_glass_notifications_total counter");
  lines.push("# TYPE approval_notifications_total counter");
  lines.push("# TYPE report_notifications_total counter");
  lines.push("# TYPE break_glass_audit_queries_total counter");

  for (const [key, value] of counters.entries()) {
    const [name, labelsRaw] = key.split("|");
    const labels = labelsRaw
      .split("|")
      .map((pair) => {
        const [k, v] = pair.split("=");
        return `${k}="${v}"`;
      })
      .join(",");
    lines.push(`${name}{${labels}} ${value}`);
  }

  return lines.join("\n") + "\n";
}
