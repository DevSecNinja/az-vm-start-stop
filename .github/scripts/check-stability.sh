#!/usr/bin/env bash
# Fail if the function has not logged a "Schedule pass complete" trace recently,
# which would indicate it has stopped running. Reports the latest deployed
# BuildSha seen. Does not assert VM start/stop actions (cron schedules on VM
# tags aren't guaranteed to fall in the window).
#
# Requires env: AZURE_RESOURCE_GROUP, LOOKBACK_HOURS.
set -euo pipefail

az extension add --name application-insights --only-show-errors

app_name=$(az resource list \
    --resource-group "${AZURE_RESOURCE_GROUP}" \
    --resource-type Microsoft.Insights/components \
    --query "[0].name" --output tsv)
if [ -z "${app_name}" ]; then
    echo "::error::No Application Insights component found in resource group '${AZURE_RESOURCE_GROUP}'."
    exit 1
fi
echo "Querying Application Insights component '${app_name}' over the last ${LOOKBACK_HOURS}h..."

query="traces | where timestamp > ago(${LOOKBACK_HOURS}h) | where message has 'Schedule pass complete' | summarize runs = count(), (lastRun, lastSha) = arg_max(timestamp, tostring(customDimensions.BuildSha))"

result=$(az monitor app-insights query \
    --app "${app_name}" \
    --resource-group "${AZURE_RESOURCE_GROUP}" \
    --analytics-query "${query}" \
    --offset "${LOOKBACK_HOURS}h" \
    --output json)

runs=$(echo "${result}" | jq -r '.tables[0].rows[0][0] // 0')
last_run=$(echo "${result}" | jq -r '(.tables[0].rows[0][1] // "") | if . == "" then "n/a" else . end')
last_sha=$(echo "${result}" | jq -r '(.tables[0].rows[0][2] // "") | if . == "" then "unknown" else . end')

echo "Schedule passes in last ${LOOKBACK_HOURS}h: ${runs} (last: ${last_run}, BuildSha: ${last_sha})"

if [ "${runs:-0}" -lt 1 ]; then
    echo "::error::No 'Schedule pass complete' traces in the last ${LOOKBACK_HOURS}h — the function may not be running."
    exit 1
fi
echo "Stability check passed: ${runs} schedule pass(es) recently; latest running BuildSha=${last_sha}."
