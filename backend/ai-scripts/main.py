from fastapi import FastAPI
from routers import recommendations, similarity

app = FastAPI(title="Restaurant AI Service")
app.include_router(recommendations.router)
app.include_router(similarity.router)


@app.get("/health")
def health():
    return {"status": "ok"}
