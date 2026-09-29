# Running the demo on Laya

[Laya](https://github.com/NandhaKishorM/laya) is an open-source, non-autoregressive System 1 decision engine.
It answers the same three typed primitives this demo is built on - `noul`, `choice`, and `score` - in a single forward pass, and its HTTP server `laya-serve` deliberately speaks the TypeSafe Jev `POST /v1/systemone` wire protocol.

That compatibility is why this repository supports both engines with one client and one set of DTOs.
`src/Jev.Core/SystemOneHttpClient.cs` is provider-agnostic; everything that differs between the two lives in `SystemOneEndpoint` (`src/Jev.Core/DecisionProviders.cs`).

## Choosing a provider

| | Jev | Laya |
|---|---|---|
| Endpoint | `https://api.typesafe.ai/v1/systemone` | `http://127.0.0.1:8010/v1/systemone` by default |
| Credentials | `TYPESAFE_API_KEY`, always required | `LAYA_API_KEY`, only if the server was started with one |
| `model` field | `jev-latest` | omitted, so Laya's router picks a checkpoint per request |
| Where state goes | a third-party service | a container on your machine |
| `confidence` means | `(n*p_max - 1)/(n - 1)` | normalized entropy `1 - H(p)/log(k)` |

Select one on the command line:

```powershell
dotnet run --project src/TenLevels.Jev -- --all --provider laya --mode mock
```

or through the environment, which is what the Copilot hook reads:

```powershell
$env:DECISION_PROVIDER = "laya"
$env:DECISION_MODE = "live"
```

With `DECISION_PROVIDER` unset, a configured `LAYA_BASE_URL` selects Laya and anything else selects Jev, so an existing Jev setup keeps working untouched.

## Start the local service

The compose project lives outside this repository, because Laya is an upstream checkout rather than a vendored dependency.
The layout these scripts expect is the one in `C:\laya-local`:

```text
C:\laya-local\
├── docker-compose.yml       # laya-serve, sandboxed, published on 127.0.0.1:8010
├── compose.offline.yml      # override that cuts the container off from the network
└── laya\                    # the cloned github.com/NandhaKishorM/laya checkout
```

Point `LAYA_HOME` elsewhere if yours differs.

```powershell
./scripts/laya-up.ps1
```

```bash
./scripts/laya-up.sh
```

The script runs `docker compose up` and then waits for `/health`, because the first boot downloads a checkpoint into a named volume and can take several minutes.
It prints the loaded checkpoints and the base URL when the service is ready.
If the container is already running, the script leaves it alone and only waits for `/health`, so running it twice does not throw away the loaded checkpoints.
Run `laya-down` first when you want a changed checkout rebuilt.

To start the container and the presentation UI in one step, with Live enabled for Laya:

```powershell
./scripts/webui.ps1 -Laya
```

```bash
./scripts/webui.sh --laya
```

Stop it with `./scripts/laya-down.ps1` (add `-RemoveVolumes`, or `--volumes` for the shell version, only if you want the next start to download the weights again).

The container publishes on host port **8010** rather than 8000 because `http.sys` reserves 8000 on Windows.
`netsh int ipv4 show excludedportrange protocol=tcp` lists what is taken on a given machine.

## Verify it end to end

```bash
curl -s http://127.0.0.1:8010/health
```

```powershell
dotnet run --project src/TenLevels.Jev -- --level 4 --provider laya --mode live
```

Level 04 asks a Noul, a Choice, and a Score question in one request, so it exercises all three answer shapes against the real service.
The header line names the provider, the base URL, and whether a bearer token is in use.

For the Copilot hook, choose the mode **before** starting Copilot CLI, since repository hook configuration is read when the CLI starts:

```powershell
$env:DECISION_PROVIDER = "laya"
$env:DECISION_MODE = "live"
$env:LAYA_BASE_URL = "http://127.0.0.1:8010"
copilot
```

## The one difference that is not cosmetic: `confidence`

Both providers return a `confidence` number on `choice` and `score` answers, and they do not mean the same thing.
Jev reports `(n*p_max - 1)/(n - 1)`; Laya reports the normalized entropy `1 - H(p)/log(k)`.
For a three-way `allow`/`ask`/`deny` answer with probabilities `0.94 / 0.03 / 0.03`, Jev reports `0.9100` and Laya reports `0.7555`; for `0.76 / 0.12 / 0.12`, Jev reports `0.6400` and Laya reports `0.3471`.
A threshold carried over from one provider silently gates differently on the other.

So `CopilotToolGate` never reads that field.
It thresholds on `AnswerConfidence`, the probability mass on the reported answer (`max(p)`), which is provider-independent by construction:
Laya sends it as `answer_confidence` (the quantity its temperature scaling is fitted on), and for Jev, which does not send it, the client recovers the same number from the returned distribution.

Both numbers are kept and both are printed by the levels, because the provider's own value is still the right thing to show a person.
`tests/Jev.Core.SelfTests` pins the distinction from three directions: a Laya answer whose entropy `confidence` is low but whose `max(p)` is decisive must be allowed, the offline mock reproduces each provider's own formula, and every gate decision must come out identical whichever provider is selected.

## Other differences worth knowing

**Model routing.** Laya has three checkpoints (`english`, `multilingual`, `typed-decisions`) and a router that picks one per request, reporting its choice in a `routing` block that Jev has no equivalent for.
The client omits `model` by default so the router decides; set `LAYA_MODEL` to pin a checkpoint.
Laya ignores an unknown `model` value rather than failing, so a Jev client that always sends `jev-latest` also works.

**Cold start.** The first request after the container starts loads a checkpoint, which is far longer than any hook budget.
`laya-up` waits for `/health` before returning for exactly this reason.
If a decision does arrive late anyway, the hook's own deadline turns it into an explicit `ask`, never a silent allow - see [SECURITY.md](SECURITY.md).

**Request limits.** `laya-serve` refuses a state over 50,000 characters, more than 64 questions, or a body over 2 MiB with a `413`, and returns `503` when `LAYA_MAX_CONCURRENT` requests are already in flight.
The client surfaces the status and body rather than inventing an answer, and the gate turns any such failure into `ask`.

**Data residency.** This is the practical reason to reach for Laya in this demo: the tool call being classified never leaves the machine.
The credential check in `RiskHeuristics.LooksSensitive` still runs first and still short-circuits, because "local" is not the same as "safe to log".

**Offline.** Once the model cache volume is warm, `compose.offline.yml` cuts the container off from the network entirely:

```powershell
docker compose --file C:/laya-local/docker-compose.yml --file C:/laya-local/compose.offline.yml up -d
```

Note that the override removes the network, so the HTTP API is unreachable under `up`; it suits the one-shot SDK path described in that file's header.

## Accuracy

The shipped Laya checkpoints are zero-shot.
Upstream reports 0.362 accuracy for the base English checkpoint on its typed-decisions benchmark against 0.766 for the fine-tuned `laya-typed-decisions` checkpoint, so for a real policy gate, fine-tuning on your own decisions matters more than the choice of engine.
Set `LAYA_MODEL=typed-decisions` to use the fine-tuned checkpoint that ships today, and treat any threshold in this repository as a starting point to re-validate on your own traffic rather than a calibrated default.
