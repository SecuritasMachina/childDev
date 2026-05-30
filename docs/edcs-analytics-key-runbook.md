# Runbook: finish storing the web analytics key in EDCS

The ChildDev.Api **read path** is already implemented and deployed-ready (commit `c4f8e21`):
at startup it fetches EDCS key `analytics.bizeyes.apikey` (appId `childdev`) and uses it as
`BizEyes:ApiKey`. EDCS is a **soft dependency** — until the steps below are done the lookup 404s
and analytics forwarding simply stays disabled; the app runs normally.

Real EDCS endpoints (OAuth2 platform — see `EDCS/src/Admin.Web/wwwroot/api-spec.md`):
- STS: `https://auth.securitasmachina.org` (`POST /connect/token`)
- AppConfig: `https://config.securitasmachina.org` (`/v1/app-config/{appId}/{key}`)

Admin creds (human, in `edcs-admins` → `edcs:admin`): `~/data/.secrets/EDCS.creds`.

---

## Step 1 — Rotate the burned key in AnalyticsHub (DO THIS FIRST)

The old web key (prefix `ah_VSr5…`) and mobile key (prefix `ah_4VJ7…`) are both in committed git history
(GitHub `SecuritasMachina/childDev`), so both are compromised. In AnalyticsHub `/apps` (app id 4,
"LevelUp"), rotate the web key. Keep the new value for Step 3. (Rotate the mobile key too; put its
new value in the gitignored `ChildDev.Mobile/Services/BizEyesConfig.Secret.cs`.)

## Step 2 — Provision a `childdev` client-credentials client in EDCS

EDCS clients are **seeded in code** (`EDCS/src/Identity.Sts/Program.cs`); there is no runtime
registration API. Add a client alongside the existing seeded ones (e.g. `edcs-mcp`), then redeploy
EDCS. Sketch (match the existing seed pattern in that file):

```csharp
// childdev web service identity — read-only AppConfig access
SeedClient(db, clientId: "childdev",
    clientSecret: "<generate: openssl rand -base64 32>",
    allowedScopes: "appconfig:read");
```

Redeploy EDCS (its own compose/stack on the same VPS). Store the chosen client secret in
`~/data/.secrets/childdev-edcs.env` (gitignored, 600) on **dev and prod**:

```
EDCS_STS_URL=https://auth.securitasmachina.org
EDCS_APPCONFIG_URL=https://config.securitasmachina.org
EDCS_CLIENT_ID=childdev
EDCS_CLIENT_SECRET=<the secret you seeded>
```

> Alternative (no redeploy): insert a row into the `client_credentials` table with the secret
> hashed exactly as `TokenService.HashToken` does. The seed-edit path is preferred (reproducible).

## Step 3 — Store the analytics key value in EDCS (admin token, one-time)

```bash
ADMINPASS='<jaxtrx EDCS password from ~/data/.secrets/EDCS.creds>'
TOKEN=$(curl -s -X POST https://auth.securitasmachina.org/connect/token \
  -d grant_type=password -d username=jaxtrx -d "password=$ADMINPASS" -d scope=openid \
  | jq -r .access_token)

# Store the NEW (rotated) value from Step 1:
curl -s -X PUT https://config.securitasmachina.org/v1/app-config/childdev/analytics.bizeyes.apikey \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"value":"<NEW_ROTATED_WEB_KEY>","label":"","contentType":"text/plain","isVaultRef":false,"isFeatureFlag":false}'
```

Verify (with the childdev client from Step 2):

```bash
T=$(curl -s -X POST https://auth.securitasmachina.org/connect/token \
  -d grant_type=client_credentials -d client_id=childdev \
  -d client_secret="<secret>" -d scope=appconfig:read | jq -r .access_token)
curl -s https://config.securitasmachina.org/v1/app-config/childdev/analytics.bizeyes.apikey \
  -H "Authorization: Bearer $T" | jq .value   # expect the new key
```

## Step 4 — Wire ChildDev prod env + restart

Append to prod `/opt/childdev/.env` (compose already passes `Edcs__*` through):

```
EDCS_STS_URL=https://auth.securitasmachina.org
EDCS_APPCONFIG_URL=https://config.securitasmachina.org
EDCS_CLIENT_ID=childdev
EDCS_CLIENT_SECRET=<secret>
```

Then `docker compose up -d --no-deps childdev-api`. On boot the app pulls the key from EDCS;
analytics forwarding turns on. If EDCS is ever down, the app still starts (analytics just off).

## Notes
- Never commit `childdev-edcs.env`, tokens, or key values. `~/data/.secrets/*` and `*.env` are gitignored.
- The leaked keys remain in git history (history was not rewritten by decision); rotation in Step 1
  is what actually neutralizes them.
