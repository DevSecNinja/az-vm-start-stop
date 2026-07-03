#!/usr/bin/env bash
# Post-create setup for the devcontainer.
#
# Installs Azure Functions Core Tools from Microsoft's official package feed
# (packages.microsoft.com) rather than a third-party/community devcontainer
# feature, then restores the .NET solution. The .NET SDK and Azure CLI come
# from the official devcontainers/features referenced in devcontainer.json.
set -euo pipefail

install_functions_core_tools() {
    if command -v func >/dev/null 2>&1; then
        echo "Azure Functions Core Tools already present: $(func --version)"
        return
    fi

    echo "Installing Azure Functions Core Tools from packages.microsoft.com (official feed)..."
    # shellcheck disable=SC1091
    . /etc/os-release
    local version_major="${VERSION_ID%%.*}"

    sudo apt-get update
    sudo apt-get install -y curl gnupg apt-transport-https ca-certificates
    curl -fsSL https://packages.microsoft.com/keys/microsoft.asc |
        gpg --dearmor |
        sudo tee /etc/apt/trusted.gpg.d/microsoft.gpg >/dev/null
    echo "deb [arch=amd64] https://packages.microsoft.com/debian/${version_major}/prod ${VERSION_CODENAME} main" |
        sudo tee /etc/apt/sources.list.d/dotnetdev.list >/dev/null
    sudo apt-get update
    sudo apt-get install -y azure-functions-core-tools-4
}

install_functions_core_tools
az bicep install
dotnet restore
