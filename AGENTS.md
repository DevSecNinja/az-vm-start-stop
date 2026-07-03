# AGENTS.md

Guidance for AI coding agents working in this repository. Humans should read the
[`README.md`](README.md) and [`docs/`](docs/) first; this file captures the
conventions and gotchas an agent needs to make correct, low-risk changes.

## What this is

Tag-driven auto-start/auto-stop for Azure VMs: a single **timer-triggered Azure
Function** (C# / **.NET 8 isolated worker**) that scans VMs, reads `AutoStart` /
`AutoStop` cron tags, and starts or deallocates the ones due. Infrastructure is
**Bicep** (resource-group scoped). Deploy is via **GitHub OIDC** (no stored
secrets); the function runs as a **user-assigned managed identity**. See
[`docs/architecture.md`](docs/architecture.md).

## Project layout

```
src/AzVmStartStop.Functions/         # Function app
  Program.cs                         # Host + DI wiring (see App Insights gotcha below)
  ScheduleFunction.cs                # Timer trigger entrypoint
  Services/VmScheduleService.cs      # Orchestration: scan, decide, act, count
  Services/ArmVmInventory.cs         # ARM SDK impl of IVmInventory (prod)
  Services/IVmInventory.cs           # Abstraction that makes the orchestrator testable
  Services/CronScheduleEvaluator.cs  # 5-field cron evaluated in the VM's time zone
  Options/AutoScheduleOptions.cs     # Bound config (AutoSchedule:*)
  BuildInfo.cs                       # Reads SourceRevisionId -> logged as BuildSha
src/AzVmStartStop.Functions.Tests/   # xUnit tests (cron/tz, power-state, orchestration)
infra/main.bicep                     # identity, storage, plan, App Insights, alert, function
.github/workflows/                   # ci-cd.yml, lint.yml, nightly-stability-check.yml, release-*
docs/                                # architecture, decisions, troubleshooting, deployment
```

## Build, test, lint

Use the local .NET 8 SDK. From the repo root:

```bash
dotnet build -c Release
dotnet test  -c Release          # smallest useful check for code changes
```

Linting/formatting is driven by [mise](https://mise.jdx.dev/) (`.mise.toml`) and
mirrors the shared lint workflow (`.github/workflows/lint.yml`, reusable from
`DevSecNinja/.github`). Run the same checks the hooks/CI run:

```bash
mise install                         # install pinned tools
mise exec -- lefthook run pre-commit # dprint, yamlfmt/yamllint, shfmt/shellcheck, actionlint, zizmor, checkov, trivy, gitleaks
mise exec -- dprint fmt              # auto-fix Markdown/JSON/TOML formatting
```

Git hooks are configured in [`.lefthook.toml`](.lefthook.toml); install them once
with `mise exec -- lefthook install` (the devcontainer does this automatically).

## Conventions

- **Commits:** [Conventional Commits](https://www.conventionalcommits.org)
  (enforced by the `commit-msg` hook via `cog verify`, config in `cog.toml`).
  Releases are automated by **release-please** — do not hand-edit `CHANGELOG.md`,
  `.release-please-manifest.json`, or bump versions manually. Include the trailer
  `Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>`.
- **Files:** LF line endings; always end files with a trailing newline (dprint
  enforces this).
- **Indentation:** spaces, not tabs.
- **Version pinning:** pin dependencies as precisely as possible (exact versions
  or SHA digests) and keep them **Renovate-managed**. New non-native pins get a
  `# renovate: datasource=X depName=Y` comment matched by the shared custom
  managers in `DevSecNinja/.github`; tool versions live in `.mise.toml`. Do not
  introduce floating versions (e.g. `8.0.x`).
- **CI parity:** any check added to the lint workflow should also exist as a
  lefthook hook, and vice versa.
- **Azure in CI:** prefer the **Azure CLI** over `azure/*` actions (avoids the
  deprecated node20 runtime); keep OIDC (`id-token: write`, scoped to the deploy
  job) and `persist-credentials: false` on checkouts.

## Gotchas — read before changing these

- **App Insights log filter (`Program.cs`).** `ConfigureFunctionsApplicationInsights()`
  installs a `LoggerFilterRule` that drops logs below `Warning`. The removal of
  that rule MUST be registered **immediately after** the AI setup, or `Information`
  logs never reach App Insights. Order matters. (decision #3)
- **`Microsoft.ApplicationInsights.WorkerService` pinned `<3.0.0`.** AI SDK 3.x
  removed `ITelemetryInitializer`, which the Functions Worker AI integration needs
  → `TypeLoadException` → worker aborts at startup (exit 134) → no timer passes.
  This is **not caught by compile or unit tests** — only by running the worker.
  Do not remove the `<3.0.0` constraint in `renovate.json5`/csproj. Tracking: #26.
- **Scan all subscriptions.** When `AutoSchedule:SubscriptionIds` is empty, the
  function enumerates **every** subscription the identity can access (the role is
  usually assigned at management-group scope). Don't revert to a single-subscription
  scan. (decision #1)
- **.NET stays on 8.x (LTS).** The app targets `net8.0`; Renovate constrains the CI
  SDK (`dotnet-sdk`) to `<9.0.0`. Don't bump the target framework or SDK to 9/10.
- **No VNet / private networking (by design).** Public networking is intentional
  for this lean deployment; the related checkov findings are documented
  `//checkov:skip` suppressions in `infra/main.bicep`. (decision #8)
- **Bicep `apiVersion`s** on top-level resources are Renovate-managed automatically
  (the `bicep` manager); nested resources are not, but this repo declares all
  resources at top level.

## Deploying / infra

Deploy happens on push to `main` (or manual dispatch) via `ci-cd.yml`. Bicep is
deployed resource-group scoped through the Azure CLI. There is **no HTTP trigger**
(timer only) and the app runs on **Linux Flex Consumption**. Full one-time setup
(Entra app + OIDC federation, repository variables, `Virtual Machine Contributor`
assignment) is in [`docs/deployment.md`](docs/deployment.md).
