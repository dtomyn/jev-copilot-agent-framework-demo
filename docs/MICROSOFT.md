# Running the demo on Microsoft Decision

`Microsoft-Decision-1` is a decision model in Microsoft Foundry.
It answers the same three typed questions this demo is built on - `noul`, `choice`, and `score` - and returns probabilities instead of prose.
Its API is `POST {resource}/providers/microsoft/v1/systemone`, the same System-One shape as the other four providers, so it uses the same client and DTOs.
Everything Microsoft-specific lives in `SystemOneEndpoint` (`src/Jev.Core/DecisionProviders.cs`); policy code does not know it exists.

Source: [Deploy and use Microsoft-Decision-1 in Microsoft Foundry](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/how-to/use-foundry-models-microsoft-decision).

## Deploy

1. In the [Foundry portal](https://ai.azure.com), open your project, then **Model catalog**, and deploy **Microsoft-Decision-1**.
2. Copy the resource endpoint, the deployment name, and an API key from the deployment details page.

Or with the Azure CLI:

```powershell
az cognitiveservices account deployment create `
  --name <ACCOUNT_NAME> --resource-group <RESOURCE_GROUP> `
  --deployment-name <DEPLOYMENT_NAME> `
  --model-name "Microsoft-Decision-1" --model-format Microsoft --model-version "1" `
  --sku-name GlobalStandard --sku-capacity 1
```

Use `DataZoneStandard` instead of `GlobalStandard` to keep inference inside one data zone where it is available.

## Run

```powershell
$env:MICROSOFT_DECISION_ENDPOINT = "https://<resource>.services.ai.azure.com"
$env:MICROSOFT_DECISION_DEPLOYMENT = "<deployment name>"
$env:MICROSOFT_DECISION_API_KEY = "<key>"
dotnet run --project src/TenLevels.Jev -- --level 4 --provider microsoft --mode live
```

For Microsoft Entra ID (the recommended method), use a token instead of a key.
It is sent as a bearer token and wins when both are set.
Tokens expire, so refresh it when you see a `401`:

```powershell
$env:MICROSOFT_DECISION_ACCESS_TOKEN = az account get-access-token --resource https://cognitiveservices.azure.com --query accessToken --output tsv
```

For the Copilot hook, set `DECISION_PROVIDER=microsoft` and `DECISION_MODE=live` **before** starting Copilot CLI.

## Differences worth knowing

- **`model` is the deployment name**, not `Microsoft-Decision-1`. The response's `model` reports the underlying model.
- **Never auto-selected.** Like Cloudflare credentials for Clef, Microsoft variables alone never select this provider: choose it with `DECISION_PROVIDER=microsoft` or `--provider microsoft`. Every tool call state is sent to Azure once you do.
- **HTTPS only.** The client refuses a plain `http://` endpoint so a key is never sent in clear text.
- **Key auth uses the `api-key` header**, not `Authorization: Bearer`.
- **`confidence` is undocumented.** The client does not read it for policy: as for Jev and Clef it recovers `max(p)` from the returned probabilities, so `CopilotToolGate` decides identically to the other providers. The offline mock uses the System-One default formula for Microsoft, which is an assumption.
- **Tuning.** The docs advise validating thresholds against labeled examples; the gate's thresholds here (`allow >= 0.85`, `deny >= 0.55`) are the demo's, not tuned for this model.

## Variables

| Variable | Meaning |
|---|---|
| `MICROSOFT_DECISION_ENDPOINT` | Foundry resource endpoint. Must be `https://`. |
| `MICROSOFT_DECISION_DEPLOYMENT` | Deployment name, sent as the request `model`. |
| `MICROSOFT_DECISION_API_KEY` | Resource key, sent in the `api-key` header. |
| `MICROSOFT_DECISION_ACCESS_TOKEN` | Entra access token, sent as a bearer token. Wins over the key. |

Live mode needs the endpoint, the deployment, and one credential.
