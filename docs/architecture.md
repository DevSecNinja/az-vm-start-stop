# Architecture

`az-vm-start-stop` is a small, self-owned alternative to Azure's Start/Stop VMs
v2. A single timer-triggered Azure Function periodically scans virtual machines
and starts or deallocates them based on cron expressions stored in resource
tags, evaluated in a per-VM (or default) time zone.

## Components

| Component | File | Responsibility |
| --- | --- | --- |
| `ScheduleFunction` | `ScheduleFunction.cs` | Timer trigger (`%ScheduleExpression%`). Resolves the evaluation window from the timer's previous run and delegates to the schedule service. |
| `IVmScheduleService` / `VmScheduleService` | `Services/VmScheduleService.cs` | Orchestrates a pass: enumerates subscriptions/VMs, evaluates tags, decides start/stop/skip, performs the action, and produces a `ScheduleRunSummary`. Owns all logging, counting and error handling. |
| `IVmInventory` / `ArmVmInventory` | `Services/IVmInventory.cs`, `Services/ArmVmInventory.cs` | Abstraction over the Azure Resource Manager SDK. Yields subscriptions and their VMs; the ARM implementation wraps `ArmClient`, `SubscriptionResource` and `VirtualMachineResource`. Keeps `VmScheduleService` unit-testable. |
| `ICronScheduleEvaluator` / `CronScheduleEvaluator` | `Services/CronScheduleEvaluator.cs` | Decides whether a 5-field cron expression has an occurrence in the UTC window, evaluated in a resolved time zone. Uses `NCrontab`. |
| `VmPowerState` | `Services/VmPowerState.cs` | Interprets instance-view power-state codes and decides whether a start/stop is applicable. |
| `AutoScheduleOptions` | `Options/AutoScheduleOptions.cs` | Bound configuration (default time zone, window, subscriptions, dry-run, operation timeout). |
| `TagNames` | `TagNames.cs` | Tag names that drive behaviour (`AutoStart`, `AutoStop`, `AutoStartTimeZone`, `AutoStopTimeZone`). |

## Request flow

1. The timer fires on the `ScheduleExpression` cadence (default every 5 minutes).
2. `ScheduleFunction` computes the window `(previousRun, now]` (or a look-back
   window on first run) and calls `VmScheduleService.RunAsync`.
3. `VmScheduleService` enumerates subscriptions via `IVmInventory`. For each VM
   it reads tags, asks `CronScheduleEvaluator` whether `AutoStart`/`AutoStop`
   are due in the window, checks the current power state, and (unless
   `DryRun`) starts or deallocates the VM.
4. Start/deallocate operations wait for completion, bounded by
   `OperationTimeoutSeconds`. A confirmed action logs both a "…fired" and a
   "…has started/stopped" line.
5. The pass logs a summary: `Scanned / Started / Stopped / Skipped / Failed`.

## Tag contract

- `AutoStart` / `AutoStop`: 5-field cron (`minute hour day-of-month month
  day-of-week`), e.g. `0 9 * * *`. Presence of the tag enables the behaviour.
- `AutoStartTimeZone` / `AutoStopTimeZone` (optional): IANA (e.g.
  `Europe/Amsterdam`) or Windows (e.g. `W. Europe Standard Time`) id. Falls back
  to `AutoSchedule:DefaultTimeZone`, and finally UTC.

## Infrastructure (Bicep, `infra/main.bicep`)

- **Function App**: Linux **Flex Consumption**, `dotnet-isolated` 8.0.
- **Identity**: a **user-assigned managed identity**; the app has no secrets.
- **Storage**: identity-based (`Storage Blob Data Owner` within the RG) for
  `AzureWebJobsStorage` and the deployment container.
- **Application Insights**: telemetry sink for the worker.
- **Scope**: the deploy provisions a single, pre-created resource group. The
  broad `Virtual Machine Contributor` grant that lets the function act on VMs
  across subscriptions is a **separate, one-time assignment** made by an admin
  at management-group (or subscription) scope.

## Security model

- No stored credentials. GitHub Actions authenticates to Azure with **OIDC
  federation**; the running function uses its **user-assigned managed identity**.
- Least privilege: the function only needs `Virtual Machine Contributor` over
  the target scope, plus the ability to list the subscriptions it should scan.

## Deployment

`.github/workflows/deploy.yml`: build + test, then (on `main`) OIDC login,
Bicep deploy and function package deploy. Azure steps are run via the **Azure
CLI** rather than `azure/*` actions (see `docs/decisions.md`).

See also: [`README.md`](../README.md) for configuration and setup,
[`docs/troubleshooting.md`](troubleshooting.md) for diagnostics, and
[`docs/decisions.md`](decisions.md) for the rationale behind key choices.
