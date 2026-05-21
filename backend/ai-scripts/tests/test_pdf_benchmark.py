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
    assert result.stdout.startswith("pages=3")
    assert "pages=3" in result.stdout
    assert "items=3" in result.stdout
    assert "peak_provider_concurrency=" in result.stdout
    assert "parse_seconds=" in result.stdout
    assert "classify_seconds=" in result.stdout
    assert "render_seconds=" in result.stdout
    assert "text_provider_seconds=" in result.stdout
    assert "vision_provider_seconds=" in result.stdout
    assert "total_seconds=" in result.stdout


def test_benchmark_pdf_import_repeat_prefixes_run_number():
    script = Path(__file__).parents[1] / "scripts" / "benchmark_pdf_import.py"
    fixture = Path(__file__).parent / "fixtures" / "sample_menus" / "scanned_3page.pdf"

    result = subprocess.run(
        [
            sys.executable,
            str(script),
            str(fixture),
            "--fake-provider",
            "--repeat",
            "2",
            "--fake-latency-ms",
            "1",
        ],
        text=True,
        capture_output=True,
        check=False,
    )

    assert result.returncode == 0, result.stderr
    lines = result.stdout.splitlines()
    assert lines[0].startswith("run=1 pages=3")
    assert lines[1].startswith("run=2 pages=3")
