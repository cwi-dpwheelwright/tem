FROM python:3.10-slim

WORKDIR /app

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1

COPY requirements.txt ./
RUN pip install --no-cache-dir -r requirements.txt

COPY pin_resolver.py ./

EXPOSE 8090

CMD ["gunicorn", "-w", "2", "-k", "gthread", "--threads", "4", "--timeout", "15", "-b", "0.0.0.0:8090", "pin_resolver:APP"]

