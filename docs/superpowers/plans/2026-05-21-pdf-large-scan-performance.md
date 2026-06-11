# PDF Large Scan Performance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep large scanned/photo PDF imports from blocking the Python event loop while preserving the existing provider limiter and one-renderer-per-job behavior.

**Architecture:** Offload CPU/blocking PDF parse and PDFium render work to threads. Parsing uses `asyncio.to_thread`; rendering uses one per-job single-thread executor so PDFium remains serial and one open document is reused. A per-job payload semaphore bounds rendered JPEGs around provider calls so page work cannot rasterize far ahead of provider capacity.

**Tech Stack:** Python 3.9 asyncio, `concurrent.futures.ThreadPoolExecutor`, pytest/pytest-asyncio tests, existing `PdfMenuExtractionService` and `PdfPageRenderer`.

---

## Task 1: Offload Parse And Render

**Files:**
- Modify: `backend/ai-scripts/services/pdf_menu_extraction_service.py`
- Modify: `backend/ai-scripts/tests/test_pdf_menu_extraction_service.py`
- Modify: `docs/menu-import-pdf-smoke.md`

- [ ] **Step 1: Write failing event-loop responsiveness tests**

Add tests that monkeypatch `pdf_text_extractor.extract` and `PdfPageRenderer.render_page` to block in a worker thread while an `asyncio.Event`/ticker proves the event loop keeps running.

- [ ] **Step 2: Run focused pytest and verify RED**

Run:

```bash
cd backend/ai-scripts
python3 -m pytest tests/test_pdf_menu_extraction_service.py -k "parse_offload or render_offload" -v
```

Expected in this environment: blocked if pytest is unavailable. In a fully provisioned environment before implementation, expected behavioral failure because parse/render currently block the event loop.

- [ ] **Step 3: Implement async parse and serial render offload**

Use `asyncio.to_thread(pdf_text_extractor.extract, path)` for parsing. Convert `_LazyPageRenderer.render_page()` to async and use a per-job `ThreadPoolExecutor(max_workers=1)` around the existing synchronous renderer call. Close the renderer in that same executor and shut it down when the job finishes. Add a per-job payload semaphore sized to `provider_concurrency` and hold it from just before render through the provider call.

- [ ] **Step 4: Update vision path call sites**

Await the async page renderer in `_run_vision_for_page`. Preserve retry render reuse by caching the returned `image_bytes` inside one `_run_vision_for_page` invocation.

- [ ] **Step 5: Verify**

Run:

```bash
cd backend/ai-scripts
python3 -m py_compile services/pdf_menu_extraction_service.py tests/test_pdf_menu_extraction_service.py
python3 -m pytest tests/test_pdf_menu_extraction_service.py -k "parse_offload or render_offload or renderer_context or vision_retries_reuse_the_rendered_page" -v
cd ../..
git diff --check
```

Record missing local tooling exactly if pytest is unavailable.

- [ ] **Step 6: Checkpoint**

Suggested commit:

```bash
git add \
  backend/ai-scripts/services/pdf_menu_extraction_service.py \
  backend/ai-scripts/tests/test_pdf_menu_extraction_service.py \
  docs/menu-import-pdf-smoke.md \
  docs/superpowers/plans/2026-05-21-pdf-large-scan-performance.md

git commit -m "perf: offload large pdf parse and render work"
```
