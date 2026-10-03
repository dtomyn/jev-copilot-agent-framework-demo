# Running the demo on Clef

[Clef-Flash](https://developers.cloudflare.com/workers-ai/models/clef-flash/) is Cloudflare's 9B multimodal decision model.
It answers the same three typed primitives this demo is built on - `noul`, `choice`, and `score` - with a probability for every allowed option, in a single forward pass.
Its model card states that the API is fully compatible with Jev and SystemOne, so it plugs into the same client and DTOs as Jev, Laya, and Decider.
Everything Clef-specific lives in `SystemOneEndpoint` (`src/Jev.Core/DecisionProviders.cs`); policy code does not know it exists.

The weights are open under Apache-2.0 on Hugging Face as [`Cloudflare/clef-flash`](https://huggingface.co/Cloudflare/clef-flash), post-trained from Qwen3.5-9B.
A larger variant, `clef`, can be selected with `CLEF_MODEL=clef`.

## Two ways to run it

| | Hosted (Workers AI) | Local (Docker) |
|---|---|---|
| Selected by | `CLOUDFLARE_ACCOUNT_ID` and `CLOUDFLARE_API_TOKEN` | `CLEF_BASE_URL` |
| Endpoint | `https://api.cloudflare.com/client/v4/accounts/{id}/ai/run/@cf/cloudflare/clef-flash` | `http://127.0.0.1:8012/v1/systemone` |
| Response | wrapped in Cloudflare's `{ "success", "errors", "result" }` envelope | the plain System-One body |
| Credentials | Cloudflare API token, always | `CLEF_API_KEY`, only if the container was started with one |
| Hardware | none | about 19 GB for BF16 weights: a 24 GB+ NVIDIA GPU, or ~24 GB of container RAM on CPU |
| Where state goes | Cloudflare | a container on your machine |
| Cost | $0.09 per million input tokens | your hardware |

Both deployments send `"model": "clef-flash"` in every request, because Clef rejects a request without one.

When `CLEF_BASE_URL` is set it always wins, even if Cloudflare credentials are also present.
That is deliberate: a local setup must never quietly fall back to sending tool calls to a hosted service.
For the same reason, Cloudflare credentials alone never *select* Clef when `DECISION_PROVIDER` is unset; they only make it available.

## Hosted: Cloudflare Workers AI

This is the path that works on an ordinary laptop.

1. Find your account ID in the Cloudflare dashboard.
2. Create an API token from the **Workers AI** token template, scoped to that account.
3. Export both, and select the provider:

```powershell
$env:CLOUDFLARE_ACCOUNT_ID = "<account id>"
$env:CLOUDFLARE_API_TOKEN = "<token>"
dotnet run --project src/TenLevels.Jev -- --level 4 --provider clef --mode live
```

```bash
export CLOUDFLARE_ACCOUNT_ID=<account id>
export CLOUDFLARE_API_TOKEN=<token>
dotnet run --project src/TenLevels.Jev -- --level 4 --provider clef --mode live
```

The account ID becomes part of the request path, so the client refuses anything but letters and digits rather than let a stray `/` or `?` change the route.
The presentation UI picks the same variables up from its own environment and shows Clef's Live button as available, with a note that requests go to Cloudflare.

For the Copilot hook, set the variables **before** starting Copilot CLI, since repository hook configuration is read when the CLI starts:

```powershell
$env:DECISION_PROVIDER = "clef"
$env:DECISION_MODE = "live"
copilot
```

## Local: Docker

> **Untested on the machine this was written on.**
> The container is wired up the same way as Decider's, but Clef-Flash needs about 19 GB just for its BF16 weights.
> A laptop GPU with 4 GB cannot hold it, and on CPU each decision takes seconds, far beyond the hook's 2-second HTTP cap.
> Use this path on a workstation or server with a 24 GB+ NVIDIA GPU; use the hosted path everywhere else.

Clef ships no HTTP server.
Its weights come with `joint_schema_model.py`, whose `systemone()` function takes a System-One request body and returns the response body.
`docker/clef/server.py` is a small FastAPI wrapper around that function, exposing `POST /v1/systemone` and `/health`.
All Python dependencies (PyTorch, Transformers) are installed inside the image; nothing is installed on the host.

```powershell
./scripts/clef-up.ps1 -Gpu
```

```bash
./scripts/clef-up.sh --gpu
```

Without `-Gpu`/`--gpu` the image is CPU-only, which works but is too slow to be useful for the hook.
The script runs `docker compose up` and then waits for `/health`, because the first start downloads roughly 19 GB of weights into the named volume `jev-clef_clef-model-cache`.
`/health` answers `503` while the weights download and load, `200` once the model is ready, and `500` if loading failed, in which case the script prints the container logs.

To start the container and the presentation UI in one step, with Live enabled for local Clef:

```powershell
./scripts/webui.ps1 -ClefGpu
```

```bash
./scripts/webui.sh --clef-gpu
```

Stop it with `./scripts/clef-down.ps1` (add `-RemoveVolumes`, or `--volumes` for the shell version, only if you want the next start to download the weights again).

The container publishes on loopback port **8012**, next to Laya on 8010 and Decider on 8011, so all three local providers can be live in the UI at once.

### What the container does and does not do

- **The model code is pinned.** `joint_schema_model.py` is code from the model repository and runs inside the server process, so the download is pinned to a specific commit rather than `main`. Bump `PINNED_REVISIONS` in `server.py` deliberately, after reading the diff. Only `clef-flash` has a pinned default; `CLEF_MODEL=clef` also needs `CLEF_REVISION`.
- **Text and JSON state only.** Clef's `images` extension is refused with a `400`. Nothing in this demo sends images, and an unvalidated image decoder is attack surface for no benefit.
- **The same request limits as `laya-serve`.** A state over 50,000 characters, more than 64 questions, or a body over 2 MiB gets a `413`.
- **One request at a time.** One model on one device runs requests sequentially. A queue of `CLEF_MAX_QUEUED` (default 4) absorbs bursts; beyond it the caller gets a `503`, which the gate turns into `ask`.
- **Unprivileged.** The server runs as a non-root user and is published only on host loopback.

## `confidence`

Clef reports `confidence` as `max(p)`, for both `choice` and `score` answers.
That happens to be the same quantity `CopilotToolGate` thresholds on (`AnswerConfidence`), but the client does not rely on the coincidence: as for Jev, it recovers `max(p)` from the returned distribution.
So for a three-way `allow`/`ask`/`deny` answer with probabilities `0.94 / 0.03 / 0.03`, Clef reports `0.94`, Jev `0.91`, and Laya `0.7555`, and every one of them produces the same gate decision.
`tests/Jev.Core.SelfTests` pins that: the offline mock reproduces Clef's definition, and switching provider to Clef changes no gate decision.

## Variables

| Variable | Default | Read by | Meaning |
|---|---|---|---|
| `CLOUDFLARE_ACCOUNT_ID` | | HTTP client / UI | Account whose Workers AI runs the model. Letters and digits only. |
| `CLOUDFLARE_API_TOKEN` | | HTTP client / UI | Workers AI token, sent as a bearer token to Cloudflare only. |
| `CLEF_BASE_URL` | | HTTP client / UI | A local Clef server. Takes precedence over the Cloudflare variables, and selects Clef when `DECISION_PROVIDER` is unset. |
| `CLEF_API_KEY` | | HTTP client / container | Optional bearer token for the local server. |
| `CLEF_MODEL` | `clef-flash` | HTTP client / container | `clef-flash` or `clef`. |
| `CLEF_REVISION` | pinned for `clef-flash` | container | Hugging Face commit to load the model code and weights from. |
| `CLEF_DEVICE` | `auto` | container | `auto` / `cpu` / `cuda`; `-Gpu` sets CUDA. |
| `CLEF_HOST_PORT` | `8012` | compose / scripts | Loopback host port for the local server. |
| `CLEF_MAX_QUEUED` | `4` | container | Requests allowed to wait before the server answers `503`. |
