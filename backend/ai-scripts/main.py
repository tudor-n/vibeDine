from fastapi import FastAPI
from routers import recommendations, similarity, directives, menu_extraction
from dotenv import load_dotenv

load_dotenv()

app = FastAPI(title="Restaurant AI Service")

app.include_router(recommendations.router)
app.include_router(similarity.router)
app.include_router(directives.router)
app.include_router(menu_extraction.router)

@app.get("/health")
def health():
    return {"status": "ok"}