"""
Programmatic fixture generator for sample_menus PDFs.
Run directly or imported by conftest.py.
All PDFs are built with reportlab — no binary blobs in the repo.
"""
import io
import os

from reportlab.lib.pagesizes import A4
from reportlab.lib.units import cm
from reportlab.pdfgen import canvas

FIXTURES_DIR = os.path.dirname(os.path.abspath(__file__))


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def _tiny_jpeg_bytes() -> bytes:
    """Return a minimal 8×8 white JPEG using Pillow."""
    from PIL import Image as PilImage
    buf = io.BytesIO()
    img = PilImage.new("RGB", (8, 8), color=(255, 255, 255))
    img.save(buf, format="JPEG", quality=85)
    return buf.getvalue()


MENU_PAGES = [
    {
        "heading": "APERITIVE",
        "items": [
            ("Bruschetă cu roșii", "12,50 lei"),
            ("Platou mezeluri", "38,00 RON"),
            ("Supă cremă de dovleac", "18,00 lei"),
            ("Ardei umpluți cu brânză", "22,90 lei"),
            ("Salată orientală", "15,00 lei"),
        ],
    },
    {
        "heading": "FEL PRINCIPAL",
        "items": [
            ("Ciorbă de burtă", "24,90 lei"),
            ("Mușchiuleț de porc", "45,00 RON"),
            ("Grătar mixt de carne", "62,50 lei"),
            ("Pui la grătar cu sos", "38,00 lei"),
            ("Sarmale tradiționale", "32,00 lei"),
        ],
    },
    {
        "heading": "DESERTURI",
        "items": [
            ("Tort de ciocolată", "19,90 lei"),
            ("Papanași cu smântână", "16,00 RON"),
            ("Clătite cu dulceață", "14,50 lei"),
            ("Înghețată artizanală", "12,00 lei"),
            ("Prăjitură de casă", "10,00 lei"),
        ],
    },
]


def _write_text_page(c: canvas.Canvas, page_data: dict) -> None:
    w, h = A4
    y = h - 3 * cm
    c.setFont("Helvetica-Bold", 16)
    c.drawString(2 * cm, y, page_data["heading"])
    y -= 1 * cm
    c.setFont("Helvetica", 12)
    for name, price in page_data["items"]:
        c.drawString(2 * cm, y, f"{name}  .....  {price}")
        y -= 0.8 * cm
    c.showPage()


def build_digital_3page(dest: str) -> None:
    """3-page all-text PDF with menu items and Romanian RON prices."""
    c = canvas.Canvas(dest, pagesize=A4)
    for page_data in MENU_PAGES:
        _write_text_page(c, page_data)
    c.save()


def build_scanned_3page(dest: str) -> None:
    """3-page PDF where each page is an embedded JPEG image (no text layer)."""
    jpeg = _tiny_jpeg_bytes()
    c = canvas.Canvas(dest, pagesize=A4)
    w, h = A4
    img_buf = io.BytesIO(jpeg)
    for _ in range(3):
        from reportlab.lib.utils import ImageReader
        img_buf.seek(0)
        reader = ImageReader(img_buf)
        c.drawImage(reader, 0, 0, width=w, height=h)
        c.showPage()
    c.save()


def build_mixed_5page(dest: str) -> None:
    """5-page PDF: pages 0,2,4 are text; pages 1,3 are image-only."""
    jpeg = _tiny_jpeg_bytes()
    c = canvas.Canvas(dest, pagesize=A4)
    w, h = A4

    from reportlab.lib.utils import ImageReader

    for i in range(5):
        if i % 2 == 0:  # text pages: 0, 2, 4
            page_data = MENU_PAGES[i % len(MENU_PAGES)]
            _write_text_page(c, page_data)
        else:  # image-only pages: 1, 3
            img_buf = io.BytesIO(jpeg)
            reader = ImageReader(img_buf)
            c.drawImage(reader, 0, 0, width=w, height=h)
            c.showPage()
    c.save()


def build_all(force: bool = False) -> dict[str, str]:
    """Build all three fixture PDFs. Returns mapping name -> path."""
    paths = {
        "digital_3page": os.path.join(FIXTURES_DIR, "digital_3page.pdf"),
        "scanned_3page": os.path.join(FIXTURES_DIR, "scanned_3page.pdf"),
        "mixed_5page": os.path.join(FIXTURES_DIR, "mixed_5page.pdf"),
    }
    if force or not os.path.exists(paths["digital_3page"]):
        build_digital_3page(paths["digital_3page"])
    if force or not os.path.exists(paths["scanned_3page"]):
        build_scanned_3page(paths["scanned_3page"])
    if force or not os.path.exists(paths["mixed_5page"]):
        build_mixed_5page(paths["mixed_5page"])
    return paths


if __name__ == "__main__":
    result = build_all(force=True)
    for name, path in result.items():
        size = os.path.getsize(path)
        print(f"{name}: {path} ({size} bytes)")
