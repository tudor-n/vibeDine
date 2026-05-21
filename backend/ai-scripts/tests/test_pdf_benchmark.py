import subprocess
import sys
from pathlib import Path


def test_benchmark_pdf_import_fake_provider_smoke():
    script = Path(__file__).parents[1] / "scripts" / "benchmark_pdf_import.py"
    fixture = Path(__file__).parent / "fixtures" / "sample_menus" / "scanned_3page.pdf"

    result = subprocess.run(
        [
            sys.executable,
            str(script),
            str(fixture),
            "--fake-provider",
            "--extract-concurrency",
            "3",
            "--provider-concurrency",
            "2",
            "--fake-latency-ms",
            "1",
        ],
        text=True,
        capture_output=True,
        check=False,
    )

    assert result.returncode == 0, result.stderr
    assert "pages=3" in result.stdout
    assert "items=3" in result.stdout
    assert "peak_provider_concurrency=" in result.stdout
    assert "total_seconds=" in result.stdout
