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
set +e
PUBLISH_OUTPUT="$(npm run release:publish 2>&1)"
PUBLISH_EXIT_CODE=$?
set -e

echo "${PUBLISH_OUTPUT}"

if [ "${PUBLISH_EXIT_CODE}" -eq 0 ]; then
  echo "Publish completed successfully."
  exit 0
fi

if EXISTING_VERSION_AFTER_FAIL="$(npm view "${PACKAGE_NAME}@${PACKAGE_VERSION}" version --silent 2>/dev/null)"; then
  EXISTING_VERSION_AFTER_FAIL="$(echo "${EXISTING_VERSION_AFTER_FAIL}" | tr -d '\r\n')"
  if [[ "${EXISTING_VERSION_AFTER_FAIL}" == "${PACKAGE_VERSION}" ]]; then
    echo "Version ${PACKAGE_VERSION} is already published after failed publish attempt; treating as idempotent success."
    exit 0
  fi
fi

if echo "${PUBLISH_OUTPUT}" | grep -qi "previously published versions"; then
  echo "Publish output indicates version is already published; treating as idempotent success."
  exit 0
fi

echo "Publish failed and package version is still unavailable on npm."
exit "${PUBLISH_EXIT_CODE}"
