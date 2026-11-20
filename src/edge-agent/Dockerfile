FROM python:3.10-slim

WORKDIR /app

COPY pyproject.toml README.md /app/
COPY edge_agent /app/edge_agent
COPY configs /app/configs

RUN pip install --no-cache-dir .

ENV PYTHONUNBUFFERED=1

RUN useradd -m -u 10001 appuser \
    && chown -R appuser:appuser /app
USER appuser

CMD ["cwi-edge-agent", "run", "--config", "/etc/cwi/edge-agent.yaml", "--max-batch", "25"]
