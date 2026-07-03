# Troubleshooting & operations

All diagnostics go to **Application Insights** (the `traces` table). The
function logs every step of a pass at `Information`, with structured
properties so you can filter precisely.

## Structured properties (`customDimensions`)

| Property                                            | Where                | Use                                                                   |
| --------------------------------------------------- | -------------------- | --------------------------------------------------------------------- |
| `RunId`                                             | every line in a pass | Group all logs for a single invocation.                               |
| `BuildSha`                                          | every line in a pass | Short commit SHA of the deployed function version that wrote the log. |
| `VmName`, `ResourceGroup`, `SubscriptionId`, `VmId` | per-VM lines         | Filter to one VM.                                                     |
| `Action`                                            | during a start/stop  | `Start` or `Stop`.                                                    |
| `DryRun`, `WindowStartUtc`, `WindowEndUtc`          | pass scope           | Run context.                                                          |

## Key queries

**Everything a specific VM did** (the go-to query):

```kusto
traces
| where customDimensions.VmName == "<yourVMName>"
| project timestamp, message, RunId = customDimensions.RunId
| order by timestamp desc
```

**Per-pass summary lines** (did it start/stop anything?):

```kusto
traces
| where timestamp > ago(1d)
| where message has "Schedule pass complete"
| project timestamp, message, RunId = customDimensions.RunId
| order by timestamp desc
```

**Confirm logs are flowing / see one full pass:**

```kusto
traces
| where timestamp > ago(30m)
| where customDimensions.RunId == "<runId>"
| project timestamp, message, VmName = customDimensions.VmName
| order by timestamp asc
```

**Warnings and errors only** (permissions, invalid cron, timeouts):

```kusto
union traces, exceptions
| where timestamp > ago(1d)
| where severityLevel >= 2
| project timestamp, message, severityLevel, outerMessage
| order by timestamp desc
```

## Log message reference

- `No SubscriptionIds configured; scanning all subscriptions accessible…` /
  `Using N configured subscription(s): …` — which subscriptions are in scope.
- `Scanning subscription '<id>' for virtual machines.` — one per subscription.
- `Scanned VM '<name>'; tags=[…]; AutoStart='…' (tz='…'), AutoStop='…'` — the
  **actual tag keys and raw values** read from the VM.
- `Evaluated cron '<expr>' in time zone '<tz>' (UTC offset …): local window (…],
  next occurrence … => due=…` — the full schedule decision, including the
  resolved zone and offset.
- `VM '<name>' is due to <Start|Stop>; current power state is '<state>'.`
- `Starting VM '<name>'.` → `VM '<name>' has started.` (and the deallocate pair)
  — the action was fired, then confirmed complete.
- `… did not confirm completion within <n>s; it may still be transitioning` —
  the action was issued but completion wasn't observed within the timeout.
- `Failed to <Start|Stop> VM '<name>'.` — the VM operation threw.
- `Failed to list or process virtual machines in subscription '<id>'…` — often a
  permissions issue on the managed identity.

## Common problems

**A VM never starts/stops and never appears in the logs.**
Check the `Scanning subscription '<id>'` lines. If the VM's subscription is
missing, the identity can't see it. By default the function scans **all
subscriptions the identity can access**; ensure the `Virtual Machine
Contributor` assignment is at a management-group scope that covers the VM's
subscription (an assignment scoped to one subscription hides the others).
This was the root cause of the original "VM not starting" report.

**The VM is scanned but shows `AutoStart='(absent)'` even though it has the tag.**
Tag name lookups are case-sensitive. The tag must be exactly `AutoStart` /
`AutoStop` (see `TagNames.cs`). Compare against the `tags=[…]` list in the log.

**`due=False` when you expected `due=True`.**
Read the `Evaluated cron …` line: check the `UTC offset` (confirms the zone) and
the `next occurrence` versus the `local window`. Remember the window is a 5-minute
slice, so the cron minute must fall inside it.

**No `Information` traces at all (only host `Executing/Executed` lines).**
The App Insights logger filter that drops sub-`Warning` logs was re-added. The
removal must run **after** `ConfigureFunctionsApplicationInsights()` in
`Program.cs` — see `docs/decisions.md`.

## Nightly stability check

The `Nightly Stability Check` workflow (`.github/workflows/nightly-stability-check.yml`)
runs daily: it queries Application Insights for `Schedule pass complete` traces
in the last ~26h and **fails if there were none**, which would indicate the
function stopped running. It only checks that passes _executed_ — not that any
VM was started/stopped, since VM cron schedules aren't guaranteed to fall in the
window. It also reports the latest `BuildSha` seen, confirming which deployed
version is live. Run it on demand from the Actions tab (workflow_dispatch).

## Safe testing

- Set `AutoSchedule__DryRun=true` to log intended actions without touching VMs.
- Set a temporary `AutoStart`/`AutoStop` cron a few minutes ahead (in the VM's
  time zone) and watch the next pass.
- `AutoSchedule__OperationTimeoutSeconds` (default 45) bounds how long a pass
  waits for a start/stop to complete.
