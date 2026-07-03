#!/usr/bin/env bash
# Deploy the Bicep template to the resource group, verify the result, and export
# the resulting function app name as a step output. On failure, dump the failed
# deployment operations for diagnosis.
#
# Requires env: AZURE_RESOURCE_GROUP, AZURE_NAME_PREFIX, GITHUB_RUN_NUMBER, GITHUB_OUTPUT.
set -euo pipefail

deployment_name="azvmstartstop-${GITHUB_RUN_NUMBER}"

if ! outputs=$(az deployment group create \
    --resource-group "${AZURE_RESOURCE_GROUP}" \
    --name "${deployment_name}" \
    --template-file infra/main.bicep \
    --parameters namePrefix="${AZURE_NAME_PREFIX}" \
    --query 'properties.outputs' \
    --output json); then
    echo "::error::Bicep deployment '${deployment_name}' failed. Failed operations:"
    az deployment operation group list \
        --resource-group "${AZURE_RESOURCE_GROUP}" \
        --name "${deployment_name}" \
        --query "[?properties.provisioningState=='Failed'].{resource:properties.targetResource.resourceType, code:properties.statusMessage.error.code, message:properties.statusMessage.error.message}" \
        --output json || true
    exit 1
fi

function_app_name=$(echo "${outputs}" | jq -r '.functionAppName.value // empty')
if [ -z "${function_app_name}" ]; then
    echo "::error::Bicep deployment '${deployment_name}' succeeded but returned no functionAppName output. Outputs were:"
    echo "${outputs}"
    exit 1
fi
echo "Infrastructure deployed; functionAppName=${function_app_name}."
echo "functionAppName=${function_app_name}" >>"${GITHUB_OUTPUT}"
