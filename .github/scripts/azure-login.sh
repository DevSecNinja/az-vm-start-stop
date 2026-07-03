#!/usr/bin/env bash
# Authenticate to Azure via GitHub OIDC federation (no stored secret), then
# confirm the identity has usable subscription access.
#
# Requires env: AZURE_CLIENT_ID, AZURE_TENANT_ID, AZURE_SUBSCRIPTION_ID and the
# ACTIONS_ID_TOKEN_REQUEST_* variables (present when the job has id-token: write).
set -euo pipefail

id_token=$(curl -sS --fail \
    -H "Authorization: bearer ${ACTIONS_ID_TOKEN_REQUEST_TOKEN}" \
    "${ACTIONS_ID_TOKEN_REQUEST_URL}&audience=api://AzureADTokenExchange" |
    jq -r '.value')
if [ -z "${id_token}" ] || [ "${id_token}" = "null" ]; then
    echo "::error::Failed to obtain a GitHub OIDC token for Azure login."
    exit 1
fi

az login --service-principal \
    --username "${AZURE_CLIENT_ID}" \
    --tenant "${AZURE_TENANT_ID}" \
    --federated-token "${id_token}" \
    --output none
az account set --subscription "${AZURE_SUBSCRIPTION_ID}"

sub_count=$(az account list --query "length([?state=='Enabled'])" --output tsv)
if [ "${sub_count:-0}" -lt 1 ]; then
    echo "::error::Azure login succeeded but the identity has access to no enabled subscriptions."
    exit 1
fi
active_sub=$(az account show --query name --output tsv)
echo "Azure login OK — ${sub_count} accessible subscription(s); active: ${active_sub}."
