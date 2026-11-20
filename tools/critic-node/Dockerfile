FROM python:3.11-slim

WORKDIR /app

COPY tools/critic-node /app/

ENV PYTHONUNBUFFERED=1

CMD ["python", "critic_node.py", "serve", "--host", "0.0.0.0"]
