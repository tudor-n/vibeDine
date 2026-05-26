from uuid import uuid4
from models.menu_extraction import PdfJobRequest


def test_pdf_job_request_locale_defaults_to_ro():
    req = PdfJobRequest(
        job_id=uuid4(), restaurant_id=uuid4(),
        storage_path="x", progress_callback_url="http://x",
        hmac_secret="abcdefgh",
    )
    assert req.locale == "ro-RO"


def test_pdf_job_request_locale_override():
    req = PdfJobRequest(
        job_id=uuid4(), restaurant_id=uuid4(),
        storage_path="x", progress_callback_url="http://x",
        hmac_secret="abcdefgh", locale="en-US",
    )
    assert req.locale == "en-US"
