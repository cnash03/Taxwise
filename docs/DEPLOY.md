# Deploying TaxWise (Phase 8)

The demo runs on three hosted pieces: the vector store on **Qdrant Cloud**, `TaxWise.Api` on **Azure App Service** (Linux, .NET 10), and the desktop App as a local Release build pointed at Azure. API keys live only in the App Service settings, never in the App or the repo.

A person creates every cloud resource (BUILD_PLAN §12 item 6). The commands below are for you to run; the agent doesn't run them.

**What's deployed now** (set up 2026-11-18):

| | |
|---|---|
| API URL | `https://taxwise-api-cnash.azurewebsites.net` |
| Resource group | `taxwise-rg` (Central US) |
| App Service plan | `taxwise-plan`, Linux B1, **West US 3** (Central US had no App Service quota on the student subscription) |
| Web app | `taxwise-api-cnash` |
| Vector store | Qdrant Cloud free cluster, AWS us-west-1 |

Already set up? Skip to §4 to run your App against it, §5 to ship API changes, or §6 to get access.

## 1. Qdrant Cloud

1. At [cloud.qdrant.io](https://cloud.qdrant.io), create a **free** cluster (1 GB is plenty; the TaxWise collection is a few thousand 1024-dim points).
2. Copy the cluster host (e.g. `abc123.us-east4-0.gcp.cloud.qdrant.io`, no `https://` and no port) and create an API key for it.
3. Ingest into it from your machine. The PDFs must be downloaded already (`--download` does it). Your Voyage key comes from user secrets as usual:
   ```bash
   dotnet run --project src/TaxWise.Ingestion -- --download --spot-check \
     --qdrant-host <cluster-host> --qdrant-api-key <qdrant-key>
   ```
   `--qdrant-api-key` turns on HTTPS. Ingestion uses the gRPC port, 6334, which Qdrant Cloud serves over TLS. The spot-check searches at the end should pass, just as they do locally.

## 2. Azure App Service

Use the Azure CLI (`brew install azure-cli`, then `az login` with your Azure for Students account). Pick a globally unique app name; it becomes `https://<app-name>.azurewebsites.net`.

```bash
RG=taxwise-rg
APP=<app-name>              # e.g. taxwise-api-cnash
LOCATION=centralus

az group create -n $RG -l $LOCATION
az appservice plan create -g $RG -n taxwise-plan --is-linux --sku B1   # F1 (free) also works but sleeps when idle and cold-starts slowly
az webapp create -g $RG -p taxwise-plan -n $APP --runtime "DOTNETCORE:10.0"
az webapp update -g $RG -n $APP --https-only true
```

**Secrets and settings.** Use `__` for nested keys. Don't paste keys into a file that's committed.

```bash
az webapp config appsettings set -g $RG -n $APP --settings \
  ANTHROPIC_API_KEY=<anthropic-key> \
  Voyage__ApiKey=<voyage-key> \
  Qdrant__Host=<cluster-host> \
  Qdrant__ApiKey=<qdrant-key> \
  ASPNETCORE_ENVIRONMENT=Production \
  ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
```

- `appsettings.Production.json` (committed, no secrets) turns on HTTPS for Qdrant and points `Sources:Directory` and `Forms:Directory` at the `data/` folder that ships with the publish output.
- `ASPNETCORE_FORWARDEDHEADERS_ENABLED` makes the API build `https://` PDF links behind Azure's TLS proxy.

**Publish.** This uses a zip of our own publish output, so the `data/` files go with it:

```bash
dotnet publish src/TaxWise.Api -c Release -o publish/api
(cd publish/api && zip -qr ../api.zip .)
az webapp deploy -g $RG -n $APP --src-path publish/api.zip --type zip
az webapp config set -g $RG -n $APP --generic-configurations '{"healthCheckPath":"/health"}'
```

Rider works too: right-click `TaxWise.Api` → **Publish** → **Azure**, then pick the web app. Set the app settings above either way.

**Source PDFs.** The deploy doesn't include the PDFs. `GET /api/sources/{docId}/pdf` serves the local file when there is one and otherwise redirects (302) to the document's irs.gov URL from the manifest. The App's `#page=N` link still lands on the right page.

**Check it** from any machine:

```bash
curl https://$APP.azurewebsites.net/health
curl https://$APP.azurewebsites.net/api/knowledge-base
curl -s https://$APP.azurewebsites.net/api/ask -H 'content-type: application/json' \
  -d '{"question":"Is my scholarship taxable if it only paid tuition?","plainLanguage":false,"taxYear":2025}'
```

Logs: `az webapp log tail -g $RG -n $APP`.

**Cost.** B1 bills against your Azure for Students credit while the plan exists. After the final report, delete everything with `az group delete -n $RG`. The API has no sign-in, so anyone with the URL can spend Anthropic and Voyage credit, and the URL is readable inside every copy of the App. `appsettings.Production.json` caps each IP address at 60 Claude-backed requests an hour (`UsageLimit`, DECISIONS #20). Change it without redeploying with `az webapp config appsettings set -g $RG -n $APP --settings UsageLimit__PermitLimit=<n>`. Students on the same campus network share one IP, so raise it before a class demo. Also set a monthly spend limit in the Anthropic console.

## 3. Desktop App (Release)

Point Release builds at Azure by creating `src/TaxWise.App/appsettings.Release.json`. It holds no secret, so commit it:

```json
{ "ApiBaseUrl": "https://<app-name>.azurewebsites.net" }
```

Release builds embed it, and its `ApiBaseUrl` overrides the localhost default. Debug builds ignore it.

**macOS (Mac Catalyst):** one command builds a universal app (Apple Silicon and Intel) and wraps it in a `.dmg`:

```bash
scripts/package-mac.sh https://<app-name>.azurewebsites.net
```

It writes `appsettings.Release.json`, checks that `/health` answers, and leaves `publish/mac/TaxWise.dmg`. Share the `.dmg`. The user opens it and drags TaxWise into Applications.

The app isn't signed with an Apple Developer ID, so macOS blocks it the first time. The user opens it once, clicks **Done**, then goes to **System Settings → Privacy & Security** and clicks **Open Anyway** (on macOS 14 and older, right-click → **Open** also works). Removing that step takes an Apple Developer account ($99/year), a `Developer ID Application` certificate and `xcrun notarytool`.

The first press of the mic button on the Ask page asks for **Microphone** and **Speech Recognition** access. The bundle must carry the `com.apple.security.device.audio-input` entitlement for that to work (DECISIONS #21); check with `codesign -d --entitlements - src/TaxWise.App/bin/Release/net10.0-maccatalyst/TaxWise.app`.

**Windows** (build on a Windows machine with the MAUI workload installed):

```powershell
dotnet publish src/TaxWise.App -f net10.0-windows10.0.19041.0 -c Release -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true
```

The output folder under `bin\Release\net10.0-windows10.0.19041.0\win10-x64\publish\` runs as-is: start `TaxWise.App.exe`. Zip the folder to share it.

The green dot next to the TaxWise logo means the App reached the deployed API.

## 4. Run your local App against the deployed API

You don't need Docker, Qdrant, the API or any keys for this. Only the App runs on your machine.

Create `src/TaxWise.App/appsettings.local.json`. It's gitignored, so it only changes your own builds:

```json
{ "ApiBaseUrl": "https://taxwise-api-cnash.azurewebsites.net" }
```

Then run the App as usual (Debug, with hot reload and the web inspector):

```
# macOS (the flag skips the exact-Xcode check, which fails after an Xcode update)
dotnet build src/TaxWise.App -t:Run -f net10.0-maccatalyst -p:ValidateXcodeVersion=false
# Windows
dotnet build src/TaxWise.App -t:Run -f net10.0-windows10.0.19041.0
```

The dot next to the logo should turn green. To go back to your local API, delete the file and rebuild. The file is embedded at build time, so changing it always needs a rebuild.

Things to know:
- Every question you ask spends the shared Anthropic credit and counts toward the per-IP limit (60 an hour). If you get "You've reached the TaxWise question limit", wait or ask whoever owns the Azure subscription to raise it (§2, **Cost**).
- The deployed API runs whatever was last published, not your branch. App-only changes (pages, CSS, client code) can be tested this way. If your change also touches the API or `TaxWise.Shared` request/response shapes, run the API locally instead (SETUP.md §4).
- The first request after the API has been idle can take 30–60 seconds.

## 5. Ship API changes to Azure

Publish from an up-to-date `main` after the PR is merged, so Azure matches `main`. You need Azure access (§6) and the Azure CLI (`brew install azure-cli` or `winget install Microsoft.AzureCLI`, then `az login`).

```bash
RG=taxwise-rg
APP=taxwise-api-cnash

git switch main && git pull
dotnet test TaxWise.CI.slnf
dotnet publish src/TaxWise.Api -c Release -o publish/api
(cd publish/api && rm -f ../api.zip && zip -qr ../api.zip .)
az webapp deploy -g $RG -n $APP --src-path publish/api.zip --type zip

curl https://$APP.azurewebsites.net/health
curl -s https://$APP.azurewebsites.net/api/ask -H 'content-type: application/json' \
  -d '{"question":"Is my scholarship taxable if it only paid tuition?","plainLanguage":false,"taxYear":2025}'
```

On Windows PowerShell, replace the `zip` line with `Compress-Archive -Path publish/api/* -DestinationPath publish/api.zip -Force` and use `curl.exe`.

- **Keys and settings** stay in Azure across deploys. Never put them in the zip. To change one: `az webapp config appsettings set -g $RG -n $APP --settings Name=value` (the app restarts by itself). To see which are set without showing values: `az webapp config appsettings list -g $RG -n $APP --query "[].name" -o tsv`.
- **Logs:** `az webapp log tail -g $RG -n $APP`. A 502 from `/api/ask` usually means a key is missing or wrong, and the log line says which.
- **Roll back** by checking out the last good commit and publishing again.
- **Re-ingest** (new IRS documents or chunking changes) by running the Ingestion command in §1 against Qdrant Cloud. You need the Qdrant host and key from the subscription owner. The API doesn't need a redeploy for that.
- **App changes** don't need an API deploy. Rebuild the `.dmg` with `scripts/package-mac.sh` (§3) and share the new one.

## 6. Give a teammate access

The subscription owner runs this once per teammate. It grants Contributor on the TaxWise resource group only:

```bash
az role assignment create --assignee <teammate-email> --role Contributor \
  --scope $(az group show -n taxwise-rg --query id -o tsv)
```

If Azure can't find the email (a different school account, for example), invite them first in the portal: **Microsoft Entra ID → Users → Invite external user**. Run the command again once they accept. The teammate then runs `az login` and `az account set --subscription "Azure for Students"` (or the name `az account list -o table` shows).

Running the App against the API (§4) needs no access at all.

