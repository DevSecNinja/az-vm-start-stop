#!/usr/bin/env bash
# Download a pinned Azure Cost Estimator (ACE) release, verify its checksum, and
# expose the binary on PATH for later workflow steps.
#
# Requires env: RUNNER_TEMP, GITHUB_PATH.
set -euo pipefail

# renovate: datasource=github-releases depName=TheCloudTheory/arm-estimator
ace_version='1.6.5'
ace_sha256='e991f9b097a82ac923a1bbf6bb47639a4acd99e293a38a4d82b763a814387df8'

install_dir="${RUNNER_TEMP}/azure-cost-estimator"
archive_path="${install_dir}/linux-x64.zip"

rm -rf "${install_dir}"
mkdir -p "${install_dir}"

curl -fsSL \
    --output "${archive_path}" \
    "https://github.com/TheCloudTheory/arm-estimator/releases/download/${ace_version}/linux-x64.zip"

echo "${ace_sha256}  ${archive_path}" | sha256sum --check --status

unzip -q "${archive_path}" -d "${install_dir}"
chmod +x "${install_dir}/azure-cost-estimator"

echo "${install_dir}" >>"${GITHUB_PATH}"
"${install_dir}/azure-cost-estimator" --version
