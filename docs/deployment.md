# Deployment

The deploy workflow (`.github/workflows/ci-cd.yml`) builds, tests, and — on
`main` — deploys via GitHub OIDC. This guide covers the one-time identity setup
and the manual deploy path.

## Security model

The deployment is **resource-group scoped** so the CI identity needs only **Owner on a
single, pre-created resource group** — no subscription- or management-group-level
rights. Add the [role-assignment condition](#deployment-role-assignment-condition)
below to constrain which roles it can assign and to which principal types.

- **No secrets in this repo.** Deployment authenticates with GitHub OIDC federated
  credentials; the running function uses a **user-assigned managed identity** (created
  by the deployment, inside the RG).
- Storage uses **identity-based** connections (`allowSharedKeyAccess: false`) — no
  access keys. The identity is granted **`Storage Blob Data Owner`** on its storage
  account (an in-RG assignment, so RG Owner can create it).
- The broad **`Virtual Machine Contributor`** grant — which lets the function start/stop
  VMs across other resource groups and subscriptions — is **not** created by the
  deployment. Because it targets a scope _above_ the RG (management group or
  subscription), it is a one-time assignment performed by an administrator (see
  step 3 below). This keeps the CI identity limited to Owner on one RG.

## One-time setup: Entra app + GitHub OIDC

The deploy workflow signs in to Azure with **workload identity federation** (OIDC) —
no client secret is ever created or stored. You create a Microsoft Entra app
registration, add a **federated credential** that trusts this repository's GitHub
Actions, grant it **Owner on the pre-created resource group**, and record its ids as
GitHub repository variables. Separately, an administrator grants the function's
identity `Virtual Machine Contributor` at management-group scope (step 3).

> The workflow's `deploy` job runs in the GitHub **`production`** environment, so the
> federated credential's _subject_ must use the `environment:production` form shown
> below (not a branch subject). Create the environment first under **Settings →
> Environments → New environment → `production`**.

### Deployment role-assignment condition

When granting the deployment identity **Owner** on the pre-created resource group,
choose **Allow user to only assign selected roles to selected principals (fewer
privileges)**. Click **Select roles and principals** and use:

| Setting                    | Value                                               |
| -------------------------- | --------------------------------------------------- |
| Condition                  | Preconfigured (Constrain roles and principal types) |
| Assignable roles           | Storage Blob Data Owner                             |
| Assignable principal types | `ServicePrincipal`                                  |

Managed identities are service principals. This allows the deployment to create
and remove the function identity's `Storage Blob Data Owner` assignment on its
storage account, the only role assignment created by `infra/main.bicep`. Use the
principal **type**, not a specific principal ID: the function's managed identity
is created during deployment and gets a new principal ID if deleted and recreated.
The recipient is the function identity, not the GitHub deployment identity.

Keep the Owner assignment at **resource-group scope**. The condition constrains
role-assignment management; it does not restrict Owner's other resource-management
permissions within that scope. Do not add Owner, User Access Administrator, Role
Based Access Control Administrator, or Virtual Machine Contributor to the
assignable roles. An administrator grants the function's VM permissions separately
after deployment.

For an existing Owner assignment, edit its condition under the resource group's
**Access control (IAM) → Role assignments**. The CLI example below creates the
assignment with the equivalent condition, covering both role-assignment creation
and deletion. See Microsoft's
[role-and-principal-type condition example](https://learn.microsoft.com/azure/role-based-access-control/delegate-role-assignments-examples#example-constrain-acr-management).

### Option A — Azure CLI (recommended)

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

# 3) Grant Owner on ONLY the resource group, with constrained role delegation.
#    b7e6dc6d-f1e8-4753-8033-0f276bb0955b is Storage Blob Data Owner.
SP_OBJECT_ID=$(az ad sp show --id "$APP_ID" --query id -o tsv)
ROLE_ASSIGNMENT_CONDITION="(
    (!(ActionMatches{'Microsoft.Authorization/roleAssignments/write'}))
    OR (
        @Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {b7e6dc6d-f1e8-4753-8033-0f276bb0955b}
        AND
        @Request[Microsoft.Authorization/roleAssignments:PrincipalType] ForAnyOfAnyValues:StringEqualsIgnoreCase {'ServicePrincipal'}
    )
)
AND
(
    (!(ActionMatches{'Microsoft.Authorization/roleAssignments/delete'}))
    OR (
        @Resource[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {b7e6dc6d-f1e8-4753-8033-0f276bb0955b}
        AND
        @Resource[Microsoft.Authorization/roleAssignments:PrincipalType] ForAnyOfAnyValues:StringEqualsIgnoreCase {'ServicePrincipal'}
    )
)"
az role assignment create \
    --assignee-object-id "$SP_OBJECT_ID" \
    --assignee-principal-type ServicePrincipal \
    --role "Owner" \
    --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP" \
    --condition "$ROLE_ASSIGNMENT_CONDITION" \
    --condition-version "2.0"

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

> By default the function scans **every subscription the identity can access**
> (all subscriptions under the management-group assignment). To restrict the
> scan to specific subscriptions, list them in `AutoSchedule:SubscriptionIds`
> (Bicep `subscriptionIds` parameter); the identity's management-group
> assignment must still cover those subscriptions.

### Option B — Azure portal

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
   Before saving, configure the
   [deployment role-assignment condition](#deployment-role-assignment-condition):
   **Storage Blob Data Owner** assignable only to **`ServicePrincipal`**.
4. After the first deploy, at the **management group** (or subscription) → **Access
   control (IAM)** → assign **Virtual Machine Contributor** to the function's
   user-assigned identity.

### GitHub repository variables

Under **Settings → Secrets and variables → Actions → Variables**, set these
**repository variables** (they are ids, not secrets):

| Variable                | Description                                                     |
| ----------------------- | --------------------------------------------------------------- |
| `AZURE_CLIENT_ID`       | Application (client) id of the Entra app (the deploy identity). |
| `AZURE_TENANT_ID`       | Entra directory (tenant) id.                                    |
| `AZURE_SUBSCRIPTION_ID` | Subscription containing the pre-created resource group.         |
| `AZURE_RESOURCE_GROUP`  | Pre-created resource group name for the function resources.     |
| `AZURE_NAME_PREFIX`     | 3–11 lowercase alphanumeric prefix for resource names.          |

Push to `main` (or run the workflow manually) to build, test, and deploy.

## Manual deploy (Azure CLI)

```bash
# Resource group must already exist.
az deployment group create \
  --resource-group <pre-created-rg-name> \
  --template-file ../infra/main.bicep \
  --parameters ../infra/main.sample.bicepparam

func azure functionapp publish <functionAppName>
```
