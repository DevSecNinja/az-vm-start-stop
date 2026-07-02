# az-vm-start

Tag-driven **auto-start** for Azure Virtual Machines.

Azure VMs ship with a native *auto-shutdown* feature but no built-in *auto-start*.
This project fills that gap with a small, self-owned Azure Function (C# / .NET 8
isolated worker) that runs on a timer, finds VMs tagged with a start schedule, and
starts the ones that are due.

It is intentionally leaner than Microsoft's [Start/Stop VMs v2](https://learn.microsoft.com/azure/azure-functions/start-stop-v2/overview)
(which is in maintenance-only mode): no Logic Apps, no queues, no classic VM support —
just a single timer-triggered function using a managed identity.

## How it works

1. A timer trigger fires on a schedule (default: every 5 minutes).
2. The function enumerates VMs in scope and reads their tags.
3. For each VM tagged with `AutoStart`, it evaluates the cron expression in the VM's
   time zone against the elapsed time window.
4. VMs that are due **and** currently stopped/deallocated are started via the
   function's managed identity (`Virtual Machine Contributor`).

Stopping is left to Azure's native auto-shutdown.

## Tag schema

| Tag | Required | Description | Example |
| --- | --- | --- | --- |
| `AutoStart` | Yes | 5-field cron (`minute hour day-of-month month day-of-week`), evaluated in the VM's time zone. Presence enables auto-start. | `0 7 * * 1-5` |
| `AutoStartTimeZone` | No | IANA or Windows time zone id. Defaults to **`Europe/Amsterdam`** (configurable). | `Europe/Amsterdam` / `W. Europe Standard Time` |

Examples:

- Start at 07:00 on weekdays, Amsterdam time (default TZ, so the TZ tag is optional):
  `AutoStart = 0 7 * * 1-5`
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
| `AutoStart:DefaultTimeZone` | `Europe/Amsterdam` | TZ used when a VM has no `AutoStartTimeZone` tag. |
| `AutoStart:ScheduleWindowMinutes` | `5` | First-run look-back window; keep aligned with the timer cadence. |
| `AutoStart:DryRun` | `false` | When `true`, logs what would start without starting anything. |
| `AutoStart:SubscriptionIds` | *(empty)* | Optional subscription ids to scan. Empty = the identity's default subscription. |

## Project layout

```
src/AzVmStart.Functions/         # Function app (timer trigger, services)
src/AzVmStart.Functions.Tests/   # xUnit tests (cron + time zone logic)
infra/main.bicep                 # Subscription-scope: RG + VM Contributor role
infra/functionApp.bicep          # Flex Consumption function app + storage + App Insights
infra/main.sample.bicepparam     # Example parameters (placeholders only)
.github/workflows/deploy.yml     # Build/test + OIDC deploy
```

## Build & test locally

```bash
dotnet build -c Release
dotnet test -c Release
```

Run the function locally with the [Azure Functions Core Tools](https://learn.microsoft.com/azure/azure-functions/functions-run-local):

```bash
cd src/AzVmStart.Functions
func start
```

Local runs use your developer/Azure CLI credentials (`az login`) via
`DefaultAzureCredential`; in Azure the function uses its system-assigned managed identity.

## Deploy

### Security model

- **No secrets in this repo.** Deployment authenticates with GitHub OIDC federated
  credentials; the running function uses a **system-assigned managed identity**.
- Storage uses **identity-based** connections (`allowSharedKeyAccess: false`) — no
  access keys.
- The identity is granted **`Virtual Machine Contributor`** (least privilege for
  start/stop) at subscription scope, and **`Storage Blob Data Owner`** on its own
  storage account.

### One-time GitHub configuration

Create a Microsoft Entra app (or user-assigned identity) with a **federated credential**
for this repository, grant it rights to deploy (e.g. `Owner`/`Contributor` +
`User Access Administrator` on the target subscription so it can create the role
assignments), then set these **repository variables** (not secrets):

| Variable | Description |
| --- | --- |
| `AZURE_CLIENT_ID` | Client (app) id of the federated identity. |
| `AZURE_TENANT_ID` | Entra tenant id. |
| `AZURE_SUBSCRIPTION_ID` | Target subscription id. |
| `AZURE_LOCATION` | Region, e.g. `westeurope`. |
| `AZURE_RESOURCE_GROUP` | Resource group name for the function resources. |
| `AZURE_NAME_PREFIX` | 3–11 lowercase alphanumeric prefix for resource names. |

Push to `main` (or run the workflow manually) to build, test, and deploy.

### Manual deploy (Azure CLI)

```bash
az deployment sub create \
  --location <region> \
  --template-file infra/main.bicep \
  --parameters infra/main.sample.bicepparam

func azure functionapp publish <functionAppName>
```

## Notes

- DST is handled by evaluating the cron in the VM's time zone, so `0 7 * * 1-5`
  means 07:00 local year-round.
- Starting an already-running VM is skipped (checked via power state), so overlapping
  windows are safe.
