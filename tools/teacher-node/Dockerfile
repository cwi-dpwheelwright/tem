FROM python:3.11-slim

WORKDIR /app

COPY tools/teacher-node/requirements.txt /tmp/teacher-node-requirements.txt
COPY src/etl /tmp/cwi-etl
RUN python -m pip install --no-cache-dir --upgrade pip \
    && pip install --no-cache-dir -r /tmp/teacher-node-requirements.txt \
    && pip install --no-cache-dir /tmp/cwi-etl

COPY tools/teacher-node /app/

ENV PYTHONUNBUFFERED=1

CMD ["python", "teacher_node.py", "serve", "--host", "0.0.0.0"]
