FROM python:3.11-slim

WORKDIR /app
COPY pyproject.toml /app/pyproject.toml
COPY cwi_etl /app/cwi_etl
COPY tests /app/tests

RUN pip install --no-cache-dir .

RUN useradd -m -u 10001 appuser \
    && chown -R appuser:appuser /app
USER appuser

CMD ["python", "-m", "cwi_etl"]
