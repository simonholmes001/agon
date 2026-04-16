#!/usr/bin/env bash
set -euo pipefail

PACKAGE_NAME="$(node -p "require('./package.json').name")"
PACKAGE_VERSION="$(node -p "require('./package.json').version")"

echo "Resolved package: ${PACKAGE_NAME}@${PACKAGE_VERSION}"

lookup_exact_version() {
  local attempts="$1"
  local sleep_seconds="$2"
  local attempt
  local lookup_result

  for attempt in $(seq 1 "${attempts}"); do
    if lookup_result="$(npm view "${PACKAGE_NAME}@${PACKAGE_VERSION}" version --silent 2>/dev/null)"; then
      lookup_result="$(echo "${lookup_result}" | tr -d '\r\n')"
      if [[ "${lookup_result}" == "${PACKAGE_VERSION}" ]]; then
        return 0
      fi
    fi

    if [[ "${attempt}" -lt "${attempts}" ]]; then
      sleep "${sleep_seconds}"
    fi
  done

  return 1
}

print_sanitized_log() {
  local log_path="$1"
  # Redact common npm auth-token patterns before printing.
  sed -E \
    -e 's#(//[^ ]+:_authToken=)[^[:space:]]+#\1[REDACTED]#g' \
    -e 's#(_authToken=)[^[:space:]]+#\1[REDACTED]#g' \
    "${log_path}"
}

if lookup_exact_version 1 0; then
  echo "Package version already published on npm. Skipping publish."
  exit 0
fi

echo "Package version not found on npm. Publishing now..."
PUBLISH_LOG_PATH="$(mktemp)"
PUBLISH_EXIT_CODE=0

if ! npm run release:publish >"${PUBLISH_LOG_PATH}" 2>&1; then
  PUBLISH_EXIT_CODE=$?
fi

if [[ "${PUBLISH_EXIT_CODE}" -eq 0 ]]; then
  print_sanitized_log "${PUBLISH_LOG_PATH}"
  echo "Publish completed successfully."
  rm -f "${PUBLISH_LOG_PATH}"
  exit 0
fi

echo "Publish command failed; evaluating idempotency before failing."

if lookup_exact_version 3 2; then
  echo "Version ${PACKAGE_VERSION} is already published after failed publish attempt; treating as idempotent success."
  rm -f "${PUBLISH_LOG_PATH}"
  exit 0
fi

if grep -Eqi "EPUBLISHCONFLICT|cannot publish over|previously published versions|cannot modify pre-existing version" "${PUBLISH_LOG_PATH}"; then
  echo "Publish output indicates an already-published version conflict; treating as idempotent success."
  rm -f "${PUBLISH_LOG_PATH}"
  exit 0
fi

echo "Publish failed and package version is still unavailable on npm."
echo "Sanitized publish output:"
print_sanitized_log "${PUBLISH_LOG_PATH}"
rm -f "${PUBLISH_LOG_PATH}"
exit "${PUBLISH_EXIT_CODE}"
