import hmac
import logging
import os

from fastapi import Depends, FastAPI, Header, HTTPException, status

from routers import recommendations, similarity, directives, menu_extraction
from dotenv import load_dotenv

load_dotenv()

logger = logging.getLogger(__name__)

INTERNAL_API_KEY_HEADER = "X-Internal-Api-Key"


def require_internal_api_key(
    x_internal_api_key: str | None = Header(default=None, alias=INTERNAL_API_KEY_HEADER),
) -> None:
    """Shared-secret gate for internal (server-to-server) callers.

    When AI_INTERNAL_API_KEY is set, every non-health route requires a matching
    header. When it is unset the check is skipped so local dev keeps working
    (a startup warning is logged in that case).
    """
    expected = os.getenv("AI_INTERNAL_API_KEY")
    if not expected:
        return
    if not x_internal_api_key or not hmac.compare_digest(x_internal_api_key, expected):
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Invalid or missing internal API key.",
        )


if not os.getenv("AI_INTERNAL_API_KEY"):
    logger.warning(
        "AI_INTERNAL_API_KEY is not set — internal API authentication is DISABLED; "
        "every non-health endpoint is reachable without a shared secret."
    )

app = FastAPI(title="Restaurant AI Service")

# Apply the shared-secret gate to every router (not /health, which stays open
# for liveness probes).
_auth = [Depends(require_internal_api_key)]
app.include_router(recommendations.router, dependencies=_auth)
app.include_router(similarity.router, dependencies=_auth)
app.include_router(directives.router, dependencies=_auth)
app.include_router(menu_extraction.router, dependencies=_auth)

@app.get("/health")
def health():
    return {"status": "ok"}
