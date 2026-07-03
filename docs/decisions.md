# Design decisions

Lightweight decision log. Each entry captures a choice, why it was made, and
the alternatives considered.

## 1. Scan all accessible subscriptions by default

**Decision.** When `AutoSchedule:SubscriptionIds` is empty, enumerate every
subscription the managed identity can access (`ArmClient.GetSubscriptions()`),
not just the identity's _default_ subscription.

**Why.** The function's `Virtual Machine Contributor` role is typically assigned
at **management-group** scope, so target VMs live in many subscriptions. The
previous behaviour (`GetDefaultSubscriptionAsync`) scanned only one subscription,
so VMs elsewhere were never seen — the root cause of the original "VM not
starting" bug. Configuring `SubscriptionIds` still restricts the scan when
desired.

**Alternatives.** Keep default-subscription-only (rejected: surprising, and the
common MG-level assignment makes it wrong); require `SubscriptionIds` always
(rejected: poor out-of-box experience).

## 2. Full step-by-step, structured logging

**Decision.** Log each step of a pass at `Information` with structured scopes
(`RunId`, `VmName`, `ResourceGroup`, `SubscriptionId`, `Action`): subscriptions
scanned, each VM's actual tag keys and raw cron values, the full cron evaluation
(resolved zone + UTC offset + local window + next occurrence + `due`), the power
state, and the action taken.

**Why.** The original issue was undiagnosable from logs. Rich, filterable logs
turned a multi-hour guessing game into a single query. See
`docs/troubleshooting.md`.

**Trade-off.** More log volume. Acceptable for the expected fleet size; can be
gated behind a verbose flag later if needed.

## 3. Remove the App Insights sub-`Warning` log filter — after AI setup

**Decision.** In `Program.cs`, remove the `LoggerFilterRule` that the isolated
worker's `ConfigureFunctionsApplicationInsights()` installs (which drops logs
below `Warning`), and register that removal **immediately after** the AI setup
so it runs last and wins.

**Why.** `Information` diagnostics never reached App Insights. Registering the
removal earlier (e.g. in a preceding `ConfigureLogging` block) does not work —
the rule is re-added afterwards.

## 4. Wait for completion with a bounded timeout

**Decision.** Start/deallocate use `WaitUntil.Completed`, awaited under a
`OperationTimeoutSeconds` (default 45s) timeout. Log both when the action is
fired and when it is confirmed complete; on timeout, log a warning and still
count the action (it continues in Azure).

**Why.** `WaitUntil.Started` returns on request acceptance, so we couldn't
truthfully log "has started/stopped". The timeout stops a single pass from
blocking indefinitely on a slow operation.

## 5. Abstract the ARM SDK behind `IVmInventory`

**Decision.** `VmScheduleService` depends on `IVmInventory` /
`IVmSubscriptionScope` / `IVmScheduleTarget` instead of `ArmClient` directly;
`ArmVmInventory` is the production implementation.

**Why.** The ARM SDK types are effectively unmockable, so the orchestration
(multi-subscription scanning, decisions, dry-run, counting, error handling) was
untested. The abstraction enables `VmScheduleServiceTests` with faked
subscriptions/VMs and locks in the multi-subscription behaviour in CI.

## 6. Prefer Azure CLI over `azure/*` GitHub Actions

**Decision.** The deploy workflow logs in and deploys via the **Azure CLI**
(`az login --federated-token`, `az deployment group create`) instead of
`azure/login` and `azure/arm-deploy`.

**Why.** Those actions still ship the deprecated **node20** runtime with no
node24 release, triggering deprecation warnings. The CLI is preinstalled on the
runner, has no node runtime, and is better maintained. OIDC (`id-token: write`)
is preserved — still no stored secrets.

## 7. Devcontainer includes the full toolchain

**Decision.** The devcontainer adds features for the **.NET 8 SDK**, **Azure
CLI** and **Azure Functions Core Tools**, plus C#/Functions/Bicep extensions.

**Why.** The base dotfiles image ships general tooling but not this service's
toolchain, so `dotnet`, `func` and `az` were unavailable for local development.

## 8. No VNet integration / private networking

**Decision.** The Function App and its storage account keep public networking
(no VNet integration, no private endpoints, no default-deny storage firewall).
Related checkov findings (`CKV_AZURE_35`, `CKV_AZURE_222`, etc.) are documented
as intentional `//checkov:skip` suppressions in `infra/main.bicep`.

**Why.** VNet integration on Flex Consumption is technically supported, but it
requires a delegated subnet plus Private Endpoint resources, which add ongoing
cost and operational complexity aimed at production/regulated workloads. This is
a lean, low-stakes personal deployment: a timer-only function with no inbound
HTTP surface, whose storage is already hardened (TLS 1.2, HTTPS-only,
identity-based access with no shared key, no public blob). The added networking
cost/complexity is not justified here.

**Revisit if.** The workload moves to a production/regulated tenant, or storage
must be reachable only privately. Tracking: closed issue #28.
