# az-vm-start-stop

Tag-driven **auto-start and auto-stop** for Azure Virtual Machines.

Azure VMs ship with a native *auto-shutdown* feature but no built-in *auto-start*,
and the native shutdown is a single fixed daily time. This project provides both,
driven by cron tags: a small, self-owned Azure Function (C# / .NET 8 isolated worker)
runs on a timer, finds VMs tagged with a start and/or stop schedule, and starts or
deallocates the ones that are due.

It is intentionally leaner than Microsoft's [Start/Stop VMs v2](https://learn.microsoft.com/azure/azure-functions/start-stop-v2/overview)
(which is in maintenance-only mode): no Logic Apps, no queues, no classic VM support —
just a single timer-triggered function using a managed identity.

## How it works

1. A timer trigger fires on a schedule (default: every 5 minutes).
2. The function enumerates VMs in scope and reads their tags.
3. For each VM tagged with `AutoStart` and/or `AutoStop`, it evaluates the cron
   expression in the VM's time zone against the elapsed time window.
4. VMs due to **start** that are currently stopped/deallocated are started; VMs due to
   **stop** that are currently running are **deallocated** (stops billing, like the
   native auto-shutdown). Actions use the function's managed identity
   (`Virtual Machine Contributor`).

## Tag schema

| Tag | Required | Description | Example |
| --- | --- | --- | --- |
| `AutoStart` | No\* | 5-field cron (`minute hour day-of-month month day-of-week`), evaluated in the VM's time zone. Presence enables auto-start. | `0 7 * * 1-5` |
| `AutoStop` | No\* | 5-field cron. Presence enables auto-stop (deallocate). | `0 19 * * 1-5` |
| `AutoStartTimeZone` | No | IANA or Windows time zone id for `AutoStart`. Defaults to **`Europe/Amsterdam`** (configurable). | `Europe/Amsterdam` / `W. Europe Standard Time` |
| `AutoStopTimeZone` | No | Time zone id for `AutoStop`. Defaults to **`Europe/Amsterdam`**. | `UTC` |

\* At least one of `AutoStart` / `AutoStop` must be present for a VM to participate.

Examples:

- Start 07:00 and stop 19:00 on weekdays, Amsterdam time (default TZ, so no TZ tags):
  `AutoStart = 0 7 * * 1-5`, `AutoStop = 0 19 * * 1-5`
- Start at 06:30 every day, UTC:
  `AutoStart = 30 6 * * *`, `AutoStartTimeZone = UTC`

> The cron expression is the **timer inside each VM's tag** and uses standard 5-field
> cron. The Function App's own polling cadence (`ScheduleExpression`) is a separate
> 6-field NCRONTAB expression.

## Configuration

Set via app settings (Bicep parameters below wire these up automatically):

| Setting | Default | Description |
| --- | --- | --- |
| `ScheduleExpression` | `0 */5 * * * *` | Timer cadence (6-field NCRONTAB). |
| `AutoSchedule:DefaultTimeZone` | `Europe/Amsterdam` | TZ used when a VM has no per-action time zone tag. |
| `AutoSchedule:ScheduleWindowMinutes` | `5` | First-run look-back window; keep aligned with the timer cadence. |
| `AutoSchedule:DryRun` | `false` | When `true`, logs what would start/stop without acting. |
| `AutoSchedule:SubscriptionIds` | *(empty)* | Optional subscription ids to scan. Empty = the identity's default subscription. |

## Project layout

```
src/AzVmStartStop.Functions/         # Function app (timer trigger, services)
src/AzVmStartStop.Functions.Tests/   # xUnit tests (cron/time zone + power-state logic)
infra/main.bicep                     # RG-scoped: identity, storage, plan, App Insights, function
infra/abbreviations.json             # Azure CAF resource-type abbreviations (azd standard)
infra/main.sample.bicepparam         # Example parameters (placeholders only)
.github/workflows/deploy.yml         # Build/test + OIDC deploy
```

## Build & test locally

```bash
dotnet build -c Release
dotnet test -c Release
```

Run the function locally with the [Azure Functions Core Tools](https://learn.microsoft.com/azure/azure-functions/functions-run-local):

```bash
cd src/AzVmStartStop.Functions
func start
```

Local runs use your developer/Azure CLI credentials (`az login`) via
`DefaultAzureCredential`; in Azure the function uses its user-assigned managed identity.

## Deploy

### Security model

The deployment is **resource-group scoped** so the CI identity needs only **Owner on a
single, pre-created resource group** — no subscription- or management-group-level
rights.

- **No secrets in this repo.** Deployment authenticates with GitHub OIDC federated
  credentials; the running function uses a **user-assigned managed identity** (created
  by the deployment, inside the RG).
- Storage uses **identity-based** connections (`allowSharedKeyAccess: false`) — no
  access keys. The identity is granted **`Storage Blob Data Owner`** on its storage
  account (an in-RG assignment, so RG Owner can create it).
- The broad **`Virtual Machine Contributor`** grant — which lets the function start/stop
  VMs across other resource groups and subscriptions — is **not** created by the
  deployment. Because it targets a scope *above* the RG (management group or
  subscription), it is a one-time assignment performed by an administrator (see
  step 3 below). This keeps the CI identity limited to Owner on one RG.

### One-time setup: Entra app + GitHub OIDC

The deploy workflow signs in to Azure with **workload identity federation** (OIDC) —
no client secret is ever created or stored. You create a Microsoft Entra app
registration, add a **federated credential** that trusts this repository's GitHub
Actions, grant it **Owner on the pre-created resource group**, and record its ids as
GitHub repository variables. Separately, an administrator grants the function's
identity `Virtual Machine Contributor` at management-group scope (step 3).

> The workflow's `deploy` job runs in the GitHub **`production`** environment, so the
> federated credential's *subject* must use the `environment:production` form shown
> below (not a branch subject). Create the environment first under **Settings →
> Environments → New environment → `production`**.

#### Option A — Azure CLI (recommended)

Run these once, replacing the placeholders. Steps 1–2 need rights to create app
registrations; steps in this block that assign roles need Owner/UAA at the relevant
scope (an admin task).

```bash
# --- variables ---
APP_NAME="az-vm-start-stop-deploy"
REPO="DevSecNinja/az-vm-start-stop"          # owner/repo
ENVIRONMENT="production"                       # matches the workflow's environment
SUBSCRIPTION_ID="<your-subscription-id>"
RESOURCE_GROUP="<pre-created-rg-name>"

# 0) Pre-create the resource group that will hold the function resources
az group create --name "$RESOURCE_GROUP" --location westeurope --subscription "$SUBSCRIPTION_ID"

# 1) Create the app registration and its service principal (the CI/deploy identity)
APP_ID=$(az ad app create --display-name "$APP_NAME" --query appId -o tsv)
az ad sp create --id "$APP_ID"

# 2) Add the federated credential trusting this repo's `production` environment
az ad app federated-credential create --id "$APP_ID" --parameters '{
  "name": "github-env-production",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:'"$REPO"':environment:'"$ENVIRONMENT"'",
  "audiences": ["api://AzureADTokenExchange"]
}'

# 3) Grant the deploy identity Owner on ONLY the resource group. RG Owner can create
#    the resources plus the in-RG storage role assignment — nothing above the RG.
SP_OBJECT_ID=$(az ad sp show --id "$APP_ID" --query id -o tsv)
az role assignment create \
  --assignee-object-id "$SP_OBJECT_ID" \
  --assignee-principal-type ServicePrincipal \
  --role "Owner" \
  --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP"

# 4) Print the ids you need for the GitHub variables below
echo "AZURE_CLIENT_ID=$APP_ID"
echo "AZURE_TENANT_ID=$(az account show --query tenantId -o tsv)"
echo "AZURE_SUBSCRIPTION_ID=$SUBSCRIPTION_ID"
echo "AZURE_RESOURCE_GROUP=$RESOURCE_GROUP"
```

After the **first successful deploy**, grant the function's user-assigned identity
permission to start/stop VMs across your estate. The deployment prints the identity's
`identityPrincipalId` as an output; assign it `Virtual Machine Contributor` at the
management group (or subscription) scope that covers the target VMs — a one-time admin
action:

```bash
MG_ID="<your-management-group-id>"                 # or a subscription id
IDENTITY_PRINCIPAL_ID="<identityPrincipalId output from the deployment>"
az role assignment create \
  --assignee-object-id "$IDENTITY_PRINCIPAL_ID" \
  --assignee-principal-type ServicePrincipal \
  --role "Virtual Machine Contributor" \
  --scope "/providers/Microsoft.Management/managementGroups/$MG_ID"
```

> To scan more than the RG's own subscription, list them in
> `AutoSchedule:SubscriptionIds` (Bicep `subscriptionIds` parameter); the identity's
> management-group assignment must cover those subscriptions.

#### Option B — Azure portal

1. **Microsoft Entra ID → App registrations → New registration** → name it, register.
   Note the **Application (client) ID** and **Directory (tenant) ID**.
2. In the app, **Certificates & secrets → Federated credentials → Add credential**.
   Choose scenario **GitHub Actions deploying Azure resources** and enter:
   - Organization: `DevSecNinja`, Repository: `az-vm-start-stop`
   - Entity type: **Environment**, value: `production`

   This produces the subject `repo:DevSecNinja/az-vm-start-stop:environment:production`
   with issuer `https://token.actions.githubusercontent.com` and audience
   `api://AzureADTokenExchange`.
3. Pre-create the resource group, then **RG → Access control (IAM) → Add role
   assignment** → assign **Owner** to the app's service principal (RG scope only).
4. After the first deploy, at the **management group** (or subscription) → **Access
   control (IAM)** → assign **Virtual Machine Contributor** to the function's
   user-assigned identity.

#### GitHub repository variables

Under **Settings → Secrets and variables → Actions → Variables**, set these
**repository variables** (they are ids, not secrets):

| Variable | Description |
| --- | --- |
| `AZURE_CLIENT_ID` | Application (client) id of the Entra app (the deploy identity). |
| `AZURE_TENANT_ID` | Entra directory (tenant) id. |
| `AZURE_SUBSCRIPTION_ID` | Subscription containing the pre-created resource group. |
| `AZURE_RESOURCE_GROUP` | Pre-created resource group name for the function resources. |
| `AZURE_NAME_PREFIX` | 3–11 lowercase alphanumeric prefix for resource names. |

Push to `main` (or run the workflow manually) to build, test, and deploy.

### Manual deploy (Azure CLI)

```bash
# Resource group must already exist.
az deployment group create \
  --resource-group <pre-created-rg-name> \
  --template-file infra/main.bicep \
  --parameters infra/main.sample.bicepparam

func azure functionapp publish <functionAppName>
```

## Notes

- DST is handled by evaluating the cron in the VM's time zone, so `0 7 * * 1-5`
  means 07:00 local year-round.
- Actions are power-state aware: an already-running VM is not started again and an
  already-stopped VM is not deallocated, so overlapping windows are safe.
- `AutoStop` **deallocates** the VM (stops compute billing), matching Azure's native
  auto-shutdown behaviour.
- If a VM has both `AutoStart` and `AutoStop` due in the same window, no action is
  taken (logged as a warning) — keep start and stop times apart.
