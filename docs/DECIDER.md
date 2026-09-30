# Running with Decider

[Decider](https://github.com/Mapika/decider) is a local, open-source System-One decision engine. Its HTTP server intentionally exposes the same `POST /v1/systemone` request/response shape used by Jev, so this repository uses the same `SystemOneHttpClient`, DTOs, policy gate, and ten levels for all three providers.

This repository targets the Hugging Face model `Mapika/decider-4b` by default. The model is selected by the Decider server (`DECIDER_MODEL`), not by the request body.

## Windows 11 quick start

The recommended Windows path is Docker Desktop using Linux containers. The repository builds a small Decider image locally rather than depending on an upstream prebuilt image.

Prerequisites:

- Windows 11 with Docker Desktop using the WSL 2 backend.
- .NET 10 SDK for the demo application.
- For GPU mode: a supported NVIDIA GPU, current Windows NVIDIA drivers, and Docker Desktop GPU support through WSL 2.

From PowerShell 7+:

```powershell
# CPU image; first start downloads/builds dependencies and model weights.
.\scripts\decider-up.ps1

# Or, for a supported NVIDIA GPU:
.\scripts\decider-up.ps1 -Gpu
```

The script waits for Decider's `/health` endpoint before returning. It binds the service to loopback only at `http://127.0.0.1:8011` and keeps Hugging Face downloads in a named Docker volume so restarts do not redownload the model.

Run a level against it:

```powershell
$env:DECIDER_BASE_URL = "http://127.0.0.1:8011"
dotnet run --project src/TenLevels.Jev -- --level 4 --provider decider --mode live
```

Or start the presentation UI and Decider together:

```powershell
.\scripts\webui.ps1 -Decider       # CPU
.\scripts\webui.ps1 -DeciderGpu    # NVIDIA GPU
```

Stop the server without deleting the cached model:

```powershell
.\scripts\decider-down.ps1
```

Delete the container **and** the cached model volume:

```powershell
.\scripts\decider-down.ps1 -RemoveVolumes
```

The equivalent macOS/Linux helper scripts are `scripts/decider-up.sh` and `scripts/decider-down.sh`. The CPU path is primarily intended as a compatibility/demo path; the 4B model is much more practical with GPU acceleration.

## What the Docker files do

The image definition is under `docker/decider/`:

- `Dockerfile` installs `decider-ai`, its serving dependencies, and the matching Flash Linear Attention backend.
- `compose.yml` exposes container port 8000 as host `127.0.0.1:8011`, mounts a persistent Hugging Face cache, and starts `uvicorn decider.serve:app`.
- `compose.gpu.yml` switches the image build to the CUDA backend, requests the NVIDIA GPU, and sets `DECIDER_DEVICE=cuda`.

The default build pins `DECIDER_AI_VERSION=1.6.0` so a future package release cannot silently change the demo. Override it before building only after checking upstream compatibility.

Useful configuration:

```text
DECIDER_BASE_URL=http://127.0.0.1:8011   # consumed by this .NET app
DECIDER_MODEL=Mapika/decider-4b          # consumed by the Decider server
DECIDER_DEVICE=auto                       # auto | cpu | cuda (server-side)
DECIDER_HOST_PORT=8011                    # host port used by Docker Compose
DECIDER_AI_VERSION=1.6.0                  # image build argument
```

For a different model, set it before the first start/rebuild, for example:

```powershell
$env:DECIDER_MODEL = "Mapika/decider-2b"
.\scripts\decider-up.ps1
```

## Why this works without a Decider-specific client

Decider's server is wire-compatible with Jev for `POST /v1/systemone`. The application therefore adds only provider configuration, not another HTTP stack:

- no bearer token is sent;
- the request omits `model`, because the Decider process owns model selection;
- Decider's `x_p_max` is read as the provider-independent `AnswerConfidence` used by policy thresholds;
- Decider's own `confidence` value is retained for display, but policy never thresholds on it.

That last point matters. Choice confidence is TypeSafe-compatible, while Score confidence uses Decider's score-distance formula. The policy gate always thresholds on `max(p)` instead, so the same code-owned thresholds have the same meaning when switching among Jev, Laya, and Decider.

## Model size and GGUF

`Mapika/decider-4b` is a 4.2B-parameter BF16 checkpoint and its published weights are about 8.4 GB before runtime overhead. On a GPU, leave additional VRAM headroom beyond the raw weight size. On CPU, expect substantially higher latency than the hook's short live-call budget; use the UI/levels for experimentation rather than assuming a cold CPU model can answer inside a Copilot hook deadline.

Decider also publishes a smaller quantized GGUF path (for example Q4_K_M). Current upstream Decider can load GGUF through its Python `Decider(...)` API, but its HTTP server and `schema()` path still require the Torch engine. For that reason this repository does **not** pretend the GGUF file is a drop-in replacement for the HTTP server. Supporting GGUF cleanly would require a small server adapter around Decider's GGUF engine.

## Native Python on Windows

Upstream Decider is a Python package (`Python >= 3.11`) and can be served directly with Uvicorn, but its acceleration stack is Linux/CUDA-oriented. On Windows 11, Docker Desktop/WSL 2 gives a more reproducible environment and is the supported path in this repository. If you already maintain a working Decider server elsewhere, the .NET app only needs:

```powershell
$env:DECIDER_BASE_URL = "http://host-or-wsl:8000"
```

No Docker-specific behavior exists in the application itself.

## Troubleshooting

Check service health:

```powershell
Invoke-RestMethod http://127.0.0.1:8011/health
```

Follow server logs:

```powershell
docker compose -p jev-decider -f docker/decider/compose.yml logs -f decider
```

For GPU mode, also include the override file:

```powershell
docker compose -p jev-decider -f docker/decider/compose.yml -f docker/decider/compose.gpu.yml logs -f decider
```

If the model does not fit your GPU, use a smaller Torch checkpoint such as `Mapika/decider-2b`, or run CPU mode. Do not expose port 8000/8011 to an untrusted network without adding your own authentication and transport security: Decider's local server is intentionally unauthenticated.

## Upstream references

- Decider repository: <https://github.com/Mapika/decider>
- `Mapika/decider-4b` model card: <https://huggingface.co/Mapika/decider-4b>
- Python package: <https://pypi.org/project/decider-ai/>
