#!/usr/bin/env python3
import argparse
import asyncio
import os
import sys
import time
from pathlib import Path
from uuid import uuid4


ROOT = Path(__file__).resolve().parents[1]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from models.menu_extraction import PdfJobRequest
from services.pdf_menu_extraction_service import PdfExtractionTimings, PdfMenuExtractionService
from services.text_extractor import ClaudeTextExtractor, FakeTextExtractor, GeminiTextExtractor
from services.vision_extractor import ClaudeExtractor, GeminiExtractor


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


class ProviderCallStats:
    def __init__(self):
        self.active = 0
        self.peak = 0
        self.calls = 0

    def begin(self) -> None:
        self.active += 1
        self.peak = max(self.peak, self.active)
        self.calls += 1

    def end(self) -> None:
        self.active -= 1


class CountingVisionExtractor:
    def __init__(self, inner, stats: ProviderCallStats):
        self._inner = inner
        self._stats = stats

    @property
    def model_name(self):
        return self._inner.model_name

    async def extract(self, image_bytes: bytes, page_number: int):
        self._stats.begin()
        try:
            return await self._inner.extract(image_bytes=image_bytes, page_number=page_number)
        finally:
            self._stats.end()


def _real_provider_factories(provider: str):
    if provider == "gemini":
        if not os.environ.get("GEMINI_API_KEY"):
            raise RuntimeError("GEMINI_API_KEY is required for --real-provider --provider gemini.")
        return GeminiTextExtractor, GeminiExtractor
    if provider == "claude":
        if not os.environ.get("ANTHROPIC_API_KEY"):
            raise RuntimeError("ANTHROPIC_API_KEY is required for --real-provider --provider claude.")
        return ClaudeTextExtractor, ClaudeExtractor
    raise RuntimeError(f"Unsupported benchmark provider: {provider}")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Benchmark PDF menu import. Fake local providers are the default; "
            "real providers require --real-provider and matching API keys."
        )
    )
    provider_mode = parser.add_mutually_exclusive_group()
    parser.add_argument("pdf_path", help="Path to a local PDF file.")
    provider_mode.add_argument(
        "--fake-provider",
        dest="fake_provider",
        action="store_true",
        default=True,
        help="Use fake providers. This is the default and does not require API keys.",
    )
    provider_mode.add_argument(
        "--real-provider",
        action="store_false",
        dest="fake_provider",
        help="Use the selected real provider. Requires API keys.",
    )
    parser.add_argument("--provider", choices=("gemini", "claude"), default="gemini")
    parser.add_argument("--extract-concurrency", type=int, default=8)
    parser.add_argument("--provider-concurrency", type=int, default=4)
    parser.add_argument("--fake-latency-ms", type=int, default=25)
    parser.add_argument("--repeat", type=int, default=1)
    args = parser.parse_args()

    if args.extract_concurrency < 1:
        parser.error("--extract-concurrency must be >= 1.")
    if args.provider_concurrency < 1:
        parser.error("--provider-concurrency must be >= 1.")
    if args.fake_latency_ms < 0:
        parser.error("--fake-latency-ms must be >= 0.")
    if args.repeat < 1:
        parser.error("--repeat must be >= 1.")
    return args


async def run_benchmark(args: argparse.Namespace) -> int:
    pdf_path = Path(args.pdf_path).expanduser().resolve()
    if not pdf_path.exists():
        print(f"PDF not found: {pdf_path}", file=sys.stderr)
        return 2
    if not pdf_path.is_file():
        print(f"PDF path is not a file: {pdf_path}", file=sys.stderr)
        return 2

    for run_number in range(1, args.repeat + 1):
        reporter = NoopProgressReporter()
        vision_stats = ProviderCallStats()
        metrics = PdfExtractionTimings()
        if args.fake_provider:
            text_extractor_factory = lambda: FakeTextExtractor(canned_response={"items": []})
            base_vision_factory = lambda: BenchmarkVisionExtractor(args.fake_latency_ms)
            provider_name = "fake"
        else:
            try:
                text_extractor_factory, base_vision_factory = _real_provider_factories(args.provider)
            except RuntimeError as exc:
                print(str(exc), file=sys.stderr)
                return 2
            provider_name = args.provider

        def vision_extractor_factory():
            return CountingVisionExtractor(base_vision_factory(), vision_stats)

        service = PdfMenuExtractionService(
            text_extractor_factory=text_extractor_factory,
            vision_extractor_factory=vision_extractor_factory,
            progress=reporter,
            concurrency=args.extract_concurrency,
            provider_concurrency=args.provider_concurrency,
            metrics=metrics,
        )
        request = PdfJobRequest(
            job_id=uuid4(),
            restaurant_id=uuid4(),
            storage_path=str(pdf_path),
            progress_callback_url="http://localhost/noop",
            hmac_secret="benchmark-secret",
            provider=provider_name,
        )

        start = time.perf_counter()
        response = await service.run(request)
        total_seconds = time.perf_counter() - start

        pages = reporter.total_pages or len(response.items) + len(response.failed_pages)
        run_prefix = f"run={run_number} " if args.repeat > 1 else ""
        print(
            f"{run_prefix}"
            f"pages={pages} "
            f"items={len(response.items)} "
            f"failed_pages={len(response.failed_pages)} "
            f"vision_calls={vision_stats.calls} "
            f"peak_provider_concurrency={vision_stats.peak} "
            f"parse_seconds={metrics.parse_seconds:.3f} "
            f"classify_seconds={metrics.classify_seconds:.3f} "
            f"render_seconds={metrics.render_seconds:.3f} "
            f"text_provider_seconds={metrics.text_provider_seconds:.3f} "
            f"vision_provider_seconds={metrics.vision_provider_seconds:.3f} "
            f"measured_seconds={metrics.total_measured_seconds():.3f} "
            f"total_seconds={total_seconds:.3f}"
        )
    return 0


def main() -> int:
    args = parse_args()
    return asyncio.run(run_benchmark(args))


if __name__ == "__main__":
    raise SystemExit(main())
