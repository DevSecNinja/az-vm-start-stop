#!/usr/bin/env bash
# Fail fast if the target resource group does not exist. The deployment is
# resource-group scoped and intentionally does not create it.
#
# Requires env: AZURE_RESOURCE_GROUP.
set -euo pipefail

if [ "$(az group exists --name "${AZURE_RESOURCE_GROUP}")" != "true" ]; then
    echo "::error::Resource group '${AZURE_RESOURCE_GROUP}' does not exist. This deployment is resource-group scoped and does not create it — pre-create the group first (see docs/deployment.md)."
    exit 1
fi
echo "Resource group '${AZURE_RESOURCE_GROUP}' found."
