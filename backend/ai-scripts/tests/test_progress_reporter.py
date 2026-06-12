"""
TDD — tests for services/progress_reporter.py

All tests run against a real ProgressReporter with an httpx.MockTransport so
no network is involved.
"""
import hashlib
import hmac
import json
from uuid import UUID

import httpx
import pytest

from models.menu_extraction import ProgressEvent
from services.progress_reporter import ProgressReporter


CALLBACK_URL = "http://internal.test/api/internal/menu-imports/abc123/progress"
SECRET = "supersecret1234"
JOB_ID = UUID("00000000-0000-0000-0000-000000000001")

SAMPLE_EVENT = ProgressEvent(
    job_id=JOB_ID,
    status="extracting",
    phase="parsing",
    processed_pages=0,
    total_pages=5,
    failed_pages=0,
)


def _expected_sig(body_bytes: bytes, secret: str) -> str:
    return hmac.new(secret.encode(), body_bytes, hashlib.sha256).hexdigest()


# ---------------------------------------------------------------------------
# Happy-path: correct payload and correct HMAC signature
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_send_posts_correct_payload_and_signature():
    captured: list[httpx.Request] = []

    def handler(req: httpx.Request) -> httpx.Response:
        captured.append(req)
        return httpx.Response(200)

    transport = httpx.MockTransport(handler)
    async with httpx.AsyncClient(transport=transport) as client:
        reporter = ProgressReporter(CALLBACK_URL, SECRET, client=client)
        await reporter.send(SAMPLE_EVENT)

    assert len(captured) == 1
    req = captured[0]

    # Method and URL
    assert req.method == "POST"
    assert str(req.url) == CALLBACK_URL

    # Body is valid JSON with the expected payload
    body_bytes = req.content
    parsed = json.loads(body_bytes)
    assert parsed["job_id"] == str(JOB_ID)
    assert parsed["status"] == "extracting"
    assert parsed["phase"] == "parsing"
    assert parsed["total_pages"] == 5

    # HMAC header present and correct
    assert "x-hmac-signature" in req.headers
    expected_sig = _expected_sig(body_bytes, SECRET)
    assert req.headers["x-hmac-signature"] == expected_sig


# ---------------------------------------------------------------------------
# Content-Type header
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_send_sets_content_type_json():
    captured: list[httpx.Request] = []

    def handler(req: httpx.Request) -> httpx.Response:
        captured.append(req)
        return httpx.Response(200)

    transport = httpx.MockTransport(handler)
    async with httpx.AsyncClient(transport=transport) as client:
        reporter = ProgressReporter(CALLBACK_URL, SECRET, client=client)
        await reporter.send(SAMPLE_EVENT)

    req = captured[0]
    assert "application/json" in req.headers.get("content-type", "")


# ---------------------------------------------------------------------------
# Retry on 500: succeeds on second attempt
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_send_retries_on_500_then_succeeds():
    call_count = 0

    def handler(req: httpx.Request) -> httpx.Response:
        nonlocal call_count
        call_count += 1
        if call_count < 2:
            return httpx.Response(500)
        return httpx.Response(200)

    transport = httpx.MockTransport(handler)
    async with httpx.AsyncClient(transport=transport) as client:
        reporter = ProgressReporter(CALLBACK_URL, SECRET, client=client)
        await reporter.send(SAMPLE_EVENT)  # should not raise

    assert call_count == 2


# ---------------------------------------------------------------------------
# Retry exhausted: does NOT raise (best-effort / swallows)
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_send_does_not_raise_on_all_failures():
    def handler(req: httpx.Request) -> httpx.Response:
        return httpx.Response(500)

    transport = httpx.MockTransport(handler)
    async with httpx.AsyncClient(transport=transport) as client:
        reporter = ProgressReporter(CALLBACK_URL, SECRET, client=client)
        # Must NOT raise even when every attempt fails
        await reporter.send(SAMPLE_EVENT)


# ---------------------------------------------------------------------------
# Connection-error is also retried and swallowed on final failure
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_send_swallows_connection_error():
    def handler(req: httpx.Request) -> httpx.Response:
        raise httpx.ConnectError("unreachable")

    transport = httpx.MockTransport(handler)
    async with httpx.AsyncClient(transport=transport) as client:
        reporter = ProgressReporter(CALLBACK_URL, SECRET, client=client)
        await reporter.send(SAMPLE_EVENT)  # should not raise


# ---------------------------------------------------------------------------
# Retry: up to 2 extra attempts (3 total) on connection errors
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_send_retries_up_to_three_total_attempts_on_connection_error():
    call_count = 0

    def handler(req: httpx.Request) -> httpx.Response:
        nonlocal call_count
        call_count += 1
        raise httpx.ConnectError("unreachable")

    transport = httpx.MockTransport(handler)
    async with httpx.AsyncClient(transport=transport) as client:
        reporter = ProgressReporter(CALLBACK_URL, SECRET, client=client)
        await reporter.send(SAMPLE_EVENT)

    # 1 original + 2 retries = 3 total
    assert call_count == 3
