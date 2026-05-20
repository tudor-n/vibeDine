"""
ProgressReporter — HMAC-signed best-effort HTTP callback for PDF job progress.

Posts ProgressEvent JSON to the C# /api/internal/menu-imports/{jobId}/progress
endpoint.  Never raises out of send() — failures are logged and swallowed.
"""
import asyncio
import hashlib
import hmac
import logging
from typing import Optional

import httpx

from models.menu_extraction import ProgressEvent


logger = logging.getLogger(__name__)

_MAX_RETRIES = 2          # 1 original attempt + 2 retries = 3 total
_BACKOFF_BASE_MS = 250    # 250 ms, 500 ms
_TIMEOUT_SECONDS = 5.0


class ProgressReporter:
    """HMAC-signed POSTs to the C# /api/internal/menu-imports/{jobId}/progress endpoint."""

    def __init__(
        self,
        callback_url: str,
        hmac_secret: str,
        *,
        client: Optional[httpx.AsyncClient] = None,
    ):
        self._callback_url = callback_url
        self._hmac_secret = hmac_secret
        self._owned_client = client is None
        self._client = client or httpx.AsyncClient(timeout=_TIMEOUT_SECONDS)

    async def send(self, event: ProgressEvent) -> None:
        """Best-effort POST.  Logs and swallows on failure.
        Retries up to _MAX_RETRIES times on connection error / 5xx with backoff.
        """
        body_bytes = event.model_dump_json().encode("utf-8")
        sig = hmac.new(
            self._hmac_secret.encode("utf-8"),
            body_bytes,
            hashlib.sha256,
        ).hexdigest()

        headers = {
            "content-type": "application/json",
            "x-hmac-signature": sig,
        }

        last_exc: Optional[Exception] = None
        for attempt in range(_MAX_RETRIES + 1):
            if attempt > 0:
                delay_seconds = (_BACKOFF_BASE_MS * attempt) / 1000.0
                await asyncio.sleep(delay_seconds)
            try:
                response = await self._client.post(
                    self._callback_url,
                    content=body_bytes,
                    headers=headers,
                    timeout=_TIMEOUT_SECONDS,
                )
                if response.status_code < 500:
                    return  # 2xx / 4xx — treat as final (don't retry client errors)
                last_exc = RuntimeError(
                    f"Progress callback returned HTTP {response.status_code}"
                )
            except (httpx.ConnectError, httpx.TransportError, httpx.TimeoutException) as exc:
                last_exc = exc

        logger.warning(
            "Progress callback failed after %d attempts for url=%r: %s",
            _MAX_RETRIES + 1,
            self._callback_url,
            last_exc,
        )

    async def aclose(self) -> None:
        if self._owned_client:
            await self._client.aclose()
