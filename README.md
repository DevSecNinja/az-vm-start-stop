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

Set via app settings (Bicep parameters below wire these up automatically). Azure
app-setting names use `__` (double underscore), which .NET maps to the `:` config
hierarchy at runtime:

| Setting | Default | Description |
| --- | --- | --- |
| `ScheduleExpression` | `0 */5 * * * *` | Timer cadence (6-field NCRONTAB). |
| `AutoSchedule__DefaultTimeZone` | `Europe/Amsterdam` | TZ used when a VM has no per-action time zone tag. |
| `AutoSchedule__ScheduleWindowMinutes` | `5` | First-run look-back window; keep aligned with the timer cadence. |
| `AutoSchedule__DryRun` | `false` | When `true`, logs what would start/stop without acting. |
| `AutoSchedule__OperationTimeoutSeconds` | `45` | Max seconds a pass waits for a start/deallocate to complete before logging a warning and moving on. |
| `AutoSchedule__SubscriptionIds__0`, `__1`, … | *(empty)* | Optional subscription ids to scan. Empty = all subscriptions accessible to the identity. |

## Project layout

```
src/AzVmStartStop.Functions/         # Function app (timer trigger, services)
src/AzVmStartStop.Functions.Tests/   # xUnit tests (cron/time zone + power-state + schedule orchestration)
infra/main.bicep                     # RG-scoped: identity, storage, plan, App Insights, function
infra/abbreviations.json             # Azure CAF resource-type abbreviations (azd standard)
infra/main.sample.bicepparam         # Example parameters (placeholders only)
.github/workflows/deploy.yml         # Build/test + OIDC deploy
docs/                                # Architecture, design decisions, troubleshooting
```

## Documentation

- [`docs/architecture.md`](docs/architecture.md) — components, request flow, infra, security.
- [`docs/deployment.md`](docs/deployment.md) — one-time identity/OIDC setup, repository variables, manual deploy.
- [`docs/decisions.md`](docs/decisions.md) — key design decisions and their rationale.
- [`docs/troubleshooting.md`](docs/troubleshooting.md) — Application Insights queries, log reference, common problems.

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

## Use it in your own environment

This is a **self-hosted** service you run in your own Azure tenant — there is no
shared/central instance. To adopt it:

1. **Fork this repository** (a fork keeps a link to upstream, so you can pull in
   fixes and improvements later). *"Use this template" also works if you prefer a
   fully independent copy.*
2. **Set up the deploy identity + GitHub OIDC** and add the repository variables
   (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`,
   `AZURE_RESOURCE_GROUP`, `AZURE_NAME_PREFIX`). Full steps:
   [`docs/deployment.md`](docs/deployment.md).
3. **Deploy** by pushing to `main` (or running the *Deploy* workflow manually).
   The workflow builds, tests, and deploys the function into your resource group.
4. **Grant it permission to act on VMs**: assign the function's managed identity
   `Virtual Machine Contributor` at the management-group or subscription scope
   that covers your VMs (one-time admin step, in `docs/deployment.md`).
5. **Tag your VMs** with `AutoStart` / `AutoStop` cron schedules (see
   [Tag schema](#tag-schema)). Tip: set `AutoSchedule__DryRun=true` first to
   preview actions without touching any VM.
6. **Verify** in Application Insights with the queries in
   [`docs/troubleshooting.md`](docs/troubleshooting.md).

Tune behaviour via the [configuration settings](#configuration) — e.g.
`DefaultTimeZone`, `ScheduleExpression`, or `SubscriptionIds` to narrow the scope.
Issues and pull requests back to upstream are welcome.

## Deploy

Deployment is **resource-group scoped** and authenticates with **GitHub OIDC**
(no stored secrets); the running function uses a **user-assigned managed
identity**. Push to `main` — or run the workflow manually — to build, test, and
deploy.

See [`docs/deployment.md`](docs/deployment.md) for the full one-time setup (Entra
app + OIDC federation, repository variables, and the `Virtual Machine
Contributor` assignment) and the manual-deploy path.

## Notes

- DST is handled by evaluating the cron in the VM's time zone, so `0 7 * * 1-5`
  means 07:00 local year-round.
- Actions are power-state aware: an already-running VM is not started again and an
  already-stopped VM is not deallocated, so overlapping windows are safe.
- `AutoStop` **deallocates** the VM (stops compute billing), matching Azure's native
  auto-shutdown behaviour.
- If a VM has both `AutoStart` and `AutoStop` due in the same window, no action is
  taken (logged as a warning) — keep start and stop times apart.
