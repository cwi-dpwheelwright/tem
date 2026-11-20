FROM python:3.11-slim

WORKDIR /app

COPY pyproject.toml README.md /app/
COPY node_agent_runtime /app/node_agent_runtime
COPY configs /app/configs

RUN pip install --no-cache-dir .

ENV PYTHONUNBUFFERED=1
ENV NODE_AGENT_HEALTH_PORT=8090

RUN useradd -m -u 10002 appuser \
    && mkdir -p /var/lib/cwi-node-agent \
    && chown -R appuser:appuser /app /var/lib/cwi-node-agent

USER appuser

HEALTHCHECK --interval=30s --timeout=3s --start-period=5s CMD python -c "import urllib.request; urllib.request.urlopen('http://127.0.0.1:8090/health').read()" || exit 1

CMD ["sh", "-c", "cwi-node-agent serve-health --config /etc/cwi/node-agent.yaml --host 0.0.0.0 --port ${NODE_AGENT_HEALTH_PORT:-8090}"]
