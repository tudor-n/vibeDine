FROM python:3.11-slim
WORKDIR /app

# Install dependencies first for better layer caching.
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Copy application code.
COPY . .

# Run as an unprivileged user (defense in depth: an RCE in an image/PDF parser
# stays confined to a non-root account). Create the menu-import storage mount
# point up-front and hand both it and the app dir to the app user so a freshly
# created named volume inherits writable ownership.
RUN useradd --create-home --uid 10001 appuser \
    && mkdir -p /menu-imports \
    && chown -R appuser:appuser /app /menu-imports
USER appuser

EXPOSE 8000
CMD ["uvicorn", "main:app", "--host", "0.0.0.0", "--port", "8000"]
