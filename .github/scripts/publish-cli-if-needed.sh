#!/usr/bin/env bash
set -euo pipefail

PACKAGE_NAME="$(node -p "require('./package.json').name")"
PACKAGE_VERSION="$(node -p "require('./package.json').version")"

echo "Resolved package: ${PACKAGE_NAME}@${PACKAGE_VERSION}"

if EXISTING_VERSION="$(npm view "${PACKAGE_NAME}@${PACKAGE_VERSION}" version --silent 2>/dev/null)"; then
  EXISTING_VERSION="$(echo "${EXISTING_VERSION}" | tr -d '\r\n')"
  if [[ "${EXISTING_VERSION}" == "${PACKAGE_VERSION}" ]]; then
    echo "Package version already published on npm. Skipping publish."
    exit 0
  fi
fi

echo "Package version not found on npm. Publishing now..."
npm run release:publish
