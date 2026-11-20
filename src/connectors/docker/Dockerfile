FROM python:3.11-slim

RUN useradd -m cwi

WORKDIR /app
COPY pyproject.toml /app/pyproject.toml
COPY cwi_connectors /app/cwi_connectors

RUN pip install --no-cache-dir .

USER cwi
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s CMD python -c "import urllib.request; urllib.request.urlopen('http://localhost:8080/health').read()" || exit 1

COPY configs /app/config

CMD ["python", "-m", "cwi_connectors", "--config", "/app/config/connectors.yaml"]
