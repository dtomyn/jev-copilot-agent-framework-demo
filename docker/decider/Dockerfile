FROM python:3.12-slim

ARG DECIDER_AI_VERSION=1.6.0
ARG DECIDER_BACKEND=cpu

ENV PYTHONUNBUFFERED=1 \
    PIP_DISABLE_PIP_VERSION_CHECK=1 \
    PIP_NO_CACHE_DIR=1 \
    HF_HOME=/models

# decider-ai declares flash-linear-attention without a backend extra. Install the matching
# PyTorch/FLA stack first, then install decider-ai without dependency resolution so Docker never
# silently mixes a CUDA stack into the CPU image (or vice versa).
RUN python -m pip install --upgrade pip && \
    if [ "$DECIDER_BACKEND" = "cuda" ]; then \
        python -m pip install "flash-linear-attention[cuda]"; \
    elif [ "$DECIDER_BACKEND" = "cpu" ]; then \
        # Released triton (>=3.x) ships only CUDA/ROCm drivers. On CPU its kernels raise
        # "0 active drivers" the first time transformers' hub-kernel dispatch tries to autotune,
        # and USE_HUB_KERNELS=NO does not help because the dispatch still prefers FLA when it is
        # importable. So on CPU we intentionally do NOT install flash-linear-attention or triton:
        # transformers then falls through to its pure-PyTorch reference path for gated_delta_rule,
        # which is slower but actually runs.
        python -m pip install --index-url https://download.pytorch.org/whl/cpu torch; \
    else \
        echo "DECIDER_BACKEND must be cpu or cuda, not '$DECIDER_BACKEND'" >&2; exit 2; \
    fi && \
    python -m pip install --no-deps "decider-ai==${DECIDER_AI_VERSION}" && \
    python -m pip install "transformers>=5" "numpy<2" huggingface_hub jinja2 fastapi "uvicorn[standard]" httpx einops

EXPOSE 8000

# Bind to all interfaces *inside* the container; compose publishes it only on host loopback.
CMD ["uvicorn", "decider.serve:app", "--host", "0.0.0.0", "--port", "8000"]
