#!/usr/bin/env python3
import argparse
import asyncio
import sys
import time
from pathlib import Path
from uuid import uuid4


ROOT = Path(__file__).resolve().parents[1]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from models.menu_extraction import PdfJobRequest
from services.pdf_menu_extraction_service import PdfMenuExtractionService
from services.text_extractor import FakeTextExtractor


class NoopProgressReporter:
    def __init__(self):
        self.total_pages = 0

    async def send(self, event):
        self.total_pages = max(self.total_pages, event.total_pages)


class BenchmarkVisionExtractor:
    model_name = "benchmark-fake-vision"

    def __init__(self, latency_ms: int):
        self.latency = latency_ms / 1000
        self.active = 0
        self.peak = 0
        self.calls = 0
        self._lock = asyncio.Lock()

    async def extract(self, image_bytes: bytes, page_number: int):
        async with self._lock:
            self.active += 1
            self.peak = max(self.peak, self.active)
            self.calls += 1
        try:
            await asyncio.sleep(self.latency)
        finally:
            async with self._lock:
                self.active -= 1

        return {
            "items": [
                {
                    "name": f"Benchmark Item {page_number}",
                    "price": 10.0,
                    "source_index": 0,
                }
            ]
        }


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Benchmark PDF menu import with fake local providers. "
            "Real provider benchmarking is not supported by this harness."
        )
    )
    parser.add_argument("pdf_path", help="Path to a local PDF file.")
    parser.add_argument(
        "--fake-provider",
        action="store_true",
        default=True,
        help="Use fake providers. This is the default and does not require API keys.",
    )
    parser.add_argument(
        "--real-provider",
        action="store_false",
        dest="fake_provider",
        help="Unsupported; this harness is fake-provider only.",
    )
    parser.add_argument("--extract-concurrency", type=int, default=8)
    parser.add_argument("--provider-concurrency", type=int, default=4)
    parser.add_argument("--fake-latency-ms", type=int, default=25)
    args = parser.parse_args()

    if not args.fake_provider:
        parser.error("real-provider mode is not supported; omit --real-provider.")
    if args.extract_concurrency < 1:
        parser.error("--extract-concurrency must be >= 1.")
    if args.provider_concurrency < 1:
        parser.error("--provider-concurrency must be >= 1.")
    if args.fake_latency_ms < 0:
        parser.error("--fake-latency-ms must be >= 0.")
    return args


async def run_benchmark(args: argparse.Namespace) -> int:
    pdf_path = Path(args.pdf_path).expanduser().resolve()
    if not pdf_path.exists():
        print(f"PDF not found: {pdf_path}", file=sys.stderr)
        return 2
    if not pdf_path.is_file():
        print(f"PDF path is not a file: {pdf_path}", file=sys.stderr)
        return 2

    reporter = NoopProgressReporter()
    vision = BenchmarkVisionExtractor(args.fake_latency_ms)
    service = PdfMenuExtractionService(
        text_extractor_factory=lambda: FakeTextExtractor(canned_response={"items": []}),
        vision_extractor_factory=lambda: vision,
        progress=reporter,
        concurrency=args.extract_concurrency,
        provider_concurrency=args.provider_concurrency,
    )
    request = PdfJobRequest(
        job_id=uuid4(),
        restaurant_id=uuid4(),
        storage_path=str(pdf_path),
        progress_callback_url="http://localhost/noop",
        hmac_secret="benchmark-secret",
        provider="fake",
    )

    start = time.perf_counter()
    response = await service.run(request)
    total_seconds = time.perf_counter() - start

    pages = reporter.total_pages or len(response.items) + len(response.failed_pages)
    print(
        f"pages={pages} "
        f"items={len(response.items)} "
        f"failed_pages={len(response.failed_pages)} "
        f"vision_calls={vision.calls} "
        f"peak_provider_concurrency={vision.peak} "
        f"total_seconds={total_seconds:.3f}"
    )
    return 0


def main() -> int:
    args = parse_args()
    return asyncio.run(run_benchmark(args))


if __name__ == "__main__":
    raise SystemExit(main())
