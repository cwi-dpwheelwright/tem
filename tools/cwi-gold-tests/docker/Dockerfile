FROM python:3.11-slim

WORKDIR /app

COPY pyproject.toml run_tests.py run_conformance.py run_perf.py /app/
COPY tests /app/tests
COPY seeding /app/seeding

RUN python -m pip install --no-cache-dir --upgrade pip \
    && pip install --no-cache-dir .

ENV PYTHONUNBUFFERED=1

RUN useradd -m -u 10001 appuser \
    && chown -R appuser:appuser /app
USER appuser

ENTRYPOINT ["python", "run_tests.py"]
