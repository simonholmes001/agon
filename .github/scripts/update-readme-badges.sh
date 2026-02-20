#!/usr/bin/env bash
#
# update-readme-badges.sh
#
# Reads coverage-summary.json and vitest output to update
# the Tests and Coverage badges in README.md.
#
# Usage: .github/scripts/update-readme-badges.sh <test-count> <coverage-json-path> <readme-path>

set -euo pipefail

TEST_COUNT="${1:?Usage: update-readme-badges.sh <test-count> <coverage-json-path> <readme-path>}"
COVERAGE_JSON="${2:?Missing coverage-summary.json path}"
README="${3:?Missing README.md path}"

if [[ ! -f "$COVERAGE_JSON" ]]; then
  echo "⚠️  Coverage summary not found at $COVERAGE_JSON — skipping badge update."
  exit 0
fi

if [[ ! -f "$README" ]]; then
  echo "⚠️  README not found at $README — skipping badge update."
  exit 0
fi

# Extract line coverage percentage from json-summary (total.lines.pct)
LINE_PCT=$(python3 -c "
import json, sys
with open('$COVERAGE_JSON') as f:
    data = json.load(f)
pct = data.get('total', {}).get('lines', {}).get('pct', 0)
print(int(round(pct)))
")

# Pick badge colour based on coverage percentage
if (( LINE_PCT >= 90 )); then
  COLOUR="brightgreen"
elif (( LINE_PCT >= 80 )); then
  COLOUR="green"
elif (( LINE_PCT >= 70 )); then
  COLOUR="yellow"
elif (( LINE_PCT >= 60 )); then
  COLOUR="orange"
else
  COLOUR="red"
fi

echo "📊 Tests: $TEST_COUNT passing | Coverage: ${LINE_PCT}% lines ($COLOUR)"

# Update Tests badge  —  matches [![Tests](...)]()
sed -i'' -e "s|\!\[Tests\](https://img.shields.io/badge/Tests-[^)]*)|![Tests](https://img.shields.io/badge/Tests-${TEST_COUNT}_passing-brightgreen?style=flat-square)|g" "$README"

# Update Coverage badge  —  matches [![Coverage](...)]()
sed -i'' -e "s|\!\[Coverage\](https://img.shields.io/badge/Coverage-[^)]*)|![Coverage](https://img.shields.io/badge/Coverage-${LINE_PCT}%25_lines-${COLOUR}?style=flat-square)|g" "$README"

echo "✅ README badges updated."
