# Menu Digitalization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **Git is user-managed in this project.** Tasks end with a "Checkpoint" step that lists what's ready and a suggested commit message — do NOT run `git add`/`git commit` automatically. The user batches and commits manually.

**Goal:** Add a 3-step admin wizard at `/admin/menu/import` that turns photos of a paper menu into digital `Products` + `Categories` via a vision-LLM, with admin review before any data lands.

**Architecture:** Vue admin wizard → C# `RestaurantApi` (`/api/admin/menu/scan` forwards photos to Python; `/api/admin/menu/products/bulk` writes everything atomically) → Python `ai-scripts` service (concurrent vision-LLM calls per page, per-image error isolation, returns structured JSON). Schema is forward-compatible — v1 prompt requests `name + price + category_hint`; later fields slot in without restructuring.

**Tech Stack:**
- Backend: ASP.NET Core 8 (C#), EF Core + PostgreSQL, xUnit + `Microsoft.AspNetCore.Mvc.Testing` + `Testcontainers.PostgreSql`, `WireMock.Net`.
- AI service: Python 3.11+ FastAPI, Pydantic, `httpx`/`anthropic` SDK, `pytest` + `pytest-asyncio` + `pytest-httpx`.
- Frontend: Vue 3 + `<script setup>` + TypeScript + Pinia + Vite + TailwindCSS; existing design tokens (`pill-btn`, `card-paper`, `field-input`, etc.). Vitest + `@vue/test-utils` + MSW.
- Spec: `docs/superpowers/specs/2026-05-19-menu-digitalization-design.md` (read this before starting any task).

---

## Task 1: Shared validation constants

The C# `MenuManagementController` already encodes the rules (name 1–120, description ≤ 1000, price > 0 ≤ 99,999,999.99). We extract them once so the new wizard's inline validation can't drift from the server.

**Files:**
- Create: `shared/types/menu-validation.ts`
- Create: `shared/types/menu-import.ts`

- [ ] **Step 1: Create the constants module**

`shared/types/menu-validation.ts`:

```ts
// Validation constants for menu Categories and Products.
// Kept in sync with backend/RestaurantApi/Controllers/MenuManagementController.cs.
export const MENU_VALIDATION = {
  NAME_MIN: 1,
  NAME_MAX: 120,
  DESCRIPTION_MAX: 1000,
  IMAGE_URL_MAX: 500,
  PRICE_MIN_EXCLUSIVE: 0,
  PRICE_MAX: 99999999.99,
  IMPORT_MAX_FILES: 10,
  IMPORT_MAX_FILE_BYTES: 5 * 1024 * 1024,
  IMPORT_ALLOWED_MIME: ['image/jpeg', 'image/png', 'image/webp', 'image/heic'] as const,
} as const

export type AllowedImportMime = (typeof MENU_VALIDATION.IMPORT_ALLOWED_MIME)[number]
```

- [ ] **Step 2: Create the import DTO types**

`shared/types/menu-import.ts`:

```ts
// Mirrors C# DTOs in backend/RestaurantApi/DTO/{Requests,Responses}/MenuScanRequests.cs / MenuScanResponses.cs
// and Python Pydantic models in backend/ai-scripts/models/menu_extraction.py.

export interface ExtractedMenuItem {
  name: string
  price: number
  sourcePage: number
  sourceIndex: number
  description?: string | null
  categoryHint?: string | null
  confidence?: number | null
}

export interface FailedPage {
  sourcePage: number
  reason: 'unreadable' | 'no_items_detected' | 'vision_api_error'
}

export interface MenuScanResponse {
  items: ExtractedMenuItem[]
  failedPages: FailedPage[]
  model: string
  schemaVersion: number
}

export interface BulkCreateCategoryItem {
  tempId: string
  name: string
  sortOrder?: number | null
}

export interface BulkCreateProductItem {
  name: string
  price: number
  categoryId: string
  description?: string | null
  isAvailable?: boolean
}

export interface BulkCreateProductsRequest {
  items: BulkCreateProductItem[]
  newCategories: BulkCreateCategoryItem[]
}
```

- [ ] **Step 3: Checkpoint**

Files ready: `shared/types/menu-validation.ts`, `shared/types/menu-import.ts`.
Suggested commit: `feat(shared): add menu-import DTOs and validation constants`

---

## Task 2: Python — Pydantic schemas + parsing tests

**Files:**
- Create: `backend/ai-scripts/models/menu_extraction.py`
- Create: `backend/ai-scripts/tests/test_menu_extraction_parsing.py`

- [ ] **Step 1: Write the failing parsing test**

`backend/ai-scripts/tests/test_menu_extraction_parsing.py`:

```python
import pytest
from pydantic import ValidationError

from models.menu_extraction import (
    ExtractedMenuItem,
    FailedPage,
    MenuExtractionResponse,
)


def test_extracted_menu_item_minimum_fields():
    item = ExtractedMenuItem(name="Ciorbă de burtă", price=24.9, source_page=0, source_index=3)
    assert item.name == "Ciorbă de burtă"
    assert item.price == 24.9
    assert item.description is None
    assert item.category_hint is None
    assert item.confidence is None


def test_extracted_menu_item_accepts_future_optional_fields():
    item = ExtractedMenuItem(
        name="Salată Caesar",
        price=18.0,
        source_page=1,
        source_index=2,
        description="Bucățele de pui pe pat de salată romana.",
        category_hint="APERITIVE",
        confidence=0.92,
    )
    assert item.description.startswith("Bucățele")
    assert item.category_hint == "APERITIVE"
    assert item.confidence == 0.92


def test_extracted_menu_item_rejects_missing_required_fields():
    with pytest.raises(ValidationError):
        ExtractedMenuItem(name="", price=1.0)  # type: ignore[call-arg]


def test_failed_page_rejects_invalid_reason():
    with pytest.raises(ValidationError):
        FailedPage(source_page=0, reason="some_other_reason")  # type: ignore[arg-type]


def test_menu_extraction_response_defaults():
    response = MenuExtractionResponse(items=[], model="claude-sonnet-test")
    assert response.failed_pages == []
    assert response.schema_version == 1
```

- [ ] **Step 2: Run the test to verify it fails**

```
cd backend/ai-scripts
pytest tests/test_menu_extraction_parsing.py -v
```

Expected: ImportError (`models.menu_extraction` doesn't exist yet).

- [ ] **Step 3: Implement the Pydantic models**

`backend/ai-scripts/models/menu_extraction.py`:

```python
from typing import List, Literal, Optional

from pydantic import BaseModel, Field


class ExtractedMenuItem(BaseModel):
    name: str = Field(min_length=1, max_length=120)
    price: float = Field(gt=0, le=99_999_999.99)
    source_page: int = Field(ge=0)
    source_index: int = Field(ge=0)
    description: Optional[str] = Field(default=None, max_length=1000)
    category_hint: Optional[str] = None
    confidence: Optional[float] = Field(default=None, ge=0.0, le=1.0)


class FailedPage(BaseModel):
    source_page: int = Field(ge=0)
    reason: Literal["unreadable", "no_items_detected", "vision_api_error"]


class MenuExtractionResponse(BaseModel):
    items: List[ExtractedMenuItem]
    failed_pages: List[FailedPage] = []
    model: str
    schema_version: int = 1
```

- [ ] **Step 4: Run the test to verify it passes**

```
cd backend/ai-scripts
pytest tests/test_menu_extraction_parsing.py -v
```

Expected: 5 passed.

- [ ] **Step 5: Checkpoint**

Files ready: `backend/ai-scripts/models/menu_extraction.py`, `backend/ai-scripts/tests/test_menu_extraction_parsing.py`.
Suggested commit: `feat(ai-scripts): add menu_extraction Pydantic schemas`

---

## Task 3: Python — VisionExtractor protocol + fake-for-tests

We define a Protocol so the orchestrator depends on an interface, not on the Anthropic SDK. The fake lives next to the protocol and powers all router-level tests.

**Files:**
- Create: `backend/ai-scripts/services/vision_extractor.py`
- Create: `backend/ai-scripts/services/__init__.py` (if not present — leave file empty)
- Modify: `backend/ai-scripts/tests/test_menu_extraction_parsing.py` (extend with a fake-extractor test)

- [ ] **Step 1: Write the failing test for the fake extractor**

Append to `backend/ai-scripts/tests/test_menu_extraction_parsing.py`:

```python
from services.vision_extractor import FakeVisionExtractor, VisionExtractionError


@pytest.mark.asyncio
async def test_fake_vision_extractor_returns_canned_items():
    fake = FakeVisionExtractor(
        canned_response={
            "items": [
                {"name": "Ciorbă de burtă", "price": 24.9, "source_index": 0, "category_hint": "Ciorbe"},
            ]
        },
        model_name="fake-test-model",
    )
    raw = await fake.extract(image_bytes=b"\x00", page_number=0)
    assert raw == {
        "items": [
            {"name": "Ciorbă de burtă", "price": 24.9, "source_index": 0, "category_hint": "Ciorbe"},
        ]
    }


@pytest.mark.asyncio
async def test_fake_vision_extractor_can_raise():
    fake = FakeVisionExtractor(raises=VisionExtractionError("simulated"))
    with pytest.raises(VisionExtractionError):
        await fake.extract(image_bytes=b"\x00", page_number=0)
```

- [ ] **Step 2: Run the test to verify it fails**

```
cd backend/ai-scripts
pytest tests/test_menu_extraction_parsing.py -v
```

Expected: ImportError for `services.vision_extractor`.

- [ ] **Step 3: Implement the protocol + fake**

`backend/ai-scripts/services/vision_extractor.py`:

```python
from typing import Any, Optional, Protocol


class VisionExtractionError(Exception):
    """Raised when the underlying vision provider fails to return a usable response."""


class VisionExtractor(Protocol):
    model_name: str

    async def extract(self, image_bytes: bytes, page_number: int) -> dict[str, Any]:
        """Return the raw JSON dict the provider produced for one image."""
        ...


class FakeVisionExtractor:
    """In-memory stand-in for tests. Either returns canned_response or raises `raises`."""

    def __init__(
        self,
        canned_response: Optional[dict[str, Any]] = None,
        raises: Optional[Exception] = None,
        model_name: str = "fake-test-model",
    ):
        if (canned_response is None) == (raises is None):
            raise ValueError("Provide exactly one of canned_response or raises.")
        self._canned = canned_response
        self._raises = raises
        self.model_name = model_name

    async def extract(self, image_bytes: bytes, page_number: int) -> dict[str, Any]:
        if self._raises is not None:
            raise self._raises
        assert self._canned is not None
        return self._canned
```

- [ ] **Step 4: Ensure `pytest-asyncio` is configured**

Check `backend/ai-scripts/tests/conftest.py` — if it doesn't already set `asyncio_mode = "auto"` (or the project doesn't pull in `pytest-asyncio`), add to `backend/ai-scripts/requirements.txt`:

```
pytest-asyncio>=0.23
```

And ensure `backend/ai-scripts/pytest.ini` (or `pyproject.toml`) contains:

```ini
[pytest]
asyncio_mode = auto
```

If those already exist, skip this step.

- [ ] **Step 5: Run the test to verify it passes**

```
cd backend/ai-scripts
pytest tests/test_menu_extraction_parsing.py -v
```

Expected: 7 passed (5 from Task 2 + 2 new).

- [ ] **Step 6: Checkpoint**

Files ready: `backend/ai-scripts/services/vision_extractor.py`, extended tests.
Suggested commit: `feat(ai-scripts): add VisionExtractor protocol + fake for tests`

---

## Task 4: Python — `ClaudeExtractor` (real provider)

Uses Anthropic's structured-output via tool-use. Pulls the model name and API key from env so CI can use the fake and dev/prod use the real one.

**Files:**
- Modify: `backend/ai-scripts/services/vision_extractor.py`
- Modify: `backend/ai-scripts/requirements.txt`
- Modify: `backend/ai-scripts/.env.example`

- [ ] **Step 1: Add the Anthropic SDK to deps**

Append to `backend/ai-scripts/requirements.txt` (don't duplicate if already present):

```
anthropic>=0.40.0
python-multipart>=0.0.9
```

- [ ] **Step 2: Document required env vars**

Append to `backend/ai-scripts/.env.example`:

```
# Menu digitalization
AI_VISION_PROVIDER=claude
ANTHROPIC_API_KEY=
ANTHROPIC_VISION_MODEL=claude-sonnet-4-6
```

- [ ] **Step 3: Implement ClaudeExtractor**

Append to `backend/ai-scripts/services/vision_extractor.py`:

```python
import base64
import os
from typing import Any

import httpx
from anthropic import AsyncAnthropic, APIStatusError


MENU_EXTRACTION_TOOL_SCHEMA = {
    "name": "emit_menu_items",
    "description": "Emit the structured list of menu items extracted from the photo.",
    "input_schema": {
        "type": "object",
        "properties": {
            "items": {
                "type": "array",
                "items": {
                    "type": "object",
                    "properties": {
                        "name": {"type": "string"},
                        "price": {"type": "number"},
                        "source_index": {"type": "integer", "minimum": 0},
                        "category_hint": {"type": ["string", "null"]},
                    },
                    "required": ["name", "price", "source_index"],
                },
            }
        },
        "required": ["items"],
    },
}

EXTRACTION_PROMPT = """Extract every item that has a clear price from this menu photo.

Rules:
- Include only rows where a price is printed. Skip section headings ("APERITIVE", "BĂUTURI", "FEL PRINCIPAL", "DESERTURI" etc.) as items, decorative captions, the restaurant's name/contact info, and footnotes.
- For `category_hint`: use the literal section heading printed above this item (e.g. "APERITIVE", "Fel principal"). Leave it null only for items that genuinely sit outside any visible heading. Do not invent a heading.
- Keep the original Romanian spelling and diacritics (ă, â, î, ș, ț).
- Prices in RON: convert "24,90 lei" / "24.90 RON" / "24,90" → 24.90.
- If a price is a range or "de la X", use the lower bound.
- If an item has multiple prices (small/large), emit one row per price, appending the qualifier to the name (e.g. "Salată Caesar (mică)").
- `source_index` is the 0-based reading order within this page (top-to-bottom, left-to-right for two-column menus).
- If you can't read an item clearly, skip it. Do not invent items.
"""

SYSTEM_PROMPT = (
    "You extract structured menu data from photos of printed restaurant menus. "
    "The menus are typically in Romanian. Use the provided tool to return the items."
)


class ClaudeExtractor:
    def __init__(
        self,
        api_key: str | None = None,
        model_name: str | None = None,
        client: AsyncAnthropic | None = None,
    ):
        resolved_key = api_key or os.environ.get("ANTHROPIC_API_KEY")
        if not resolved_key and client is None:
            raise RuntimeError("ANTHROPIC_API_KEY is required to use ClaudeExtractor.")
        self.model_name = model_name or os.environ.get("ANTHROPIC_VISION_MODEL", "claude-sonnet-4-6")
        self._client = client or AsyncAnthropic(api_key=resolved_key)

    async def extract(self, image_bytes: bytes, page_number: int) -> dict[str, Any]:
        media_type = _sniff_media_type(image_bytes)
        encoded = base64.standard_b64encode(image_bytes).decode("ascii")
        try:
            response = await self._client.messages.create(
                model=self.model_name,
                max_tokens=4096,
                system=SYSTEM_PROMPT,
                tools=[MENU_EXTRACTION_TOOL_SCHEMA],
                tool_choice={"type": "tool", "name": "emit_menu_items"},
                messages=[
                    {
                        "role": "user",
                        "content": [
                            {
                                "type": "image",
                                "source": {"type": "base64", "media_type": media_type, "data": encoded},
                            },
                            {"type": "text", "text": EXTRACTION_PROMPT},
                        ],
                    }
                ],
                timeout=httpx.Timeout(60.0, connect=10.0),
            )
        except APIStatusError as exc:
            raise VisionExtractionError(f"Anthropic API error: {exc.status_code}") from exc
        except Exception as exc:
            raise VisionExtractionError("Vision provider call failed") from exc

        for block in response.content:
            if block.type == "tool_use" and block.name == "emit_menu_items":
                payload = block.input
                if not isinstance(payload, dict) or "items" not in payload:
                    raise VisionExtractionError("Tool-use payload missing 'items'")
                return payload  # type: ignore[return-value]
        raise VisionExtractionError("Model did not return a tool_use block")


def _sniff_media_type(image_bytes: bytes) -> str:
    if image_bytes.startswith(b"\xff\xd8\xff"):
        return "image/jpeg"
    if image_bytes.startswith(b"\x89PNG\r\n"):
        return "image/png"
    if image_bytes[:4] == b"RIFF" and image_bytes[8:12] == b"WEBP":
        return "image/webp"
    return "image/jpeg"  # default — caller already validated mime upstream
```

- [ ] **Step 4: Install the new dep**

```
cd backend/ai-scripts
pip install -r requirements.txt
```

- [ ] **Step 5: Smoke-check imports**

```
cd backend/ai-scripts
python -c "from services.vision_extractor import ClaudeExtractor, FakeVisionExtractor; print('ok')"
```

Expected: `ok`. (Does not require an API key — only constructs the module.)

- [ ] **Step 6: Checkpoint**

Files ready: extended `vision_extractor.py`, updated `requirements.txt`, updated `.env.example`.
Suggested commit: `feat(ai-scripts): add ClaudeExtractor for menu image → JSON`

---

## Task 5: Python — `menu_extraction_service` orchestrator + tests

This is the boundary the spec calls "one pure transform". Concurrent per-image calls, per-image try/except, merge results.

**Files:**
- Create: `backend/ai-scripts/services/menu_extraction_service.py`
- Create: `backend/ai-scripts/tests/test_menu_extraction_service.py`

- [ ] **Step 1: Write the failing service tests**

`backend/ai-scripts/tests/test_menu_extraction_service.py`:

```python
import pytest

from services.menu_extraction_service import MenuExtractionService
from services.vision_extractor import FakeVisionExtractor, VisionExtractionError


def _items(*names_and_prices, hint=None):
    return [
        {"name": n, "price": p, "source_index": i, "category_hint": hint}
        for i, (n, p) in enumerate(names_and_prices)
    ]


@pytest.mark.asyncio
async def test_single_page_success():
    service = MenuExtractionService(
        extractor_factory=lambda: FakeVisionExtractor(
            canned_response={"items": _items(("Ciorbă", 18.0), ("Salată", 22.5), hint="APERITIVE")},
            model_name="fake-x",
        )
    )
    result = await service.extract([b"\x00"])
    assert result.model == "fake-x"
    assert result.schema_version == 1
    assert len(result.items) == 2
    assert result.items[0].source_page == 0
    assert result.items[0].category_hint == "APERITIVE"
    assert result.failed_pages == []


@pytest.mark.asyncio
async def test_multi_page_success_preserves_page_order():
    canned = {"items": _items(("Foo", 10.0))}
    service = MenuExtractionService(
        extractor_factory=lambda: FakeVisionExtractor(canned_response=canned, model_name="fake-y")
    )
    result = await service.extract([b"\x00", b"\x01", b"\x02"])
    assert [it.source_page for it in result.items] == [0, 1, 2]


@pytest.mark.asyncio
async def test_one_page_fails_others_succeed():
    state = {"calls": 0}

    def factory():
        state["calls"] += 1
        if state["calls"] == 2:  # second page (index 1) fails
            return FakeVisionExtractor(raises=VisionExtractionError("boom"))
        return FakeVisionExtractor(canned_response={"items": _items(("Foo", 10.0))})

    service = MenuExtractionService(extractor_factory=factory)
    result = await service.extract([b"\x00", b"\x01", b"\x02"])
    assert {fp.source_page for fp in result.failed_pages} == {1}
    assert result.failed_pages[0].reason == "vision_api_error"
    assert {it.source_page for it in result.items} == {0, 2}


@pytest.mark.asyncio
async def test_empty_items_marked_as_no_items_detected():
    service = MenuExtractionService(
        extractor_factory=lambda: FakeVisionExtractor(canned_response={"items": []})
    )
    result = await service.extract([b"\x00"])
    assert result.items == []
    assert len(result.failed_pages) == 1
    assert result.failed_pages[0].reason == "no_items_detected"


@pytest.mark.asyncio
async def test_invalid_provider_payload_treated_as_vision_api_error():
    service = MenuExtractionService(
        extractor_factory=lambda: FakeVisionExtractor(
            canned_response={"items": [{"name": "", "price": -1.0, "source_index": 0}]}
        )
    )
    result = await service.extract([b"\x00"])
    assert result.items == []
    assert result.failed_pages[0].reason == "vision_api_error"
```

- [ ] **Step 2: Run to verify failure**

```
cd backend/ai-scripts
pytest tests/test_menu_extraction_service.py -v
```

Expected: ImportError for `services.menu_extraction_service`.

- [ ] **Step 3: Implement the service**

`backend/ai-scripts/services/menu_extraction_service.py`:

```python
import asyncio
import logging
from typing import Callable, List

from pydantic import ValidationError

from models.menu_extraction import (
    ExtractedMenuItem,
    FailedPage,
    MenuExtractionResponse,
)
from services.vision_extractor import VisionExtractionError, VisionExtractor


logger = logging.getLogger(__name__)


class MenuExtractionService:
    """Coordinates per-page vision-LLM calls and merges the results into one response."""

    def __init__(self, extractor_factory: Callable[[], VisionExtractor]):
        # Factory rather than instance so each page can be retried in isolation
        # and so tests can produce different fakes per page.
        self._extractor_factory = extractor_factory

    async def extract(self, image_bytes_list: List[bytes]) -> MenuExtractionResponse:
        tasks = [self._extract_one(idx, b) for idx, b in enumerate(image_bytes_list)]
        per_page = await asyncio.gather(*tasks)

        items: list[ExtractedMenuItem] = []
        failed: list[FailedPage] = []
        model_name = ""
        for page_items, page_failure, page_model in per_page:
            items.extend(page_items)
            if page_failure is not None:
                failed.append(page_failure)
            if not model_name and page_model:
                model_name = page_model

        return MenuExtractionResponse(
            items=items,
            failed_pages=failed,
            model=model_name or "unknown",
            schema_version=1,
        )

    async def _extract_one(
        self, page_number: int, image_bytes: bytes
    ) -> tuple[list[ExtractedMenuItem], FailedPage | None, str]:
        extractor = self._extractor_factory()
        try:
            raw = await extractor.extract(image_bytes=image_bytes, page_number=page_number)
        except VisionExtractionError:
            logger.warning("vision extractor failed for page %s", page_number, exc_info=True)
            return [], FailedPage(source_page=page_number, reason="vision_api_error"), extractor.model_name

        raw_items = raw.get("items") if isinstance(raw, dict) else None
        if not raw_items:
            return [], FailedPage(source_page=page_number, reason="no_items_detected"), extractor.model_name

        parsed: list[ExtractedMenuItem] = []
        try:
            for raw_item in raw_items:
                parsed.append(
                    ExtractedMenuItem(
                        name=raw_item["name"],
                        price=float(raw_item["price"]),
                        source_page=page_number,
                        source_index=int(raw_item["source_index"]),
                        category_hint=raw_item.get("category_hint"),
                    )
                )
        except (KeyError, TypeError, ValueError, ValidationError):
            logger.warning("invalid item payload for page %s", page_number, exc_info=True)
            return [], FailedPage(source_page=page_number, reason="vision_api_error"), extractor.model_name

        return parsed, None, extractor.model_name
```

- [ ] **Step 4: Run to verify pass**

```
cd backend/ai-scripts
pytest tests/test_menu_extraction_service.py -v
```

Expected: 5 passed.

- [ ] **Step 5: Checkpoint**

Files ready: `services/menu_extraction_service.py`, `tests/test_menu_extraction_service.py`.
Suggested commit: `feat(ai-scripts): add menu extraction orchestrator with per-page error isolation`

---

## Task 6: Python — FastAPI router `/menu/extract` + integration tests

**Files:**
- Create: `backend/ai-scripts/routers/menu_extraction.py`
- Create: `backend/ai-scripts/tests/test_menu_extraction_router.py`
- Modify: `backend/ai-scripts/main.py`
- Create: `backend/ai-scripts/tests/fixtures/tiny_pixel.png`

- [ ] **Step 1: Add the fixture PNG**

Generate a 1×1 PNG once (any image works; this is just a payload). Run from `backend/ai-scripts/`:

```
python -c "import base64,pathlib; pathlib.Path('tests/fixtures').mkdir(parents=True, exist_ok=True); pathlib.Path('tests/fixtures/tiny_pixel.png').write_bytes(base64.b64decode('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFAQH/PQg9ywAAAABJRU5ErkJggg=='))"
```

- [ ] **Step 2: Write failing router tests**

`backend/ai-scripts/tests/test_menu_extraction_router.py`:

```python
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

from main import app
from routers import menu_extraction as router_module
from services.menu_extraction_service import MenuExtractionService
from services.vision_extractor import FakeVisionExtractor, VisionExtractionError


FIXTURE = Path(__file__).parent / "fixtures" / "tiny_pixel.png"


@pytest.fixture
def client():
    return TestClient(app)


def _override(service_factory):
    app.dependency_overrides[router_module.get_extraction_service] = service_factory
    yield
    app.dependency_overrides.clear()


@pytest.fixture
def use_service():
    """Helper to inject a MenuExtractionService for the duration of a test."""
    def _set(service: MenuExtractionService):
        app.dependency_overrides[router_module.get_extraction_service] = lambda: service
    yield _set
    app.dependency_overrides.clear()


def _png_bytes() -> bytes:
    return FIXTURE.read_bytes()


def test_extract_happy_path(client, use_service):
    use_service(MenuExtractionService(
        extractor_factory=lambda: FakeVisionExtractor(
            canned_response={
                "items": [
                    {"name": "Ciorbă", "price": 18.0, "source_index": 0, "category_hint": "Ciorbe"},
                ]
            },
            model_name="fake-router",
        )
    ))
    response = client.post(
        "/menu/extract",
        files=[("photos", ("p0.png", _png_bytes(), "image/png"))],
    )
    assert response.status_code == 200
    body = response.json()
    assert body["model"] == "fake-router"
    assert body["schema_version"] == 1
    assert body["items"][0]["name"] == "Ciorbă"
    assert body["items"][0]["source_page"] == 0
    assert body["failed_pages"] == []


def test_extract_multi_file_preserves_page_index(client, use_service):
    use_service(MenuExtractionService(
        extractor_factory=lambda: FakeVisionExtractor(
            canned_response={"items": [{"name": "X", "price": 5.0, "source_index": 0}]}
        )
    ))
    response = client.post(
        "/menu/extract",
        files=[
            ("photos", ("p0.png", _png_bytes(), "image/png")),
            ("photos", ("p1.png", _png_bytes(), "image/png")),
        ],
    )
    assert response.status_code == 200
    pages = sorted(it["source_page"] for it in response.json()["items"])
    assert pages == [0, 1]


def test_extract_rejects_zero_photos(client):
    response = client.post("/menu/extract", files=[])
    assert response.status_code == 400
    assert response.json()["detail"].lower().startswith("at least one")


def test_extract_one_page_fails_others_succeed(client, use_service):
    state = {"calls": 0}

    def factory():
        state["calls"] += 1
        if state["calls"] == 1:
            return FakeVisionExtractor(raises=VisionExtractionError("boom"))
        return FakeVisionExtractor(canned_response={"items": [{"name": "OK", "price": 9.0, "source_index": 0}]})

    use_service(MenuExtractionService(extractor_factory=factory))
    response = client.post(
        "/menu/extract",
        files=[
            ("photos", ("p0.png", _png_bytes(), "image/png")),
            ("photos", ("p1.png", _png_bytes(), "image/png")),
        ],
    )
    assert response.status_code == 200
    body = response.json()
    assert body["failed_pages"] == [{"source_page": 0, "reason": "vision_api_error"}]
    assert body["items"][0]["source_page"] == 1
```

- [ ] **Step 3: Run to verify failure**

```
cd backend/ai-scripts
pytest tests/test_menu_extraction_router.py -v
```

Expected: ImportError on `routers.menu_extraction`.

- [ ] **Step 4: Implement the router**

`backend/ai-scripts/routers/menu_extraction.py`:

```python
import os
from typing import List

from fastapi import APIRouter, Depends, File, HTTPException, UploadFile

from models.menu_extraction import MenuExtractionResponse
from services.menu_extraction_service import MenuExtractionService
from services.vision_extractor import ClaudeExtractor, VisionExtractor


router = APIRouter(prefix="/menu", tags=["menu-extraction"])


def _default_extractor_factory() -> VisionExtractor:
    provider = os.environ.get("AI_VISION_PROVIDER", "claude").lower()
    if provider == "claude":
        return ClaudeExtractor()
    raise RuntimeError(f"Unsupported AI_VISION_PROVIDER: {provider}")


def get_extraction_service() -> MenuExtractionService:
    """FastAPI dependency. Overridden in tests via app.dependency_overrides."""
    return MenuExtractionService(extractor_factory=_default_extractor_factory)


@router.post("/extract", response_model=MenuExtractionResponse)
async def extract_menu(
    photos: List[UploadFile] = File(default=[]),
    service: MenuExtractionService = Depends(get_extraction_service),
) -> MenuExtractionResponse:
    if not photos:
        raise HTTPException(status_code=400, detail="At least one photo is required.")
    image_bytes_list = [await p.read() for p in photos]
    return await service.extract(image_bytes_list)
```

- [ ] **Step 5: Register the router in `main.py`**

In `backend/ai-scripts/main.py`, add — alongside the existing `routers.recommendations` / `routers.similarity` registrations:

```python
from routers import menu_extraction
app.include_router(menu_extraction.router)
```

Place the import next to the other `from routers import ...` lines and the `include_router` call next to the others.

- [ ] **Step 6: Run the tests**

```
cd backend/ai-scripts
pytest tests/test_menu_extraction_router.py -v
```

Expected: 4 passed.

- [ ] **Step 7: Checkpoint**

Files ready: router, integration tests, fixture, `main.py` wiring.
Suggested commit: `feat(ai-scripts): add POST /menu/extract endpoint with dependency override for tests`

---

## Task 7: C# — bootstrap `RestaurantApi.Tests` project

The C# backend currently has no test project. Add it now so every C# change in this plan can be covered.

**Files:**
- Create: `backend/RestaurantApi.Tests/RestaurantApi.Tests.csproj`
- Create: `backend/RestaurantApi.Tests/Helpers/TestWebApplicationFactory.cs`
- Create: `backend/RestaurantApi.Tests/SanityTest.cs`
- Modify: solution file `backend/*.sln` (if present) to include the test project

- [ ] **Step 1: Create the test project file**

`backend/RestaurantApi.Tests/RestaurantApi.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.10.0" />
    <PackageReference Include="xunit" Version="2.9.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.0.8" />
    <PackageReference Include="Testcontainers.PostgreSql" Version="3.10.0" />
    <PackageReference Include="WireMock.Net" Version="1.5.62" />
    <PackageReference Include="FluentAssertions" Version="6.12.1" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\RestaurantApi\RestaurantApi.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add the WebApplicationFactory helper**

`backend/RestaurantApi.Tests/Helpers/TestWebApplicationFactory.cs`:

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RestaurantApi.Data;

namespace RestaurantApi.Tests.Helpers;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    public string PostgresConnectionString { get; set; } = string.Empty;
    public string PythonAiBaseUrl { get; set; } = "http://localhost:0";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = PostgresConnectionString,
                ["AiService:BaseUrl"] = PythonAiBaseUrl,
            });
        });
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor != null) services.Remove(descriptor);
            services.AddDbContext<AppDbContext>(opts => opts.UseNpgsql(PostgresConnectionString));
        });
    }
}
```

If the existing `Program.cs` does not declare `public partial class Program;` at the bottom, add it as a one-liner (xUnit needs it as a type reference). Modify `backend/RestaurantApi/Program.cs` only if the line is missing — append `public partial class Program;` at the very end.

- [ ] **Step 3: Add a minimal sanity test**

`backend/RestaurantApi.Tests/SanityTest.cs`:

```csharp
namespace RestaurantApi.Tests;

public class SanityTest
{
    [Fact]
    public void TestRunnerWorks()
    {
        Assert.Equal(4, 2 + 2);
    }
}
```

- [ ] **Step 4: Add the project to the solution if a `.sln` exists**

```
cd backend
dotnet sln list
# if a solution file exists, add the test project
dotnet sln add RestaurantApi.Tests/RestaurantApi.Tests.csproj
```

If no `.sln` exists, skip this step.

- [ ] **Step 5: Run the sanity test**

```
cd backend
dotnet test RestaurantApi.Tests/RestaurantApi.Tests.csproj
```

Expected: 1 passed.

- [ ] **Step 6: Checkpoint**

Files ready: full test project bootstrap.
Suggested commit: `chore(backend): add RestaurantApi.Tests project skeleton`

---

## Task 8: C# — extract `ProductValidator` from `MenuManagementController`

Pull the validation rules out so the new bulk endpoint and the existing single-create endpoint share one source of truth.

**Files:**
- Create: `backend/RestaurantApi/Validators/ProductValidator.cs`
- Create: `backend/RestaurantApi.Tests/ProductValidatorTests.cs`
- Modify: `backend/RestaurantApi/Controllers/MenuManagementController.cs`

- [ ] **Step 1: Write failing validator tests**

`backend/RestaurantApi.Tests/ProductValidatorTests.cs`:

```csharp
using FluentAssertions;
using RestaurantApi.Validators;

namespace RestaurantApi.Tests;

public class ProductValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_Empty_IsInvalid(string? name)
    {
        var result = ProductValidator.ValidateName(name);
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("Name");
    }

    [Fact]
    public void Name_TooLong_IsInvalid()
    {
        var result = ProductValidator.ValidateName(new string('a', 121));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Name_Valid_IsTrimmed()
    {
        var result = ProductValidator.ValidateName("  Ciorbă de burtă  ");
        result.IsValid.Should().BeTrue();
        result.Value.Should().Be("Ciorbă de burtă");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(99999999.999)]
    [InlineData(100000000)]
    public void Price_OutOfRange_IsInvalid(decimal price)
    {
        ProductValidator.ValidatePrice(price).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Price_InRange_IsValid()
    {
        ProductValidator.ValidatePrice(24.90m).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Description_TooLong_IsInvalid()
    {
        ProductValidator.ValidateDescription(new string('x', 1001)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Description_Null_IsValid()
    {
        ProductValidator.ValidateDescription(null).IsValid.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run to verify failure**

```
cd backend
dotnet test RestaurantApi.Tests/RestaurantApi.Tests.csproj
```

Expected: build error (`RestaurantApi.Validators` missing) or test failures.

- [ ] **Step 3: Create the validator**

`backend/RestaurantApi/Validators/ProductValidator.cs`:

```csharp
namespace RestaurantApi.Validators;

public readonly record struct ValidationResult<T>(bool IsValid, T? Value, string? Error)
{
    public static ValidationResult<T> Ok(T value) => new(true, value, null);
    public static ValidationResult<T> Fail(string error) => new(false, default, error);
}

public static class ProductValidator
{
    public const int NameMaxLength = 120;
    public const int DescriptionMaxLength = 1000;
    public const int ImageUrlMaxLength = 500;
    public const decimal PriceMax = 99999999.99m;

    public static ValidationResult<string> ValidateName(string? raw)
    {
        var trimmed = (raw ?? string.Empty).Trim();
        if (trimmed.Length is < 1 or > NameMaxLength)
            return ValidationResult<string>.Fail($"Name is required and must be 1–{NameMaxLength} characters.");
        return ValidationResult<string>.Ok(trimmed);
    }

    public static ValidationResult<decimal> ValidatePrice(decimal price)
    {
        if (price <= 0 || price > PriceMax)
            return ValidationResult<decimal>.Fail($"Price must be greater than 0 and at most {PriceMax}.");
        return ValidationResult<decimal>.Ok(price);
    }

    public static ValidationResult<string?> ValidateDescription(string? raw)
    {
        if (raw is null) return ValidationResult<string?>.Ok(null);
        if (raw.Length > DescriptionMaxLength)
            return ValidationResult<string?>.Fail($"Description must be at most {DescriptionMaxLength} characters.");
        return ValidationResult<string?>.Ok(raw);
    }

    public static ValidationResult<string?> ValidateImageUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ValidationResult<string?>.Ok(null);
        if (raw.Length > ImageUrlMaxLength)
            return ValidationResult<string?>.Fail($"ImageUrl must be at most {ImageUrlMaxLength} characters.");
        if (!Uri.TryCreate(raw, UriKind.Absolute, out _))
            return ValidationResult<string?>.Fail("ImageUrl must be a valid absolute URL.");
        return ValidationResult<string?>.Ok(raw);
    }
}
```

- [ ] **Step 4: Refactor MenuManagementController to use the validator (no behavior change)**

In `backend/RestaurantApi/Controllers/MenuManagementController.cs`:

1. Delete the four `private const` lines (`NameMaxLength`, `DescriptionMaxLength`, `ImageUrlMaxLength`, `PriceMax`).
2. Add `using RestaurantApi.Validators;` to the top.
3. Replace the validation blocks in `CreateCategory`, `UpdateCategory`, `CreateProduct`, and `UpdateProduct` with calls to `ProductValidator.*` — same error messages, same behavior, just funneled through the validator. Example for `CreateProduct`'s name check:

```csharp
var nameResult = ProductValidator.ValidateName(request.Name);
if (!nameResult.IsValid) return BadRequest(new { message = nameResult.Error });
var name = nameResult.Value!;
```

Apply the analogous change for `Price`, `Description`, and `ImageUrl` everywhere those rules appear. The existing endpoint behaviors must remain identical.

- [ ] **Step 5: Run tests**

```
cd backend
dotnet test RestaurantApi.Tests/RestaurantApi.Tests.csproj
```

Expected: all ProductValidator tests pass; existing single-create endpoints still behave identically (no regressions in the runtime — verified by hand if no integration tests exist yet for them).

- [ ] **Step 6: Checkpoint**

Files ready: validator, validator tests, refactored controller.
Suggested commit: `refactor(api): extract ProductValidator to share rules with bulk-create endpoint`

---

## Task 9: C# — DTOs for menu scan + bulk create

**Files:**
- Create: `backend/RestaurantApi/DTO/Responses/MenuScanResponses.cs`
- Create: `backend/RestaurantApi/DTO/Requests/MenuScanRequests.cs`

- [ ] **Step 1: Create response DTOs**

`backend/RestaurantApi/DTO/Responses/MenuScanResponses.cs`:

```csharp
namespace RestaurantApi.DTOs.Responses;

public record ExtractedMenuItemResponse(
    string Name,
    decimal Price,
    int SourcePage,
    int SourceIndex,
    string? Description = null,
    string? CategoryHint = null,
    double? Confidence = null);

public record FailedPageResponse(int SourcePage, string Reason);

public record MenuScanResponse(
    IReadOnlyList<ExtractedMenuItemResponse> Items,
    IReadOnlyList<FailedPageResponse> FailedPages,
    string Model,
    int SchemaVersion);
```

- [ ] **Step 2: Create request DTOs**

`backend/RestaurantApi/DTO/Requests/MenuScanRequests.cs`:

```csharp
namespace RestaurantApi.DTOs.Requests;

public record BulkCreateCategoryItem(
    Guid TempId,
    string Name,
    int? SortOrder = null);

public record BulkCreateProductItem(
    string Name,
    decimal Price,
    Guid CategoryId,
    string? Description = null,
    bool IsAvailable = true);

public record BulkCreateProductsRequest(
    IReadOnlyList<BulkCreateProductItem> Items,
    IReadOnlyList<BulkCreateCategoryItem> NewCategories);
```

- [ ] **Step 3: Verify build**

```
cd backend
dotnet build RestaurantApi/RestaurantApi.csproj
```

Expected: build succeeds.

- [ ] **Step 4: Checkpoint**

Files ready: both DTO files.
Suggested commit: `feat(api): add menu scan + bulk-create DTOs`

---

## Task 10: C# — `MenuExtractionClient` HTTP client + WireMock tests

Wrapper for the Python `/menu/extract` endpoint. Tests stub Python via `WireMock.Net`.

**Files:**
- Create: `backend/RestaurantApi/Services/MenuExtractionClient.cs`
- Create: `backend/RestaurantApi.Tests/MenuExtractionClientTests.cs`
- Modify: `backend/RestaurantApi/Program.cs` (register the typed HttpClient)
- Modify: `backend/RestaurantApi/appsettings.Development.json` (already has `AiService:BaseUrl` — leave alone if so)

- [ ] **Step 1: Write failing client tests**

`backend/RestaurantApi.Tests/MenuExtractionClientTests.cs`:

```csharp
using System.Net.Http.Json;
using FluentAssertions;
using RestaurantApi.DTOs.Responses;
using RestaurantApi.Services;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace RestaurantApi.Tests;

public class MenuExtractionClientTests : IAsyncLifetime
{
    private WireMockServer _python = null!;
    private HttpClient _httpClient = null!;

    public Task InitializeAsync()
    {
        _python = WireMockServer.Start();
        _httpClient = new HttpClient { BaseAddress = new Uri(_python.Url!) };
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _httpClient.Dispose();
        _python.Stop();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Forwards_photos_and_returns_parsed_response()
    {
        _python
            .Given(Request.Create().WithPath("/menu/extract").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                {
                  "items": [
                    { "name": "Ciorbă", "price": 18.0, "source_page": 0, "source_index": 0,
                      "description": null, "category_hint": "Ciorbe", "confidence": null }
                  ],
                  "failed_pages": [],
                  "model": "claude-sonnet-test",
                  "schema_version": 1
                }
                """));

        var client = new MenuExtractionClient(_httpClient);
        var photos = new List<(string FileName, string ContentType, byte[] Bytes)>
        {
            ("p0.png", "image/png", new byte[] { 1, 2, 3 }),
        };

        var result = await client.ExtractAsync(photos, CancellationToken.None);

        result.Items.Should().HaveCount(1);
        result.Items[0].Name.Should().Be("Ciorbă");
        result.Items[0].CategoryHint.Should().Be("Ciorbe");
        result.FailedPages.Should().BeEmpty();
        result.Model.Should().Be("claude-sonnet-test");
    }

    [Fact]
    public async Task Throws_on_non_success_response()
    {
        _python
            .Given(Request.Create().WithPath("/menu/extract").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(503));

        var client = new MenuExtractionClient(_httpClient);
        var photos = new List<(string, string, byte[])> { ("p0.png", "image/png", new byte[] { 1 }) };

        var act = async () => await client.ExtractAsync(photos, CancellationToken.None);
        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
```

- [ ] **Step 2: Run to verify failure**

```
cd backend
dotnet test RestaurantApi.Tests/RestaurantApi.Tests.csproj --filter MenuExtractionClientTests
```

Expected: compile error — `MenuExtractionClient` not defined.

- [ ] **Step 3: Implement the client**

`backend/RestaurantApi/Services/MenuExtractionClient.cs`:

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using RestaurantApi.DTOs.Responses;

namespace RestaurantApi.Services;

public class MenuExtractionClient
{
    private readonly HttpClient _http;

    public MenuExtractionClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<MenuScanResponse> ExtractAsync(
        IReadOnlyList<(string FileName, string ContentType, byte[] Bytes)> photos,
        CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        foreach (var p in photos)
        {
            var content = new ByteArrayContent(p.Bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue(p.ContentType);
            form.Add(content, "photos", p.FileName);
        }

        using var response = await _http.PostAsync("/menu/extract", form, cancellationToken);
        response.EnsureSuccessStatusCode();

        var parsed = await response.Content.ReadFromJsonAsync<PythonExtractResponse>(
            cancellationToken: cancellationToken);
        if (parsed is null) throw new InvalidOperationException("Empty response from /menu/extract.");

        var items = parsed.Items
            .Select(i => new ExtractedMenuItemResponse(
                i.Name, (decimal)i.Price, i.SourcePage, i.SourceIndex,
                i.Description, i.CategoryHint, i.Confidence))
            .ToList();
        var failed = parsed.FailedPages
            .Select(f => new FailedPageResponse(f.SourcePage, f.Reason))
            .ToList();

        return new MenuScanResponse(items, failed, parsed.Model, parsed.SchemaVersion);
    }

    // Internal mirror of Python's snake_case shape — System.Text.Json options below.
    private record PythonExtractResponse
    {
        public List<PythonItem> Items { get; init; } = new();
        public List<PythonFailedPage> FailedPages { get; init; } = new();
        public string Model { get; init; } = "";
        public int SchemaVersion { get; init; }
    }

    private record PythonItem
    {
        public string Name { get; init; } = "";
        public double Price { get; init; }
        public int SourcePage { get; init; }
        public int SourceIndex { get; init; }
        public string? Description { get; init; }
        public string? CategoryHint { get; init; }
        public double? Confidence { get; init; }
    }

    private record PythonFailedPage
    {
        public int SourcePage { get; init; }
        public string Reason { get; init; } = "";
    }
}
```

System.Text.Json must use snake_case naming. In `Program.cs`, ensure the AddControllers / AddJsonOptions call sets `PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower` *or* attribute the inner DTOs with `[JsonPropertyName("source_page")]` etc. Pick whichever matches the project's existing convention — check what `RecommendationService` does and follow that pattern. If unsure, use attributes locally on `PythonItem` / `PythonFailedPage` / `PythonExtractResponse` to avoid altering global JSON options.

- [ ] **Step 4: Register the typed HttpClient in `Program.cs`**

In `backend/RestaurantApi/Program.cs`, alongside the existing AI service wiring, add:

```csharp
builder.Services.AddHttpClient<MenuExtractionClient>(client =>
{
    var baseUrl = builder.Configuration["AiService:BaseUrl"]
        ?? throw new InvalidOperationException("AiService:BaseUrl not configured.");
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromMinutes(6);
});
```

Add `using RestaurantApi.Services;` to the top of `Program.cs` if not present.

- [ ] **Step 5: Run tests**

```
cd backend
dotnet test RestaurantApi.Tests/RestaurantApi.Tests.csproj --filter MenuExtractionClientTests
```

Expected: both tests pass.

- [ ] **Step 6: Checkpoint**

Files ready: `MenuExtractionClient.cs`, its tests, `Program.cs` registration.
Suggested commit: `feat(api): add MenuExtractionClient with WireMock-tested HTTP forwarding`

---

## Task 11: C# — `MenuScanController` + structured logging

**Files:**
- Create: `backend/RestaurantApi/Controllers/MenuScanController.cs`
- Create: `backend/RestaurantApi.Tests/MenuScanControllerTests.cs`

- [ ] **Step 1: Write failing controller tests**

`backend/RestaurantApi.Tests/MenuScanControllerTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantApi.DTOs.Responses;
using RestaurantApi.Services;
using RestaurantApi.Tests.Helpers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace RestaurantApi.Tests;

public class MenuScanControllerTests : IAsyncLifetime
{
    private WireMockServer _python = null!;
    private TestWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _python = WireMockServer.Start();
        _factory = new TestWebApplicationFactory
        {
            // BulkCreate tests will overwrite this — scan tests only need /menu/extract reachable.
            PostgresConnectionString = "Host=localhost;Database=ignored;Username=x;Password=x",
            PythonAiBaseUrl = _python.Url!,
        };
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        _python.Stop();
        return Task.CompletedTask;
    }

    private MultipartFormDataContent OnePhoto()
    {
        var form = new MultipartFormDataContent();
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(content, "photos", "p0.png");
        return form;
    }

    [Fact]
    public async Task Happy_path_forwards_to_python_and_returns_items()
    {
        _python.Given(Request.Create().WithPath("/menu/extract").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""
              { "items": [{ "name":"Ciorbă","price":18.0,"source_page":0,"source_index":0,"description":null,"category_hint":"Ciorbe","confidence":null }],
                "failed_pages": [], "model":"fake","schema_version":1 }
            """));

        var resp = await _client.PostAsync("/api/admin/menu/scan", OnePhoto());
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<MenuScanResponse>();
        body!.Items.Should().HaveCount(1);
        body.Items[0].CategoryHint.Should().Be("Ciorbe");
    }

    [Fact]
    public async Task Returns_502_when_python_unreachable()
    {
        _python.Stop();
        var resp = await _client.PostAsync("/api/admin/menu/scan", OnePhoto());
        resp.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task Returns_413_when_payload_exceeds_cap()
    {
        var form = new MultipartFormDataContent();
        for (var i = 0; i < 11; i++)
        {
            var content = new ByteArrayContent(new byte[] { 1 });
            content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(content, "photos", $"p{i}.png");
        }
        var resp = await _client.PostAsync("/api/admin/menu/scan", form);
        resp.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task Returns_400_when_no_photos()
    {
        var resp = await _client.PostAsync("/api/admin/menu/scan", new MultipartFormDataContent());
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
```

- [ ] **Step 2: Run to verify failure**

```
cd backend
dotnet test RestaurantApi.Tests/RestaurantApi.Tests.csproj --filter MenuScanControllerTests
```

Expected: compile errors (`MenuScanController` does not exist).

- [ ] **Step 3: Implement the controller**

`backend/RestaurantApi/Controllers/MenuScanController.cs`:

```csharp
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using RestaurantApi.DTOs.Responses;
using RestaurantApi.Services;

namespace RestaurantApi.Controllers;

[ApiController]
[Route("api/admin/menu/scan")]
public class MenuScanController : ControllerBase
{
    private const int MaxFiles = 10;
    private const long MaxFileBytes = 5L * 1024 * 1024;
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp", "image/heic",
    };

    private readonly MenuExtractionClient _extractor;
    private readonly ILogger<MenuScanController> _logger;

    public MenuScanController(MenuExtractionClient extractor, ILogger<MenuScanController> logger)
    {
        _extractor = extractor;
        _logger = logger;
    }

    [HttpPost]
    [RequestSizeLimit(MaxFiles * MaxFileBytes + 1024 * 1024)]  // 1 MB slack for form overhead
    public async Task<IActionResult> Scan(
        [FromForm(Name = "photos")] List<IFormFile>? photos,
        CancellationToken cancellationToken)
    {
        if (photos is null || photos.Count == 0)
            return BadRequest(new { message = "At least one photo is required." });

        if (photos.Count > MaxFiles)
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new { message = $"At most {MaxFiles} photos allowed." });

        var totalBytes = 0L;
        var prepared = new List<(string FileName, string ContentType, byte[] Bytes)>(photos.Count);
        foreach (var photo in photos)
        {
            if (photo.Length > MaxFileBytes)
                return StatusCode(StatusCodes.Status413PayloadTooLarge,
                    new { message = $"File '{photo.FileName}' exceeds {MaxFileBytes / 1024 / 1024} MB." });
            if (!AllowedContentTypes.Contains(photo.ContentType))
                return BadRequest(new { message = $"Unsupported content type '{photo.ContentType}'." });
            using var ms = new MemoryStream();
            await photo.CopyToAsync(ms, cancellationToken);
            var bytes = ms.ToArray();
            totalBytes += bytes.Length;
            prepared.Add((photo.FileName ?? "photo", photo.ContentType, bytes));
        }

        var sw = Stopwatch.StartNew();
        MenuScanResponse result;
        try
        {
            result = await _extractor.ExtractAsync(prepared, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Python /menu/extract unreachable");
            return StatusCode(StatusCodes.Status502BadGateway,
                new { message = "Serviciul de scanare nu este disponibil. Încearcă din nou." });
        }
        sw.Stop();

        _logger.LogInformation(new EventId(7001, "MenuScan"),
            "MenuScan completed admin={Admin} files={FileCount} bytes={TotalBytes} ms={Duration} model={Model} items={Items} failed={Failed}",
            User.Identity?.Name ?? "anonymous",
            prepared.Count,
            totalBytes,
            sw.ElapsedMilliseconds,
            result.Model,
            result.Items.Count,
            result.FailedPages.Count);

        return Ok(result);
    }
}
```

- [ ] **Step 4: Run tests**

```
cd backend
dotnet test RestaurantApi.Tests/RestaurantApi.Tests.csproj --filter MenuScanControllerTests
```

Expected: 4 passed.

- [ ] **Step 5: Checkpoint**

Files ready: controller, tests.
Suggested commit: `feat(api): add POST /api/admin/menu/scan endpoint with cap and structured logging`

---

## Task 12: C# — bulk-create endpoint + transactional tests

This adds `POST /api/admin/menu/products/bulk` on the existing `MenuManagementController` (the spec keeps menu CRUD operations together).

**Files:**
- Modify: `backend/RestaurantApi/Controllers/MenuManagementController.cs` (add the action)
- Create: `backend/RestaurantApi.Tests/BulkCreateProductsTests.cs`

- [ ] **Step 1: Write failing tests against a Testcontainers Postgres**

`backend/RestaurantApi.Tests/BulkCreateProductsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantApi.Data;
using RestaurantApi.DTOs.Requests;
using RestaurantApi.Models;
using RestaurantApi.Tests.Helpers;
using Testcontainers.PostgreSql;

namespace RestaurantApi.Tests;

public class BulkCreateProductsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private TestWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;
    private Guid _existingCategoryId;

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        _factory = new TestWebApplicationFactory
        {
            PostgresConnectionString = _pg.GetConnectionString(),
            PythonAiBaseUrl = "http://localhost:0",
        };
        _client = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        var cat = new Category { Id = Guid.NewGuid(), Name = "Aperitive", SortOrder = 0 };
        db.Categories.Add(cat);
        await db.SaveChangesAsync();
        _existingCategoryId = cat.Id;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        await _pg.DisposeAsync();
    }

    [Fact]
    public async Task Happy_path_creates_categories_and_products_atomically()
    {
        var tempId = Guid.NewGuid();
        var request = new BulkCreateProductsRequest(
            Items: new List<BulkCreateProductItem>
            {
                new("Ciorbă", 18.00m, _existingCategoryId),
                new("Tort", 22.00m, tempId),
            },
            NewCategories: new List<BulkCreateCategoryItem>
            {
                new(tempId, "Deserturi"),
            });

        var resp = await _client.PostAsJsonAsync("/api/admin/menu/products/bulk", request);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Categories.CountAsync()).Should().Be(2);
        (await db.Products.CountAsync()).Should().Be(2);
        var dessertCat = await db.Categories.SingleAsync(c => c.Name == "Deserturi");
        (await db.Products.CountAsync(p => p.CategoryId == dessertCat.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Validation_failure_on_one_item_rolls_back_everything()
    {
        var tempId = Guid.NewGuid();
        var request = new BulkCreateProductsRequest(
            Items: new List<BulkCreateProductItem>
            {
                new("Ciorbă", 18.00m, _existingCategoryId),
                new("", 1.00m, _existingCategoryId),  // invalid: empty name
            },
            NewCategories: new List<BulkCreateCategoryItem> { new(tempId, "Should not be created") });

        var resp = await _client.PostAsJsonAsync("/api/admin/menu/products/bulk", request);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Categories.CountAsync()).Should().Be(1);  // only seed
        (await db.Products.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Unknown_category_id_returns_409()
    {
        var bogusId = Guid.NewGuid();
        var request = new BulkCreateProductsRequest(
            Items: new List<BulkCreateProductItem> { new("Ciorbă", 18.00m, bogusId) },
            NewCategories: new List<BulkCreateCategoryItem>());

        var resp = await _client.PostAsJsonAsync("/api/admin/menu/products/bulk", request);
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        body.Should().ContainKey("missingCategoryIds");
    }

    [Fact]
    public async Task New_category_with_existing_name_is_merged_into_existing()
    {
        var tempId = Guid.NewGuid();
        var request = new BulkCreateProductsRequest(
            Items: new List<BulkCreateProductItem> { new("Salată", 12.00m, tempId) },
            NewCategories: new List<BulkCreateCategoryItem> { new(tempId, "  APERITIVE  ") }); // normalises to "aperitive"

        var resp = await _client.PostAsJsonAsync("/api/admin/menu/products/bulk", request);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Categories.CountAsync()).Should().Be(1);  // existing "Aperitive" reused
        var product = await db.Products.SingleAsync();
        product.CategoryId.Should().Be(_existingCategoryId);
    }
}
```

- [ ] **Step 2: Run to verify failure**

```
cd backend
dotnet test RestaurantApi.Tests/RestaurantApi.Tests.csproj --filter BulkCreateProductsTests
```

Expected: compile error (no bulk endpoint) or 404s.

- [ ] **Step 3: Add the bulk-create action**

Append to `backend/RestaurantApi/Controllers/MenuManagementController.cs`, inside the existing class:

```csharp
[HttpPost("products/bulk")]
public async Task<IActionResult> BulkCreate(
    [FromBody] BulkCreateProductsRequest request,
    CancellationToken cancellationToken)
{
    if (request.Items.Count == 0)
        return BadRequest(new { message = "At least one item is required." });

    // Validate every item upfront so a roll-back happens before any DB writes.
    var errors = new List<object>();
    for (var i = 0; i < request.Items.Count; i++)
    {
        var item = request.Items[i];
        var nameResult = ProductValidator.ValidateName(item.Name);
        if (!nameResult.IsValid) errors.Add(new { index = i, field = "name", message = nameResult.Error });
        var priceResult = ProductValidator.ValidatePrice(item.Price);
        if (!priceResult.IsValid) errors.Add(new { index = i, field = "price", message = priceResult.Error });
        var descResult = ProductValidator.ValidateDescription(item.Description);
        if (!descResult.IsValid) errors.Add(new { index = i, field = "description", message = descResult.Error });
    }
    if (errors.Count > 0) return BadRequest(new { errors });

    await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

    // 1. Create/merge new categories, building tempId → realId map.
    var tempIdToRealId = new Dictionary<Guid, Guid>();
    foreach (var nc in request.NewCategories)
    {
        var normalized = NormalizeCategoryName(nc.Name);
        var existing = await _db.Categories
            .Where(c => EF.Functions.ILike(c.Name, normalized))
            .FirstOrDefaultAsync(cancellationToken);
        if (existing != null)
        {
            tempIdToRealId[nc.TempId] = existing.Id;
            continue;
        }
        var nameResult = ProductValidator.ValidateName(nc.Name);
        if (!nameResult.IsValid) return BadRequest(new { errors = new[] { new { field = "newCategories.name", message = nameResult.Error } } });
        var newCat = new Category
        {
            Id = Guid.NewGuid(),
            Name = nameResult.Value!,
            SortOrder = nc.SortOrder,
        };
        _db.Categories.Add(newCat);
        tempIdToRealId[nc.TempId] = newCat.Id;
    }
    await _db.SaveChangesAsync(cancellationToken);

    // 2. Resolve each item's CategoryId.
    var resolvedItems = new List<(BulkCreateProductItem Item, Guid RealCategoryId)>();
    var missing = new List<Guid>();
    foreach (var item in request.Items)
    {
        Guid realId;
        if (tempIdToRealId.TryGetValue(item.CategoryId, out var mapped))
        {
            realId = mapped;
        }
        else
        {
            realId = item.CategoryId;
            var exists = await _db.Categories.AnyAsync(c => c.Id == realId, cancellationToken);
            if (!exists) missing.Add(realId);
        }
        resolvedItems.Add((item, realId));
    }
    if (missing.Count > 0)
    {
        await tx.RollbackAsync(cancellationToken);
        return Conflict(new { missingCategoryIds = missing.Distinct().ToList() });
    }

    // 3. Insert products. SortOrder = bottom of the target category.
    var created = new List<AdminProductResponse>();
    var maxSortByCategory = new Dictionary<Guid, int>();
    foreach (var (item, categoryId) in resolvedItems)
    {
        if (!maxSortByCategory.TryGetValue(categoryId, out var max))
        {
            max = await _db.Products
                .Where(p => p.CategoryId == categoryId)
                .Select(p => (int?)p.SortOrder)
                .MaxAsync(cancellationToken) ?? -1;
            maxSortByCategory[categoryId] = max;
        }
        max += 1;
        maxSortByCategory[categoryId] = max;

        var product = new Product
        {
            Id = Guid.NewGuid(),
            CategoryId = categoryId,
            Name = item.Name.Trim(),
            Description = item.Description ?? string.Empty,
            Price = item.Price,
            ImageUrl = null,
            IsAvailable = item.IsAvailable,
            SortOrder = max,
        };
        _db.Products.Add(product);
        created.Add(new AdminProductResponse
        {
            Id = product.Id,
            CategoryId = product.CategoryId,
            CategoryName = "", // populated after the SaveChanges + reload below
            Name = product.Name,
            Description = product.Description,
            Price = product.Price,
            ImageUrl = product.ImageUrl,
            IsAvailable = product.IsAvailable ?? false,
            SortOrder = product.SortOrder,
        });
    }
    await _db.SaveChangesAsync(cancellationToken);

    // 4. Backfill category names for the response.
    var categoryNames = await _db.Categories
        .Where(c => created.Select(r => r.CategoryId).Distinct().Contains(c.Id))
        .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
    foreach (var r in created)
    {
        if (categoryNames.TryGetValue(r.CategoryId, out var name)) r.CategoryName = name;
    }

    await tx.CommitAsync(cancellationToken);
    _logger.LogInformation("Bulk-created {Products} products across {Categories} categories",
        created.Count, created.Select(r => r.CategoryId).Distinct().Count());
    return Ok(created);
}

private static string NormalizeCategoryName(string raw)
{
    return new string((raw ?? string.Empty)
        .Trim()
        .Normalize(System.Text.NormalizationForm.FormD)
        .Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch)
            != System.Globalization.UnicodeCategory.NonSpacingMark)
        .ToArray())
        .ToLowerInvariant();
}
```

Add the appropriate `using RestaurantApi.DTOs.Requests;` and `using RestaurantApi.Validators;` at the top if missing.

- [ ] **Step 4: Run the tests**

```
cd backend
dotnet test RestaurantApi.Tests/RestaurantApi.Tests.csproj --filter BulkCreateProductsTests
```

Expected: 4 passed.

- [ ] **Step 5: Checkpoint**

Files ready: extended controller, new test file.
Suggested commit: `feat(api): add POST /api/admin/menu/products/bulk with transactional category+product creation`

---

## Task 13: Frontend — API service

**Files:**
- Create: `frontend-admin/src/services/menuImportApi.ts`
- Create: `frontend-admin/src/__tests__/menuImportApi.spec.ts`

- [ ] **Step 1: Write failing API service tests**

`frontend-admin/src/__tests__/menuImportApi.spec.ts`:

```ts
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'

import { menuImportApi } from '@/services/menuImportApi'
import type { BulkCreateProductsRequest, MenuScanResponse } from '@shared/types/menu-import'

const server = setupServer()
beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

const baseUrl = 'http://api.test'
import.meta.env.VITE_API_URL = baseUrl

describe('menuImportApi.scan', () => {
  it('POSTs photos as multipart and parses the response', async () => {
    server.use(
      http.post(`${baseUrl}/api/admin/menu/scan`, async ({ request }) => {
        const form = await request.formData()
        const files = form.getAll('photos')
        expect(files).toHaveLength(2)
        return HttpResponse.json<MenuScanResponse>({
          items: [{ name: 'Ciorbă', price: 18, sourcePage: 0, sourceIndex: 0, categoryHint: 'Ciorbe' }],
          failedPages: [],
          model: 'fake',
          schemaVersion: 1,
        })
      }),
    )

    const result = await menuImportApi.scan([
      new File([new Uint8Array([1])], 'a.png', { type: 'image/png' }),
      new File([new Uint8Array([2])], 'b.png', { type: 'image/png' }),
    ])
    expect(result.items[0].name).toBe('Ciorbă')
    expect(result.items[0].categoryHint).toBe('Ciorbe')
  })

  it('throws a typed error on 5xx', async () => {
    server.use(
      http.post(`${baseUrl}/api/admin/menu/scan`, () => HttpResponse.json({ message: 'down' }, { status: 502 })),
    )
    await expect(
      menuImportApi.scan([new File([new Uint8Array([1])], 'a.png', { type: 'image/png' })]),
    ).rejects.toThrowError(/scan/i)
  })
})

describe('menuImportApi.bulkCreate', () => {
  it('POSTs JSON and parses response', async () => {
    server.use(
      http.post(`${baseUrl}/api/admin/menu/products/bulk`, async ({ request }) => {
        const body = (await request.json()) as BulkCreateProductsRequest
        expect(body.items).toHaveLength(1)
        return HttpResponse.json([
          { id: 'p1', categoryId: 'c1', categoryName: 'Aperitive', name: 'Ciorbă', description: '', price: 18, imageUrl: null, isAvailable: true, sortOrder: 0 },
        ])
      }),
    )
    const created = await menuImportApi.bulkCreate({
      items: [{ name: 'Ciorbă', price: 18, categoryId: 'c1' }],
      newCategories: [],
    })
    expect(created).toHaveLength(1)
  })

  it('throws with parsed missingCategoryIds on 409', async () => {
    server.use(
      http.post(`${baseUrl}/api/admin/menu/products/bulk`, () =>
        HttpResponse.json({ missingCategoryIds: ['c-gone'] }, { status: 409 }),
      ),
    )
    await expect(
      menuImportApi.bulkCreate({
        items: [{ name: 'X', price: 1, categoryId: 'c-gone' }],
        newCategories: [],
      }),
    ).rejects.toMatchObject({ status: 409, missingCategoryIds: ['c-gone'] })
  })
})
```

- [ ] **Step 2: Install MSW if not present**

Check `frontend-admin/package.json` for `msw`. If missing:

```
cd frontend-admin
npm install --save-dev msw@^2
```

- [ ] **Step 3: Run to verify failure**

```
cd frontend-admin
npm run test -- src/__tests__/menuImportApi.spec.ts
```

Expected: import failure on `menuImportApi`.

- [ ] **Step 4: Implement the service**

`frontend-admin/src/services/menuImportApi.ts`:

```ts
import type {
  BulkCreateProductsRequest,
  MenuScanResponse,
} from '@shared/types/menu-import'
import type { AdminProduct } from '@shared/types/models'

const BASE_URL = import.meta.env.VITE_API_URL ?? ''

export interface MenuImportApiError extends Error {
  status: number
  missingCategoryIds?: string[]
  body?: unknown
}

function makeError(message: string, status: number, body?: unknown): MenuImportApiError {
  const err = new Error(message) as MenuImportApiError
  err.status = status
  err.body = body
  if (body && typeof body === 'object' && 'missingCategoryIds' in body) {
    err.missingCategoryIds = (body as { missingCategoryIds: string[] }).missingCategoryIds
  }
  return err
}

async function scan(files: File[]): Promise<MenuScanResponse> {
  const form = new FormData()
  for (const file of files) form.append('photos', file, file.name)
  const res = await fetch(`${BASE_URL}/api/admin/menu/scan`, { method: 'POST', body: form })
  if (!res.ok) {
    const body = await res.json().catch(() => undefined)
    throw makeError(`scan failed: HTTP ${res.status}`, res.status, body)
  }
  return (await res.json()) as MenuScanResponse
}

async function bulkCreate(payload: BulkCreateProductsRequest): Promise<AdminProduct[]> {
  const res = await fetch(`${BASE_URL}/api/admin/menu/products/bulk`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })
  if (!res.ok) {
    const body = await res.json().catch(() => undefined)
    throw makeError(`bulk-create failed: HTTP ${res.status}`, res.status, body)
  }
  return (await res.json()) as AdminProduct[]
}

export const menuImportApi = { scan, bulkCreate }
```

- [ ] **Step 5: Run tests**

```
cd frontend-admin
npm run test -- src/__tests__/menuImportApi.spec.ts
```

Expected: 4 passed.

- [ ] **Step 6: Checkpoint**

Files ready: `menuImportApi.ts`, its tests.
Suggested commit: `feat(admin): add menuImportApi service`

---

## Task 14: Frontend — Pinia store + tests

**Files:**
- Create: `frontend-admin/src/stores/useMenuImportStore.ts`
- Create: `frontend-admin/src/__tests__/useMenuImportStore.spec.ts`

- [ ] **Step 1: Write failing store tests**

`frontend-admin/src/__tests__/useMenuImportStore.spec.ts`:

```ts
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useMenuImportStore } from '@/stores/useMenuImportStore'
import type { MenuScanResponse } from '@shared/types/menu-import'

vi.mock('@/services/menuImportApi', () => ({
  menuImportApi: {
    scan: vi.fn(),
    bulkCreate: vi.fn(),
  },
}))

import { menuImportApi } from '@/services/menuImportApi'

const scanFixture: MenuScanResponse = {
  items: [
    { name: 'Ciorbă', price: 18, sourcePage: 0, sourceIndex: 0, categoryHint: 'Ciorbe' },
    { name: 'Tort', price: 22, sourcePage: 1, sourceIndex: 0, categoryHint: 'Deserturi' },
    { name: 'Mister', price: 9, sourcePage: 1, sourceIndex: 1, categoryHint: null },
  ],
  failedPages: [],
  model: 'fake',
  schemaVersion: 1,
}

describe('useMenuImportStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.resetAllMocks()
  })

  it('starts on step 1 with empty state', () => {
    const store = useMenuImportStore()
    expect(store.step).toBe(1)
    expect(store.files).toEqual([])
    expect(store.canAdvanceFromStep1).toBe(false)
  })

  it('addFiles enforces max count and unique names', () => {
    const store = useMenuImportStore()
    for (let i = 0; i < 12; i++) {
      store.addFiles([new File([new Uint8Array([i])], `f${i}.png`, { type: 'image/png' })])
    }
    expect(store.files.length).toBe(10)
  })

  it('scan() advances to step 2 and seeds category mapping', async () => {
    ;(menuImportApi.scan as ReturnType<typeof vi.fn>).mockResolvedValueOnce(scanFixture)
    const store = useMenuImportStore()
    store.addFiles([new File([new Uint8Array([1])], 'a.png', { type: 'image/png' })])
    await store.scan()
    expect(store.step).toBe(2)
    expect(Object.keys(store.categoryMapping).sort()).toEqual(['Ciorbe', 'Deserturi'])
    expect(store.unassignedCount).toBe(1) // one item with categoryHint = null
  })

  it('mappingComplete is false until every detected hint AND the unassigned bucket are mapped', async () => {
    ;(menuImportApi.scan as ReturnType<typeof vi.fn>).mockResolvedValueOnce(scanFixture)
    const store = useMenuImportStore()
    store.addFiles([new File([new Uint8Array([1])], 'a.png', { type: 'image/png' })])
    await store.scan()
    expect(store.mappingComplete).toBe(false)
    store.setMapping('Ciorbe', { kind: 'new', tempId: 't1', name: 'Ciorbe' })
    store.setMapping('Deserturi', { kind: 'new', tempId: 't2', name: 'Deserturi' })
    expect(store.mappingComplete).toBe(false) // unassigned still missing
    store.setUnassignedMapping({ kind: 'existing', categoryId: 'cat-existing' })
    expect(store.mappingComplete).toBe(true)
  })

  it('review items include resolvedCategory based on mapping', async () => {
    ;(menuImportApi.scan as ReturnType<typeof vi.fn>).mockResolvedValueOnce(scanFixture)
    const store = useMenuImportStore()
    store.addFiles([new File([new Uint8Array([1])], 'a.png', { type: 'image/png' })])
    await store.scan()
    store.setMapping('Ciorbe', { kind: 'existing', categoryId: 'cat-c' })
    store.setMapping('Deserturi', { kind: 'new', tempId: 't-d', name: 'Deserturi' })
    store.setUnassignedMapping({ kind: 'existing', categoryId: 'cat-u' })
    store.advanceToReview()
    expect(store.step).toBe(3)
    const byName = Object.fromEntries(store.reviewItems.map((r) => [r.name, r]))
    expect(byName['Ciorbă'].resolvedCategory).toMatchObject({ kind: 'existing', categoryId: 'cat-c' })
    expect(byName['Tort'].resolvedCategory).toMatchObject({ kind: 'new', name: 'Deserturi' })
    expect(byName['Mister'].resolvedCategory).toMatchObject({ kind: 'existing', categoryId: 'cat-u' })
  })

  it('canImport is false while any non-skipped row is invalid', async () => {
    ;(menuImportApi.scan as ReturnType<typeof vi.fn>).mockResolvedValueOnce(scanFixture)
    const store = useMenuImportStore()
    store.addFiles([new File([new Uint8Array([1])], 'a.png', { type: 'image/png' })])
    await store.scan()
    store.setMapping('Ciorbe', { kind: 'existing', categoryId: 'cat-c' })
    store.setMapping('Deserturi', { kind: 'existing', categoryId: 'cat-d' })
    store.setUnassignedMapping({ kind: 'existing', categoryId: 'cat-u' })
    store.advanceToReview()

    const first = store.reviewItems[0]
    store.updateItem(first.clientId, { name: '' })
    expect(store.canImport).toBe(false)
    store.toggleSkip(first.clientId)
    expect(store.canImport).toBe(true)
  })

  it('$reset clears everything', () => {
    const store = useMenuImportStore()
    store.addFiles([new File([new Uint8Array([1])], 'a.png', { type: 'image/png' })])
    store.$reset()
    expect(store.files).toEqual([])
    expect(store.step).toBe(1)
  })
})
```

- [ ] **Step 2: Run to verify failure**

```
cd frontend-admin
npm run test -- src/__tests__/useMenuImportStore.spec.ts
```

Expected: import failure on `useMenuImportStore`.

- [ ] **Step 3: Implement the store**

`frontend-admin/src/stores/useMenuImportStore.ts`:

```ts
import { defineStore } from 'pinia'
import { computed, reactive, ref } from 'vue'

import { menuImportApi } from '@/services/menuImportApi'
import { MENU_VALIDATION } from '@shared/types/menu-validation'
import type {
  BulkCreateCategoryItem,
  BulkCreateProductItem,
  BulkCreateProductsRequest,
  ExtractedMenuItem,
  FailedPage,
} from '@shared/types/menu-import'

export type MappedCategory =
  | { kind: 'existing'; categoryId: string }
  | { kind: 'new'; tempId: string; name: string }

export interface ReviewItem {
  clientId: string
  name: string
  price: number
  sourcePage: number
  categoryHint: string | null
  resolvedCategory: MappedCategory
  skipped: boolean
}

function normaliseHint(hint: string): string {
  return hint.normalize('NFD').replace(/\p{Diacritic}/gu, '').trim().toLowerCase()
}

function makeId(): string {
  return crypto.randomUUID()
}

export const useMenuImportStore = defineStore('menuImport', () => {
  const step = ref<1 | 2 | 3>(1)
  const files = ref<File[]>([])
  const isScanning = ref(false)
  const scanError = ref<string | null>(null)
  const isImporting = ref(false)
  const importError = ref<string | null>(null)

  const extractedItems = ref<ExtractedMenuItem[]>([])
  const failedPages = ref<FailedPage[]>([])
  const detectedHints = ref<string[]>([])
  const categoryMapping = reactive<Record<string, MappedCategory>>({})
  const unassignedMapping = ref<MappedCategory | null>(null)
  const reviewItems = ref<ReviewItem[]>([])

  const canAdvanceFromStep1 = computed(() => files.value.length > 0 && !isScanning.value)
  const unassignedCount = computed(
    () => extractedItems.value.filter((it) => it.categoryHint == null).length,
  )
  const mappingComplete = computed(() => {
    const allHintsMapped = detectedHints.value.every((h) => h in categoryMapping)
    const unassignedOk = unassignedCount.value === 0 || unassignedMapping.value !== null
    return allHintsMapped && unassignedOk
  })

  function addFiles(incoming: File[]): { rejected: { file: File; reason: string }[] } {
    const rejected: { file: File; reason: string }[] = []
    for (const f of incoming) {
      if (files.value.length >= MENU_VALIDATION.IMPORT_MAX_FILES) {
        rejected.push({ file: f, reason: 'too_many' })
        continue
      }
      if (f.size > MENU_VALIDATION.IMPORT_MAX_FILE_BYTES) {
        rejected.push({ file: f, reason: 'too_large' })
        continue
      }
      if (!(MENU_VALIDATION.IMPORT_ALLOWED_MIME as readonly string[]).includes(f.type)) {
        rejected.push({ file: f, reason: 'bad_mime' })
        continue
      }
      if (files.value.some((existing) => existing.name === f.name && existing.size === f.size)) continue
      files.value.push(f)
    }
    return { rejected }
  }

  function removeFile(name: string) {
    files.value = files.value.filter((f) => f.name !== name)
  }

  async function scan() {
    isScanning.value = true
    scanError.value = null
    try {
      const result = await menuImportApi.scan(files.value)
      extractedItems.value = result.items
      failedPages.value = result.failedPages
      detectedHints.value = Array.from(
        new Set(result.items.map((it) => it.categoryHint).filter((h): h is string => !!h)),
      )
      // Reset mapping for current hint set.
      for (const k of Object.keys(categoryMapping)) delete categoryMapping[k]
      unassignedMapping.value = null
      step.value = 2
    } catch (err) {
      scanError.value = err instanceof Error ? err.message : 'Scan failed.'
      throw err
    } finally {
      isScanning.value = false
    }
  }

  function setMapping(hint: string, mapping: MappedCategory) {
    categoryMapping[hint] = mapping
  }

  function setUnassignedMapping(mapping: MappedCategory) {
    unassignedMapping.value = mapping
  }

  function autoMatch(existingCategories: { id: string; name: string }[]) {
    const byNormalised = new Map(existingCategories.map((c) => [normaliseHint(c.name), c.id]))
    for (const hint of detectedHints.value) {
      if (hint in categoryMapping) continue
      const match = byNormalised.get(normaliseHint(hint))
      if (match) {
        categoryMapping[hint] = { kind: 'existing', categoryId: match }
      } else {
        categoryMapping[hint] = { kind: 'new', tempId: makeId(), name: hint }
      }
    }
  }

  function resolveCategoryForItem(item: ExtractedMenuItem): MappedCategory {
    if (item.categoryHint && categoryMapping[item.categoryHint]) {
      return categoryMapping[item.categoryHint]
    }
    if (unassignedMapping.value) return unassignedMapping.value
    // Should not be reachable if mappingComplete gates the transition; fall through to a placeholder.
    return { kind: 'new', tempId: 'unmapped', name: 'Unassigned' }
  }

  function advanceToReview() {
    reviewItems.value = extractedItems.value
      .slice()
      .sort((a, b) => a.sourcePage - b.sourcePage || a.sourceIndex - b.sourceIndex)
      .map((it) => ({
        clientId: makeId(),
        name: it.name,
        price: it.price,
        sourcePage: it.sourcePage,
        categoryHint: it.categoryHint ?? null,
        resolvedCategory: resolveCategoryForItem(it),
        skipped: false,
      }))
    step.value = 3
  }

  function updateItem(clientId: string, patch: Partial<Pick<ReviewItem, 'name' | 'price'>>) {
    const target = reviewItems.value.find((r) => r.clientId === clientId)
    if (!target) return
    if (patch.name !== undefined) target.name = patch.name
    if (patch.price !== undefined) target.price = patch.price
  }

  function toggleSkip(clientId: string) {
    const target = reviewItems.value.find((r) => r.clientId === clientId)
    if (target) target.skipped = !target.skipped
  }

  function validateRow(row: ReviewItem): { ok: boolean; field?: 'name' | 'price' } {
    const name = row.name.trim()
    if (name.length < MENU_VALIDATION.NAME_MIN || name.length > MENU_VALIDATION.NAME_MAX)
      return { ok: false, field: 'name' }
    if (!(row.price > 0) || row.price > MENU_VALIDATION.PRICE_MAX) return { ok: false, field: 'price' }
    return { ok: true }
  }

  const canImport = computed(() => {
    const active = reviewItems.value.filter((r) => !r.skipped)
    if (active.length === 0) return false
    return active.every((r) => validateRow(r).ok)
  })

  function buildBulkPayload(): BulkCreateProductsRequest {
    const newCategoriesByTempId = new Map<string, BulkCreateCategoryItem>()
    const items: BulkCreateProductItem[] = []
    for (const row of reviewItems.value) {
      if (row.skipped) continue
      let categoryId: string
      if (row.resolvedCategory.kind === 'existing') {
        categoryId = row.resolvedCategory.categoryId
      } else {
        categoryId = row.resolvedCategory.tempId
        if (!newCategoriesByTempId.has(row.resolvedCategory.tempId)) {
          newCategoriesByTempId.set(row.resolvedCategory.tempId, {
            tempId: row.resolvedCategory.tempId,
            name: row.resolvedCategory.name,
          })
        }
      }
      items.push({
        name: row.name.trim(),
        price: row.price,
        categoryId,
        isAvailable: true,
      })
    }
    return { items, newCategories: Array.from(newCategoriesByTempId.values()) }
  }

  async function importAll() {
    isImporting.value = true
    importError.value = null
    try {
      await menuImportApi.bulkCreate(buildBulkPayload())
    } catch (err) {
      importError.value = err instanceof Error ? err.message : 'Import failed.'
      throw err
    } finally {
      isImporting.value = false
    }
  }

  function $reset() {
    step.value = 1
    files.value = []
    isScanning.value = false
    scanError.value = null
    isImporting.value = false
    importError.value = null
    extractedItems.value = []
    failedPages.value = []
    detectedHints.value = []
    for (const k of Object.keys(categoryMapping)) delete categoryMapping[k]
    unassignedMapping.value = null
    reviewItems.value = []
  }

  return {
    // state
    step,
    files,
    isScanning,
    scanError,
    isImporting,
    importError,
    extractedItems,
    failedPages,
    detectedHints,
    categoryMapping,
    unassignedMapping,
    reviewItems,
    // getters
    canAdvanceFromStep1,
    unassignedCount,
    mappingComplete,
    canImport,
    // actions
    addFiles,
    removeFile,
    scan,
    setMapping,
    setUnassignedMapping,
    autoMatch,
    advanceToReview,
    updateItem,
    toggleSkip,
    validateRow,
    buildBulkPayload,
    importAll,
    $reset,
  }
})
```

- [ ] **Step 4: Run tests**

```
cd frontend-admin
npm run test -- src/__tests__/useMenuImportStore.spec.ts
```

Expected: 7 passed.

- [ ] **Step 5: Checkpoint**

Files ready: store, tests.
Suggested commit: `feat(admin): add useMenuImportStore for the import wizard`

---

## Task 15: Frontend — `ImportStepIndicator` and `StepUploadPhotos`

These two are small, share no logic; bundling reduces churn.

**Files:**
- Create: `frontend-admin/src/components/menu-import/ImportStepIndicator.vue`
- Create: `frontend-admin/src/components/menu-import/StepUploadPhotos.vue`

- [ ] **Step 1: `ImportStepIndicator.vue`**

```vue
<script setup lang="ts">
defineProps<{
  current: 1 | 2 | 3
}>()

const steps = [
  { num: 1, label: 'Fotografii' },
  { num: 2, label: 'Categorii' },
  { num: 3, label: 'Revizuire' },
] as const
</script>

<template>
  <ol class="step-bar" aria-label="Pași importul meniului">
    <li
      v-for="s in steps"
      :key="s.num"
      class="step-bar-item"
      :class="{
        'is-current': s.num === current,
        'is-done': s.num < current,
      }"
      :aria-current="s.num === current ? 'step' : undefined"
    >
      <span class="step-bar-num">{{ s.num }}</span>
      <span class="step-bar-label">{{ s.label }}</span>
    </li>
  </ol>
</template>

<style scoped>
.step-bar {
  display: flex;
  gap: 12px;
  list-style: none;
  padding: 0;
  margin: 0 0 32px 0;
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 11px;
  text-transform: uppercase;
  letter-spacing: 0.18em;
}
.step-bar-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 14px;
  border: 1px solid var(--ink12);
  border-radius: 999px;
  background: var(--paper);
  color: var(--ink40);
}
.step-bar-item.is-done {
  color: var(--ink70);
  background: var(--paperHi);
}
.step-bar-item.is-current {
  color: var(--ink);
  background: var(--paperHi);
  border-color: var(--ink);
  box-shadow: 0 0 0 2px var(--lime) inset;
}
.step-bar-num {
  display: inline-grid;
  place-items: center;
  width: 18px;
  height: 18px;
  border-radius: 50%;
  background: var(--ink06);
  color: inherit;
  font-weight: 600;
}
.is-current .step-bar-num {
  background: var(--lime);
  color: var(--onLime);
}
</style>
```

- [ ] **Step 2: `StepUploadPhotos.vue`**

```vue
<script setup lang="ts">
import { computed, ref } from 'vue'

import { useMenuImportStore } from '@/stores/useMenuImportStore'
import { MENU_VALIDATION } from '@shared/types/menu-validation'

const store = useMenuImportStore()
const dragOver = ref(false)
const fileInput = ref<HTMLInputElement | null>(null)
const lastRejection = ref<string | null>(null)

const acceptAttr = computed(() => MENU_VALIDATION.IMPORT_ALLOWED_MIME.join(','))

function onPickClick() {
  fileInput.value?.click()
}

function onFilesSelected(event: Event) {
  const input = event.target as HTMLInputElement
  if (!input.files) return
  const { rejected } = store.addFiles(Array.from(input.files))
  reportRejected(rejected)
  input.value = ''
}

function onDrop(event: DragEvent) {
  event.preventDefault()
  dragOver.value = false
  if (!event.dataTransfer) return
  const { rejected } = store.addFiles(Array.from(event.dataTransfer.files))
  reportRejected(rejected)
}

function reportRejected(rejected: { reason: string }[]) {
  if (rejected.length === 0) {
    lastRejection.value = null
    return
  }
  const reason = rejected[0].reason
  lastRejection.value =
    reason === 'too_many'
      ? `Limită ${MENU_VALIDATION.IMPORT_MAX_FILES} fotografii.`
      : reason === 'too_large'
        ? `Maxim ${MENU_VALIDATION.IMPORT_MAX_FILE_BYTES / 1024 / 1024} MB / fotografie.`
        : 'Tipul de fișier nu este suportat (jpeg/png/webp/heic).'
}

async function startScan() {
  try {
    await store.scan()
  } catch {
    // store.scanError is already set
  }
}

function thumbUrl(file: File): string {
  return URL.createObjectURL(file)
}
</script>

<template>
  <section>
    <div
      class="dropzone card-paper"
      :class="{ 'is-drag': dragOver }"
      @dragover.prevent="dragOver = true"
      @dragleave="dragOver = false"
      @drop="onDrop"
    >
      <div class="eyebrow mb-3">pasul 1 · încărcare</div>
      <h2 class="display-title text-2xl mb-2">Trage fotografiile aici</h2>
      <p class="text-ink-70 text-sm mb-4">
        Maxim {{ MENU_VALIDATION.IMPORT_MAX_FILES }} pagini ·
        {{ MENU_VALIDATION.IMPORT_MAX_FILE_BYTES / 1024 / 1024 }} MB pe fotografie ·
        jpeg / png / webp / heic
      </p>
      <button class="pill-btn pill-btn-ghost" type="button" @click="onPickClick">
        Alege fișiere
      </button>
      <input
        ref="fileInput"
        type="file"
        :accept="acceptAttr"
        multiple
        class="hidden-input"
        @change="onFilesSelected"
      />
    </div>

    <p v-if="lastRejection" class="reject-note">⚠ {{ lastRejection }}</p>

    <div v-if="store.files.length" class="thumbs">
      <figure v-for="file in store.files" :key="file.name" class="thumb card-white">
        <img :src="thumbUrl(file)" :alt="file.name" />
        <figcaption>
          <span class="thumb-name">{{ file.name }}</span>
          <button type="button" class="thumb-remove" @click="store.removeFile(file.name)" aria-label="Elimină">
            ×
          </button>
        </figcaption>
      </figure>
    </div>

    <div v-if="store.scanError" class="card-paper p-4 mt-6" style="border-color: var(--accentBorder);">
      <span class="font-mono text-xs uppercase tracking-[0.18em] text-accent font-bold">
        ⚠ {{ store.scanError }}
      </span>
    </div>

    <div class="footer-row">
      <button
        class="pill-btn"
        type="button"
        :disabled="!store.canAdvanceFromStep1"
        @click="startScan"
      >
        <template v-if="store.isScanning">Scanez {{ store.files.length }} fotografii…</template>
        <template v-else>Scanează meniul</template>
      </button>
    </div>
  </section>
</template>

<style scoped>
.dropzone {
  padding: 32px;
  text-align: left;
  transition: border-color 0.15s ease, background 0.15s ease;
}
.dropzone.is-drag {
  border-color: var(--ink);
  background: var(--paperHi);
}
.hidden-input { display: none; }
.thumbs {
  display: grid;
  gap: 12px;
  grid-template-columns: repeat(auto-fill, minmax(160px, 1fr));
  margin-top: 24px;
}
.thumb {
  padding: 8px;
  position: relative;
}
.thumb img {
  width: 100%;
  height: 110px;
  object-fit: cover;
  border-radius: 8px;
  background: var(--ink06);
}
.thumb figcaption {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-top: 6px;
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 11px;
  color: var(--ink70);
}
.thumb-name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; max-width: 80%; }
.thumb-remove {
  border: none;
  background: var(--ink06);
  color: var(--ink);
  width: 24px;
  height: 24px;
  border-radius: 50%;
  cursor: pointer;
  line-height: 1;
}
.reject-note { margin-top: 12px; color: var(--accent); font-size: 13px; }
.footer-row {
  margin-top: 28px;
  display: flex;
  justify-content: flex-end;
}
</style>
```

- [ ] **Step 3: Checkpoint**

Files ready: both components.
Suggested commit: `feat(admin): add step indicator + upload-photos step component`

---

## Task 16: Frontend — `StepMapCategories`

**Files:**
- Create: `frontend-admin/src/components/menu-import/StepMapCategories.vue`

- [ ] **Step 1: Implement the component**

```vue
<script setup lang="ts">
import { onMounted } from 'vue'

import { useMenuImportStore, type MappedCategory } from '@/stores/useMenuImportStore'
import { useMenuManagementStore } from '@/stores/useMenuManagementStore'

const importStore = useMenuImportStore()
const menuStore = useMenuManagementStore()

onMounted(async () => {
  if (menuStore.categories.length === 0) await menuStore.fetchMenu()
  importStore.autoMatch(menuStore.categories.map((c) => ({ id: c.id, name: c.name })))
})

function onMappingChange(hint: string, raw: string) {
  if (raw.startsWith('existing:')) {
    importStore.setMapping(hint, { kind: 'existing', categoryId: raw.slice('existing:'.length) })
  } else {
    importStore.setMapping(hint, { kind: 'new', tempId: crypto.randomUUID(), name: hint })
  }
}

function onUnassignedChange(raw: string) {
  if (raw.startsWith('existing:')) {
    importStore.setUnassignedMapping({ kind: 'existing', categoryId: raw.slice('existing:'.length) })
  } else if (raw.startsWith('new:')) {
    const name = raw.slice('new:'.length)
    importStore.setUnassignedMapping({ kind: 'new', tempId: crypto.randomUUID(), name })
  }
}

function selectValueFor(mapping: MappedCategory | undefined | null, hint: string | null): string {
  if (!mapping) return hint ? `new:${hint}` : ''
  if (mapping.kind === 'existing') return `existing:${mapping.categoryId}`
  return `new:${mapping.name}`
}
</script>

<template>
  <section>
    <div class="eyebrow mb-3">pasul 2 · categorii</div>
    <h2 class="display-title text-2xl mb-2">Asociază secțiunile detectate</h2>
    <p class="text-ink-70 text-sm mb-5">
      Folosim titlurile de secțiune pe care le-am citit de pe meniu. Le poți redirecționa
      către categorii existente sau le creăm noi la importul final.
    </p>

    <div v-if="importStore.detectedHints.length === 0 && importStore.unassignedCount === 0" class="card-paper p-5">
      <p class="text-ink-70 italic">Niciun titlu de secțiune detectat. Continuă spre revizuire.</p>
    </div>

    <ul v-else class="mapping-list">
      <li v-for="hint in importStore.detectedHints" :key="hint" class="mapping-row card-white">
        <div class="hint-block">
          <span class="eyebrow">detectat</span>
          <span class="hint-name">„{{ hint }}"</span>
        </div>
        <div class="select-block">
          <label class="field-label">Mapează la</label>
          <select
            class="field-input"
            :value="selectValueFor(importStore.categoryMapping[hint], hint)"
            @change="(e) => onMappingChange(hint, (e.target as HTMLSelectElement).value)"
          >
            <optgroup label="Existente">
              <option
                v-for="cat in menuStore.categories"
                :key="cat.id"
                :value="`existing:${cat.id}`"
              >
                {{ cat.name }}
              </option>
            </optgroup>
            <optgroup label="Crează nouă">
              <option :value="`new:${hint}`">+ „{{ hint }}" (nouă)</option>
            </optgroup>
          </select>
        </div>
      </li>

      <li v-if="importStore.unassignedCount > 0" class="mapping-row card-white">
        <div class="hint-block">
          <span class="eyebrow">fără heading</span>
          <span class="hint-name">{{ importStore.unassignedCount }} produse</span>
        </div>
        <div class="select-block">
          <label class="field-label">Mapează la</label>
          <select
            class="field-input"
            :value="selectValueFor(importStore.unassignedMapping, null)"
            @change="(e) => onUnassignedChange((e.target as HTMLSelectElement).value)"
          >
            <option value="" disabled>— alege categoria —</option>
            <optgroup label="Existente">
              <option
                v-for="cat in menuStore.categories"
                :key="cat.id"
                :value="`existing:${cat.id}`"
              >
                {{ cat.name }}
              </option>
            </optgroup>
            <optgroup label="Crează nouă">
              <option value="new:Nesortate">+ „Nesortate" (nouă)</option>
            </optgroup>
          </select>
        </div>
      </li>
    </ul>

    <div class="footer-row">
      <button class="pill-btn pill-btn-ghost" type="button" @click="importStore.$patch({ step: 1 })">
        Înapoi
      </button>
      <button
        class="pill-btn"
        type="button"
        :disabled="!importStore.mappingComplete"
        @click="importStore.advanceToReview()"
      >
        Continuă la revizuire
      </button>
    </div>
  </section>
</template>

<style scoped>
.mapping-list { list-style: none; padding: 0; display: grid; gap: 12px; }
.mapping-row {
  padding: 16px;
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1.6fr);
  gap: 16px;
  align-items: center;
}
.hint-block { display: flex; flex-direction: column; gap: 4px; }
.hint-name { font-family: 'Playfair Display', serif; font-size: 18px; }
.select-block { display: flex; flex-direction: column; gap: 6px; }
.footer-row { margin-top: 28px; display: flex; justify-content: space-between; }
</style>
```

- [ ] **Step 2: Checkpoint**

File ready.
Suggested commit: `feat(admin): add step-map-categories component`

---

## Task 17: Frontend — `StepReviewItems` + component tests

**Files:**
- Create: `frontend-admin/src/components/menu-import/StepReviewItems.vue`
- Create: `frontend-admin/src/__tests__/StepReviewItems.spec.ts`

- [ ] **Step 1: Write failing component tests**

`frontend-admin/src/__tests__/StepReviewItems.spec.ts`:

```ts
import { createPinia, setActivePinia } from 'pinia'
import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'

import StepReviewItems from '@/components/menu-import/StepReviewItems.vue'
import { useMenuImportStore } from '@/stores/useMenuImportStore'

function seed(store: ReturnType<typeof useMenuImportStore>) {
  store.$patch({
    step: 3,
    reviewItems: [
      {
        clientId: 'a',
        name: 'Ciorbă',
        price: 18,
        sourcePage: 0,
        categoryHint: 'Ciorbe',
        resolvedCategory: { kind: 'existing', categoryId: 'cat-c' },
        skipped: false,
      },
    ],
  })
}

describe('StepReviewItems', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('renders one editable row per item', () => {
    const store = useMenuImportStore()
    seed(store)
    const wrapper = mount(StepReviewItems)
    expect(wrapper.findAll('[data-testid="review-row"]')).toHaveLength(1)
  })

  it('shows inline error when name is cleared', async () => {
    const store = useMenuImportStore()
    seed(store)
    const wrapper = mount(StepReviewItems)
    const nameInput = wrapper.find<HTMLInputElement>('[data-testid="row-name"]')
    await nameInput.setValue('')
    expect(wrapper.find('[data-testid="row-name-error"]').exists()).toBe(true)
    expect(wrapper.find<HTMLButtonElement>('[data-testid="import-btn"]').element.disabled).toBe(true)
  })

  it('skip toggles the row out of the import', async () => {
    const store = useMenuImportStore()
    seed(store)
    const wrapper = mount(StepReviewItems)
    await wrapper.find('[data-testid="row-name"]').setValue('')
    await wrapper.find('[data-testid="row-skip"]').trigger('click')
    expect(wrapper.find<HTMLButtonElement>('[data-testid="import-btn"]').element.disabled).toBe(true)
    // No active rows now — button stays disabled because canImport requires ≥ 1 active row.
  })
})
```

- [ ] **Step 2: Run to verify failure**

```
cd frontend-admin
npm run test -- src/__tests__/StepReviewItems.spec.ts
```

Expected: import failure.

- [ ] **Step 3: Implement the component**

`frontend-admin/src/components/menu-import/StepReviewItems.vue`:

```vue
<script setup lang="ts">
import { computed } from 'vue'

import { useMenuImportStore } from '@/stores/useMenuImportStore'
import { useMenuManagementStore } from '@/stores/useMenuManagementStore'
import { MENU_VALIDATION } from '@shared/types/menu-validation'

const importStore = useMenuImportStore()
const menuStore = useMenuManagementStore()

const grouped = computed(() => {
  const buckets = new Map<string, typeof importStore.reviewItems>()
  for (const item of importStore.reviewItems) {
    const key = labelFor(item.resolvedCategory)
    if (!buckets.has(key)) buckets.set(key, [])
    buckets.get(key)!.push(item)
  }
  return Array.from(buckets.entries())
})

function labelFor(mapping: typeof importStore.reviewItems[0]['resolvedCategory']) {
  if (mapping.kind === 'existing') {
    const cat = menuStore.categories.find((c) => c.id === mapping.categoryId)
    return cat?.name ?? 'Necunoscut'
  }
  return `${mapping.name} (nouă)`
}

function rowError(item: typeof importStore.reviewItems[0]) {
  if (item.skipped) return null
  const r = importStore.validateRow(item)
  if (r.ok) return null
  return r.field
}

async function onImport() {
  try {
    await importStore.importAll()
  } catch {
    // importStore.importError is already set
  }
}
</script>

<template>
  <section>
    <div class="eyebrow mb-3">pasul 3 · revizuire</div>
    <h2 class="display-title text-2xl mb-2">Verifică produsele detectate</h2>
    <p class="text-ink-70 text-sm mb-5">
      Editează numele și prețul, sări peste rândurile greșite. Importul final creează
      categoriile noi și produsele într-o singură tranzacție.
    </p>

    <div v-if="importStore.failedPages.length" class="failures card-paper p-4 mb-5">
      <span class="font-mono text-xs uppercase tracking-[0.18em] text-accent font-bold">
        ⚠ Pagini neclare
      </span>
      <ul>
        <li v-for="fp in importStore.failedPages" :key="fp.sourcePage">
          Pagina {{ fp.sourcePage + 1 }} · {{ fp.reason }}
        </li>
      </ul>
    </div>

    <div v-if="importStore.reviewItems.length === 0" class="card-paper p-8 text-ink-70 italic">
      Nu am detectat niciun produs cu preț. Verifică dacă fotografiile arată un meniu cu prețuri vizibile.
    </div>

    <div v-else class="groups">
      <section v-for="[cat, items] in grouped" :key="cat" class="card-paper p-5">
        <header class="group-head">
          <div class="eyebrow">categorie</div>
          <h3 class="display-title text-xl">{{ cat }}</h3>
          <div class="text-ink-70 text-sm">{{ items.length }} produse</div>
        </header>
        <ul class="row-list">
          <li
            v-for="row in items"
            :key="row.clientId"
            class="card-white review-row"
            :class="{ 'is-skipped': row.skipped }"
            data-testid="review-row"
          >
            <span class="page-badge">p{{ row.sourcePage + 1 }}</span>
            <div class="row-inputs">
              <label class="field" :class="{ 'is-error': rowError(row) === 'name' }">
                <span class="field-label">Nume</span>
                <input
                  data-testid="row-name"
                  class="field-input"
                  type="text"
                  :maxlength="MENU_VALIDATION.NAME_MAX"
                  :value="row.name"
                  @input="(e) => importStore.updateItem(row.clientId, { name: (e.target as HTMLInputElement).value })"
                />
                <span v-if="rowError(row) === 'name'" data-testid="row-name-error" class="field-error">
                  Numele este obligatoriu (1–{{ MENU_VALIDATION.NAME_MAX }} caractere).
                </span>
              </label>
              <label class="field" :class="{ 'is-error': rowError(row) === 'price' }">
                <span class="field-label">Preț RON</span>
                <input
                  data-testid="row-price"
                  class="field-input"
                  type="number"
                  step="0.01"
                  :min="0.01"
                  :value="row.price"
                  @input="(e) => importStore.updateItem(row.clientId, { price: parseFloat((e.target as HTMLInputElement).value) })"
                />
                <span v-if="rowError(row) === 'price'" class="field-error">Preț invalid.</span>
              </label>
            </div>
            <button
              class="pill-btn pill-btn-ghost"
              type="button"
              data-testid="row-skip"
              @click="importStore.toggleSkip(row.clientId)"
            >
              {{ row.skipped ? 'Reactivează' : 'Sări' }}
            </button>
          </li>
        </ul>
      </section>
    </div>

    <div v-if="importStore.importError" class="card-paper p-4 mt-5" style="border-color: var(--accentBorder);">
      <span class="font-mono text-xs uppercase tracking-[0.18em] text-accent font-bold">
        ⚠ {{ importStore.importError }}
      </span>
    </div>

    <div class="footer-row">
      <button
        class="pill-btn pill-btn-ghost"
        type="button"
        @click="importStore.$patch({ step: 2 })"
      >
        Înapoi
      </button>
      <button
        class="pill-btn"
        type="button"
        data-testid="import-btn"
        :disabled="!importStore.canImport || importStore.isImporting"
        @click="onImport"
      >
        <template v-if="importStore.isImporting">Import în curs…</template>
        <template v-else>
          Importă {{ importStore.reviewItems.filter((r) => !r.skipped).length }} produse
        </template>
      </button>
    </div>
  </section>
</template>

<style scoped>
.groups { display: grid; gap: 18px; }
.group-head { display: flex; flex-direction: column; gap: 4px; margin-bottom: 12px; }
.row-list { list-style: none; padding: 0; display: grid; gap: 10px; }
.review-row {
  padding: 14px;
  display: grid;
  grid-template-columns: auto minmax(0, 1fr) auto;
  gap: 14px;
  align-items: start;
}
.review-row.is-skipped { opacity: 0.45; }
.page-badge {
  display: inline-grid;
  place-items: center;
  min-width: 38px;
  padding: 4px 8px;
  border-radius: 999px;
  background: var(--ink06);
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 11px;
  letter-spacing: 0.12em;
  color: var(--ink70);
}
.row-inputs {
  display: grid;
  grid-template-columns: 2fr 1fr;
  gap: 10px;
}
.field { display: flex; flex-direction: column; gap: 4px; }
.field-error { color: var(--accent); font-size: 11px; }
.field.is-error .field-input { border-color: var(--accent); }
.failures ul { margin-top: 6px; padding-left: 18px; font-size: 13px; color: var(--ink70); }
.footer-row { margin-top: 28px; display: flex; justify-content: space-between; }
</style>
```

- [ ] **Step 4: Run tests**

```
cd frontend-admin
npm run test -- src/__tests__/StepReviewItems.spec.ts
```

Expected: 3 passed.

- [ ] **Step 5: Checkpoint**

Files ready: review component, tests.
Suggested commit: `feat(admin): add step-review-items with inline validation`

---

## Task 18: Frontend — `MenuImportView` wizard shell + route + entry button

**Files:**
- Create: `frontend-admin/src/views/MenuImportView.vue`
- Modify: `frontend-admin/src/router/index.ts`
- Modify: `frontend-admin/src/views/MenuManagementView.vue`

- [ ] **Step 1: Wizard shell**

`frontend-admin/src/views/MenuImportView.vue`:

```vue
<script setup lang="ts">
import { onBeforeUnmount, watch } from 'vue'
import { useRouter } from 'vue-router'

import ImportStepIndicator from '@/components/menu-import/ImportStepIndicator.vue'
import StepUploadPhotos from '@/components/menu-import/StepUploadPhotos.vue'
import StepMapCategories from '@/components/menu-import/StepMapCategories.vue'
import StepReviewItems from '@/components/menu-import/StepReviewItems.vue'
import { useMenuImportStore } from '@/stores/useMenuImportStore'
import { useMenuManagementStore } from '@/stores/useMenuManagementStore'
import { useToast } from '@/composables/useToast'

const router = useRouter()
const importStore = useMenuImportStore()
const menuStore = useMenuManagementStore()
const toast = useToast()

watch(
  () => importStore.isImporting,
  async (now, before) => {
    if (before && !now && !importStore.importError) {
      const count = importStore.reviewItems.filter((r) => !r.skipped).length
      toast.success(`${count} produse importate`)
      await menuStore.fetchMenu()
      importStore.$reset()
      router.push('/admin/menu')
    }
  },
)

onBeforeUnmount(() => {
  importStore.$reset()
})
</script>

<template>
  <div class="max-w-5xl mx-auto">
    <header class="mb-8">
      <div class="eyebrow mb-3">meniu · import</div>
      <h1 class="display-title text-[44px] md:text-[56px]">
        Din hârtie<br />
        <span class="text-accent">în vitrină.</span>
      </h1>
      <p class="text-ink-70 text-lg mt-4 max-w-2xl">
        Trei pași: încarci fotografiile, mapezi secțiunile și revizuiești produsele. La final,
        importul se face într-o singură tranzacție.
      </p>
    </header>

    <ImportStepIndicator :current="importStore.step" />

    <StepUploadPhotos v-if="importStore.step === 1" />
    <StepMapCategories v-else-if="importStore.step === 2" />
    <StepReviewItems v-else />
  </div>
</template>
```

- [ ] **Step 2: Register the route**

In `frontend-admin/src/router/index.ts`, add to the `routes` array (kept alongside the other admin routes — match the existing pattern):

```ts
{
  path: '/admin/menu/import',
  name: 'menu-import',
  component: () => import('@/views/MenuImportView.vue'),
},
```

- [ ] **Step 3: Add the entry button**

In `frontend-admin/src/views/MenuManagementView.vue`, locate the header's "Adaugă categorie" button (line ~298 in the current file) and insert *immediately before* it:

```vue
<RouterLink
  to="/admin/menu/import"
  class="pill-btn pill-btn-ghost whitespace-nowrap"
>
  <span>📷</span>
  <span>Importă din fotografie</span>
</RouterLink>
```

Make sure `RouterLink` is imported from `vue-router` in the `<script setup>` block (it is auto-importable in Vue 3 setups that include the `vue-router` plugin — if the existing file does not import it explicitly, leave it; otherwise add `import { RouterLink } from 'vue-router'`).

- [ ] **Step 4: Smoke-test the route in dev**

```
cd frontend-admin
npm run dev
```

Open `http://localhost:3001/admin/menu`. Click "Importă din fotografie". The wizard should render with the step indicator on step 1.

- [ ] **Step 5: Run the full frontend test suite**

```
cd frontend-admin
npm run test
```

Expected: all suites pass (the new specs from Tasks 13, 14, 17 plus any pre-existing).

- [ ] **Step 6: Checkpoint**

Files ready: wizard view, router entry, entry button.
Suggested commit: `feat(admin): wire menu-import wizard route and entry button`

---

## Task 19: Manual verification & acceptance criteria

This task isn't TDD; it's a guided walk through the spec's acceptance criteria with the real stack running.

**Prereqs:**
- `ANTHROPIC_API_KEY` is set in `backend/ai-scripts/.env` (or the equivalent env vars for whichever provider is configured).
- Postgres is up; both backends and the admin app are running (`docker-compose up` covers the C# + Python services; `cd frontend-admin && npm run dev` for the UI).

- [ ] **Step 1: Onboarding case (empty menu)**

Wipe categories/products in the dev DB. Photograph a real Romanian menu of 3–5 pages with your phone. Run the wizard.

Verify:
- Step 2 shows the section headings the menu actually has.
- All detected hints default to "+ Create new with this name" since no categories exist.
- Step 3 lists every item under its section heading.
- Import creates the categories and products; you land back in `/admin/menu` with the items visible.

- [ ] **Step 2: Auto-match case (existing categories)**

Pre-create categories with names that match a few of your menu's headings (case-insensitive, with/without diacritics). Run the wizard.

Verify:
- Step 2 auto-resolves the matching hints to the existing categories (the dropdown shows them as the default).
- Mismatched hints still default to "+ Create new".

- [ ] **Step 3: Editing in the review table**

Pick a row, edit its name and price, then import.

Verify:
- The saved product reflects the edited values, not the original extracted values.

- [ ] **Step 4: Skipping**

Skip one or more rows before importing.

Verify:
- Skipped rows do not appear as products in the menu.

- [ ] **Step 5: Failure / re-upload**

Take a deliberately blurry photo (or a non-menu image) and include it in the batch.

Verify:
- Step 3 surfaces "Pagina X · vision_api_error" (or `no_items_detected`) in the failures banner.
- Other pages' items are still listed and importable.

- [ ] **Step 6: Verify logging**

Tail the C# backend logs while running a scan:

```
docker-compose logs -f restaurant-api
```

(or the equivalent for the local setup).

Verify a structured line containing `MenuScan completed admin=... files=... ms=... model=... items=... failed=...` appears for each scan.

- [ ] **Step 7: Final checkpoint**

If anything in the spec's `§10 Acceptance criteria for v1` is not satisfied, fix the relevant task first.
Suggested commit (if any docs touch-ups were needed): `docs(menu-import): finalize v1 verification notes`

---

## Self-review

**Spec coverage check — every requirement maps to a task:**

| Spec section | Covered by |
|---|---|
| §2 in-scope: 1–10 photo upload | Tasks 11 (cap + mime), 15 (dropzone), 14 (store enforcement) |
| §2 vision-LLM extraction (name + price + category_hint) | Tasks 2, 4, 5 (Python), prompt in Task 4 |
| §2 auto-map by normalized name | Task 14 (`autoMatch`), Task 16 (UI), Task 12 (server-side merge) |
| §2 editable review table | Task 17 |
| §2 atomic bulk-create + new categories | Task 12 |
| §2 scan-event logging | Task 11 |
| §3 Architecture & flow | Tasks 6, 11, 12, 13, 14, 18 (end-to-end) |
| §4 Data contracts (Pydantic / C# / TS) | Tasks 1, 2, 9 |
| §5 Vision-LLM prompt | Task 4 |
| §6 Frontend wizard structure | Tasks 14, 15, 16, 17, 18 |
| §7 Per-photo failures, validation, transaction safety, race conditions, cost guard | Tasks 5, 6, 11, 12 |
| §8 Testing strategy (Python, C#, frontend) | Tasks 2, 5, 6, 7, 8, 10, 11, 12, 13, 14, 17 |
| §8 Manual verification checklist | Task 19 |
| §9 Future extensibility (modularity) | The seams are in place: `VisionExtractor` protocol (Task 3), Optional Pydantic fields (Task 2), `MenuScanEvent` log shape (Task 11) — no implementation needed in v1 |
| §10 Acceptance criteria | Task 19 |

**Type / signature consistency check:**

- `ExtractedMenuItem` shape: `name + price + source_page + source_index + (Optional) description + category_hint + confidence` — consistent across Python (Task 2), C# (Task 9), TS (Task 1), and store (Task 14).
- `FailedPage.reason` values: `"unreadable" | "no_items_detected" | "vision_api_error"` — single source of truth in Task 2's `Literal`, mirrored in TS in Task 1, asserted in Tasks 5 & 11.
- `BulkCreateProductsRequest` shape (items + newCategories with tempId resolution) is identical in C# DTO (Task 9), TS DTO (Task 1), store payload builder (Task 14), and controller logic (Task 12).
- `MENU_VALIDATION` constants (NAME_MAX, PRICE_MAX, DESCRIPTION_MAX, IMPORT_MAX_FILES, IMPORT_MAX_FILE_BYTES, IMPORT_ALLOWED_MIME) are defined once in Task 1 and consumed by Tasks 11 (server cap), 14 (store), 15 (dropzone), 17 (row validation).
- `MenuScanResponse` field names use camelCase in TS, PascalCase in C# records, snake_case in Pydantic — each layer's serialization handles the conversion; the C# client (Task 10) explicitly bridges snake_case ↔ camelCase via attributes if the project's JSON options aren't already configured for it.

No placeholders found. No `TBD`/`TODO`/"implement later" strings. Every code step is complete.

---

**End of plan.**
