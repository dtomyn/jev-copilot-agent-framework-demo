"""Serves Cloudflare Clef as a local System-One endpoint.

Clef's weights ship with `joint_schema_model.py`, whose `systemone()` function already takes a
`POST /v1/systemone` request body and returns the response body. It does not ship an HTTP server,
so this module is that server: it loads the model once, then exposes

    GET  /health        200 once the model is loaded, 503 while it is still loading
    POST /v1/systemone  the System-One contract, the same one Jev, Laya, and Decider speak

Only text and JSON state is accepted. Clef's `images` extension is refused rather than half
supported, because nothing in the demo sends images and an unvalidated decode path is attack
surface for no benefit.
"""

from __future__ import annotations

import asyncio
import hmac
import json
import logging
import os
import re
import sys
import threading
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from typing import Any

from fastapi import FastAPI, Request
from fastapi.concurrency import run_in_threadpool
from fastapi.responses import JSONResponse

log = logging.getLogger("clef")
logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")

# joint_schema_model.py is code from the model repository and runs inside this process, so the
# revision is pinned: a moved `main` must never change what executes here. Bump it deliberately.
PINNED_REVISIONS = {
    "clef-flash": "17f0b0ad64efb65d273590632833508766b2aae6",
}

MODEL = os.environ.get("CLEF_MODEL", "clef-flash").strip()
REPOSITORY = os.environ.get("CLEF_REPOSITORY", f"Cloudflare/{MODEL}")
REVISION = os.environ.get("CLEF_REVISION", "").strip() or PINNED_REVISIONS.get(MODEL)
DEVICE = os.environ.get("CLEF_DEVICE", "auto").strip().lower()
API_KEY = os.environ.get("CLEF_API_KEY", "")
MAX_QUEUED = int(os.environ.get("CLEF_MAX_QUEUED", "4"))

MAX_BODY_BYTES = 2 * 1024 * 1024
MAX_STATE_CHARS = 50_000
MAX_QUESTIONS = 64
QUESTION_ID = re.compile(r"^[A-Za-z0-9_.\-]{1,100}$")

if MODEL not in ("clef", "clef-flash"):
    sys.exit(f"CLEF_MODEL must be 'clef' or 'clef-flash', not '{MODEL}'.")
if REVISION is None:
    sys.exit(f"Set CLEF_REVISION to a commit of {REPOSITORY}; only clef-flash has a pinned default.")

_state: dict[str, Any] = {"model": None, "processor": None, "systemone": None, "device": None, "error": None}
_inference = asyncio.Lock()
_queued = 0


def _load() -> None:
    try:
        import torch
        from huggingface_hub import snapshot_download

        device = DEVICE
        if device == "auto":
            device = "cuda" if torch.cuda.is_available() else "cpu"

        log.info("Downloading %s@%s (cached in HF_HOME after the first start)...", REPOSITORY, REVISION)
        path = snapshot_download(REPOSITORY, revision=REVISION)
        sys.path.insert(0, path)
        from joint_schema_model import load_release_model, systemone

        log.info("Loading %s on %s...", MODEL, device)
        model, processor = load_release_model(path, device=device)
        _state.update(model=model, processor=processor, systemone=systemone, device=device)
        log.info("%s is ready on %s.", MODEL, device)
    except Exception as error:  # Reported by /health; the container healthcheck then fails.
        log.exception("Loading %s failed.", MODEL)
        _state["error"] = f"{type(error).__name__}: {error}"


@asynccontextmanager
async def _lifespan(_: FastAPI) -> AsyncIterator[None]:
    # Loaded off the event loop, so /health can answer "loading" during a long first download.
    threading.Thread(target=_load, name="clef-load", daemon=True).start()
    yield


app = FastAPI(title="clef-serve", docs_url=None, redoc_url=None, openapi_url=None, lifespan=_lifespan)


def _error(status: int, message: str) -> JSONResponse:
    return JSONResponse(status_code=status, content={"error": {"message": message}})


@app.get("/health")
def health() -> JSONResponse:
    if _state["error"] is not None:
        return JSONResponse(status_code=500, content={"status": "failed", "model": MODEL, "error": _state["error"]})
    if _state["model"] is None:
        return JSONResponse(status_code=503, content={"status": "loading", "model": MODEL})
    return JSONResponse(content={"status": "ok", "model": MODEL, "revision": REVISION, "device": _state["device"]})


def _validate(body: Any) -> str | None:
    if not isinstance(body, dict):
        return "The request body must be a JSON object."
    if "images" in body or "videos" in body:
        return "This local server accepts text and JSON state only."
    if "state" not in body or body["state"] is None:
        return "'state' is required."
    state = body["state"]
    state_chars = len(state) if isinstance(state, str) else len(json.dumps(state))
    if state_chars > MAX_STATE_CHARS:
        return f"'state' is larger than {MAX_STATE_CHARS} characters."
    model = body.get("model")
    if model is not None and (not isinstance(model, str) or model.strip() != MODEL):
        return f"This server serves '{MODEL}', not '{model}'."
    questions = body.get("questions")
    if not isinstance(questions, dict) or not 1 <= len(questions) <= MAX_QUESTIONS:
        return f"'questions' must be an object with 1 to {MAX_QUESTIONS} entries."
    for question_id, question in questions.items():
        if not QUESTION_ID.match(question_id):
            return f"Question id '{question_id[:100]}' must use letters, digits, '_', '.', or '-'."
        if not isinstance(question, dict) or question.get("type") not in ("noul", "choice", "score"):
            return f"Question '{question_id}' must have type noul, choice, or score."
    return None


@app.post("/v1/systemone")
async def decide(request: Request) -> JSONResponse:
    global _queued

    if API_KEY:
        supplied = request.headers.get("authorization", "")
        if not hmac.compare_digest(supplied.encode(), f"Bearer {API_KEY}".encode()):
            return _error(401, "A valid bearer token is required.")

    raw = await request.body()
    if len(raw) > MAX_BODY_BYTES:
        return _error(413, f"The request body is larger than {MAX_BODY_BYTES} bytes.")
    try:
        body = json.loads(raw)
    except ValueError:
        return _error(400, "The request body is not valid JSON.")

    problem = _validate(body)
    if problem is not None:
        return _error(413 if "larger than" in problem else 400, problem)

    if _state["model"] is None:
        return _error(503, f"{MODEL} is still loading; poll /health.")

    # One model on one device: requests run one at a time. A short queue absorbs bursts; beyond
    # it the caller gets an explicit 503 instead of an unbounded wait.
    if _queued >= MAX_QUEUED:
        return _error(503, "Too many requests are already waiting.")

    _queued += 1
    try:
        async with _inference:
            request_body = {"model": MODEL, "state": body["state"], "questions": body["questions"]}
            response = await run_in_threadpool(
                _state["systemone"], _state["model"], _state["processor"], request_body)
    except (ValueError, KeyError, TypeError) as error:
        return _error(400, f"{type(error).__name__}: {error}")
    finally:
        _queued -= 1

    return JSONResponse(content=response)
