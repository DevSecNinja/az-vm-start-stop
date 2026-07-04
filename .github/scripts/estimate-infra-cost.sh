#!/usr/bin/env bash
# Estimate the cost of the Bicep deployment using Azure Cost Estimator (ACE) and
# publish the Markdown report to the GitHub Actions step summary.
#
# Requires env: AZURE_SUBSCRIPTION_ID, AZURE_RESOURCE_GROUP, AZURE_NAME_PREFIX,
# RUNNER_TEMP, GITHUB_STEP_SUMMARY.
set -euo pipefail

report_dir="${RUNNER_TEMP}/ace-report"
markdown_report="${report_dir}/estimate.md"
json_report="${report_dir}/estimate.json"

mkdir -p "${report_dir}"

azure-cost-estimator \
    infra/main.bicep \
    "${AZURE_SUBSCRIPTION_ID}" \
    "${AZURE_RESOURCE_GROUP}" \
    --inline "namePrefix=${AZURE_NAME_PREFIX}" \
    --generate-markdown-output \
    --markdown-output-filename "${markdown_report}" \
    --generate-json-output \
    --json-output-filename "${json_report}" \
    --output-format Table

{
    echo '## Azure cost estimate'
    echo
    cat "${markdown_report}"
} >>"${GITHUB_STEP_SUMMARY}"
