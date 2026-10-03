FROM python:3.12-slim

ARG CLEF_BACKEND=cpu

ENV PYTHONUNBUFFERED=1 \
    PIP_DISABLE_PIP_VERSION_CHECK=1 \
    PIP_NO_CACHE_DIR=1 \
    HF_HOME=/models

# The model card is tested with torch 2.11 and transformers 5.10.2, so those are the floors. The
# CPU wheel index keeps several gigabytes of CUDA libraries out of the CPU image; the default
# index ships CUDA wheels for the GPU image.
RUN python -m pip install --upgrade pip && \
    if [ "$CLEF_BACKEND" = "cuda" ]; then \
        python -m pip install "torch>=2.11"; \
    elif [ "$CLEF_BACKEND" = "cpu" ]; then \
        python -m pip install --index-url https://download.pytorch.org/whl/cpu "torch>=2.11"; \
    else \
        echo "CLEF_BACKEND must be cpu or cuda, not '$CLEF_BACKEND'" >&2; exit 2; \
    fi && \
    python -m pip install "transformers>=5.10.2" accelerate safetensors huggingface_hub pillow \
        fastapi "uvicorn[standard]"

# Model code and weights are not baked into the image: server.py downloads the pinned revision
# into the HF_HOME volume on first start, so a rebuild does not throw away ~19 GB of weights.
RUN useradd --create-home --uid 10001 clef && mkdir -p /models && chown clef /models
COPY --chown=clef docker/clef/server.py /app/server.py
WORKDIR /app
USER clef

EXPOSE 8000

# Bind to all interfaces *inside* the container; compose publishes it only on host loopback.
CMD ["uvicorn", "server:app", "--host", "0.0.0.0", "--port", "8000"]
