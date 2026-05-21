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
            "--vision-batch-size",
            "2",
            "--provider-timeout-seconds",
            "12.5",
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
    assert "vision_batch_size=2" in result.stdout
    assert "provider_timeout_seconds=12.5" in result.stdout
    assert "parse_seconds=" in result.stdout
    assert "classify_seconds=" in result.stdout
    assert "render_seconds=" in result.stdout
    assert "render_scale=1.5" in result.stdout
    assert "render_quality=85" in result.stdout
    assert "rendered_pages=" in result.stdout
    assert "rendered_bytes=" in result.stdout
    assert "avg_rendered_bytes=" in result.stdout
    assert "embedded_image_pages=" in result.stdout
    assert "embedded_image_bytes=" in result.stdout
    assert "skipped_embedded_image_pages=" in result.stdout
    assert "skipped_embedded_image_bytes=" in result.stdout
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


def test_benchmark_pdf_import_accepts_render_tuning_args():
    script = Path(__file__).parents[1] / "scripts" / "benchmark_pdf_import.py"
    fixture = Path(__file__).parent / "fixtures" / "sample_menus" / "scanned_3page.pdf"

    result = subprocess.run(
        [
            sys.executable,
            str(script),
            str(fixture),
            "--fake-provider",
            "--render-scale",
            "1.0",
            "--render-quality",
            "70",
            "--fake-latency-ms",
            "1",
        ],
        text=True,
        capture_output=True,
        check=False,
    )

    assert result.returncode == 0, result.stderr
    assert "render_scale=1" in result.stdout
    assert "render_quality=70" in result.stdout
    assert "rendered_pages=" in result.stdout
    assert "rendered_bytes=" in result.stdout
    assert "avg_rendered_bytes=" in result.stdout
    assert "embedded_image_pages=" in result.stdout
    assert "embedded_image_bytes=" in result.stdout
    assert "skipped_embedded_image_pages=" in result.stdout
    assert "skipped_embedded_image_bytes=" in result.stdout


def test_benchmark_pdf_import_accepts_embedded_image_byte_budget():
    script = Path(__file__).parents[1] / "scripts" / "benchmark_pdf_import.py"
    fixture = Path(__file__).parent / "fixtures" / "sample_menus" / "scanned_3page.pdf"

    result = subprocess.run(
        [
            sys.executable,
            str(script),
            str(fixture),
            "--fake-provider",
            "--max-embedded-image-bytes",
            "0",
            "--fake-latency-ms",
            "1",
        ],
        text=True,
        capture_output=True,
        check=False,
    )

    assert result.returncode == 0, result.stderr
    assert "max_embedded_image_bytes=0" in result.stdout
    assert "embedded_image_pages=0" in result.stdout


def test_benchmark_pdf_import_rejects_invalid_vision_batch_size():
    script = Path(__file__).parents[1] / "scripts" / "benchmark_pdf_import.py"
    fixture = Path(__file__).parent / "fixtures" / "sample_menus" / "scanned_3page.pdf"

    result = subprocess.run(
        [
            sys.executable,
            str(script),
            str(fixture),
            "--fake-provider",
            "--vision-batch-size",
            "0",
        ],
        text=True,
        capture_output=True,
        check=False,
    )

    assert result.returncode == 2, result.stderr
    assert "--vision-batch-size must be >= 1." in result.stderr


def test_benchmark_pdf_import_rejects_invalid_provider_timeout():
    script = Path(__file__).parents[1] / "scripts" / "benchmark_pdf_import.py"
    fixture = Path(__file__).parent / "fixtures" / "sample_menus" / "scanned_3page.pdf"

    for value in ("0", "nan", "inf", "-inf"):
        result = subprocess.run(
            [
                sys.executable,
                str(script),
                str(fixture),
                "--fake-provider",
                "--provider-timeout-seconds",
                value,
            ],
            text=True,
            capture_output=True,
            check=False,
        )

        assert result.returncode == 2, result.stderr
        assert "--provider-timeout-seconds" in result.stderr


def test_benchmark_pdf_import_rejects_non_finite_render_scale():
    script = Path(__file__).parents[1] / "scripts" / "benchmark_pdf_import.py"
    fixture = Path(__file__).parent / "fixtures" / "sample_menus" / "scanned_3page.pdf"

    for value in ("nan", "inf", "-inf"):
        result = subprocess.run(
            [
                sys.executable,
                str(script),
                str(fixture),
                "--fake-provider",
                "--render-scale",
                value,
            ],
            text=True,
            capture_output=True,
            check=False,
        )

        assert result.returncode == 2, result.stderr
        assert "--render-scale must be finite." in result.stderr


def test_benchmark_pdf_import_real_provider_without_key_exits_cleanly():
    script = Path(__file__).parents[1] / "scripts" / "benchmark_pdf_import.py"
    fixture = Path(__file__).parent / "fixtures" / "sample_menus" / "scanned_3page.pdf"

    result = subprocess.run(
        [
            sys.executable,
            str(script),
            str(fixture),
            "--real-provider",
            "--provider",
            "gemini",
        ],
        text=True,
        capture_output=True,
        check=False,
        env={
            "PATH": "/usr/bin:/bin",
            "PYTHONPATH": str(Path(__file__).parents[1]),
        },
    )

    assert result.returncode == 2
    assert "GEMINI_API_KEY" in result.stderr
    assert "not supported" not in result.stderr
    assert "Traceback" not in result.stderr
