#!/usr/bin/env bash
set -euo pipefail

APP_RG_DEFAULT="rg-agon-dev-swc-app"
ENVIRONMENT_DEFAULT="dev"

APP_RG="${APP_RG_DEFAULT}"
ENVIRONMENT="${ENVIRONMENT_DEFAULT}"
APP_NAME=""
USER_UPN=""
USER_ID=""
GROUP_INPUT=""
DRY_RUN="false"
FORCE="false"
INVITE_IF_MISSING="false"
INVITE_REDIRECT_URL="https://myapplications.microsoft.com"
DEFAULT_INVITE_REDIRECT_HOST="myapplications.microsoft.com"
DRY_RUN_WOULD_INVITE_SENTINEL="__DRY_RUN_WOULD_INVITE__"

usage() {
  cat <<'EOF'
Onboard one tester without sharing provider API keys.

This script:
1) validates that provider keys are configured as Key Vault references in App Service,
2) validates trial access guardrails,
3) adds the target user to the tester Entra group.

Usage:
  infrastructure/scripts/onboard-tester.sh \
    --user-upn user@contoso.com \
    [--group <group-id-or-name>] \
    [--app-name <app-service-name>] \
    [--app-rg <resource-group>] \
    [--environment <env>] \
    [--invite-if-missing] \
    [--invite-redirect-url <url>] \
    [--force] \
    [--dry-run]

Options:
  --user-upn      User principal name/email for the tester (mutually exclusive with --user-id)
  --user-id       Entra object ID of the tester (mutually exclusive with --user-upn)
  --group         Tester Entra group object ID or display name.
                  If omitted, script uses TrialAccess__RequiredEntraGroupObjectIdsCsv from App Service.
  --app-name      App Service name. If omitted, auto-discovered by tags.
  --app-rg        App resource group (default: rg-agon-dev-swc-app)
  --environment   Environment tag used for discovery (default: dev)
  --invite-if-missing
                  If --user-upn does not resolve, invite as Entra guest automatically
                  and continue onboarding with the created guest object.
  --invite-redirect-url
                  Redirect URL used for guest invitation (default: https://myapplications.microsoft.com)
  --force         Continue even if trial-access guardrail flags are not enabled
  --dry-run       Validate and resolve everything without modifying group membership
  --help          Show this help
EOF
}

log() {
  printf '[INFO] %s\n' "$1" >&2
}

warn() {
  printf '[WARN] %s\n' "$1" >&2
}

fail() {
  printf '[ERROR] %s\n' "$1" >&2
  exit 1
}

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || fail "Missing required command: $1"
}

is_uuid_like() {
  local value="$1"
  [[ "$value" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]]
}

is_key_vault_reference() {
  local value="$1"
  [[ "$value" =~ ^@Microsoft\.KeyVault\(SecretUri(WithVersion)?=https://[^[:space:]]+\)$ ]]
}

escape_odata_literal() {
  local value="$1"
  printf '%s\n' "${value//\'/\'\'}"
}

escape_json_string() {
  local value="$1"
  value="${value//\\/\\\\}"
  value="${value//\"/\\\"}"
  value="${value//$'\n'/\\n}"
  value="${value//$'\r'/\\r}"
  value="${value//$'\t'/\\t}"
  printf '%s\n' "$value"
}

is_https_url() {
  local value="$1"
  [[ "$value" =~ ^https://[^[:space:]]+$ ]]
}

url_host() {
  local value="$1"
  local without_scheme host

  without_scheme="${value#https://}"
  host="${without_scheme%%/*}"
  host="${host%%\?*}"
  host="${host%%\#*}"
  printf '%s\n' "$host"
}

invite_guest_user() {
  local user_email="$1"
  local email_json redirect_json request_body invited_user_id
  local error_file error_text

  email_json="$(escape_json_string "$user_email")"
  redirect_json="$(escape_json_string "$INVITE_REDIRECT_URL")"
  request_body="{\"invitedUserEmailAddress\":\"${email_json}\",\"inviteRedirectUrl\":\"${redirect_json}\",\"sendInvitationMessage\":true}"

  log "Inviting guest with redirect URL: ${INVITE_REDIRECT_URL}"

  error_file="$(mktemp)"
  if ! invited_user_id="$(az rest \
    --method POST \
    --url "https://graph.microsoft.com/v1.0/invitations" \
    --headers "Content-Type=application/json" \
    --body "$request_body" \
    --query "invitedUser.id" \
    --output tsv 2>"$error_file")"; then
    error_text="$(tr '\n' ' ' < "$error_file" | sed -e 's/[[:space:]]\+/ /g' -e 's/^ //; s/ $//')"
    rm -f "$error_file"
    fail "Failed to invite '$user_email' as guest. Azure error: ${error_text:-unknown error}"
  fi
  rm -f "$error_file"

  if [[ -z "$invited_user_id" || "$invited_user_id" == "null" ]]; then
    fail "Failed to invite '$user_email' as guest. Check Entra guest invitation policy and Graph permissions."
  fi

  if ! is_uuid_like "$invited_user_id"; then
    fail "Invitation returned invalid invitedUser.id: '$invited_user_id'."
  fi

  printf '%s\n' "$invited_user_id"
}

resolve_app_name() {
  if [[ -n "$APP_NAME" ]]; then
    printf '%s\n' "$APP_NAME"
    return 0
  fi

  local discovered
  discovered="$(az webapp list \
    --resource-group "$APP_RG" \
    --query "[?tags.environment=='${ENVIRONMENT}' && tags.workload=='agon'].name | [0]" \
    --output tsv)"

  if [[ -z "$discovered" || "$discovered" == "null" ]]; then
    discovered="$(az webapp list \
      --resource-group "$APP_RG" \
      --query "[?starts_with(name, 'app-agon-${ENVIRONMENT}-')].name | [0]" \
      --output tsv)"
  fi

  if [[ -z "$discovered" || "$discovered" == "null" ]]; then
    fail "Could not auto-discover App Service in $APP_RG. Pass --app-name explicitly."
  fi

  printf '%s\n' "$discovered"
}

app_setting_value() {
  local app_name="$1"
  local setting_name="$2"
  az webapp config appsettings list \
    --name "$app_name" \
    --resource-group "$APP_RG" \
    --query "[?name=='${setting_name}'].value | [0]" \
    --output tsv
}

validate_app_service_identity() {
  local app_name="$1"
  local principal_id
  principal_id="$(az webapp identity show --name "$app_name" --resource-group "$APP_RG" --query principalId --output tsv)"

  if [[ -z "$principal_id" || "$principal_id" == "null" ]]; then
    fail "App Service managed identity is not enabled. Key Vault-backed provider keys require managed identity."
  fi
}

validate_server_managed_provider_keys() {
  local app_name="$1"
  local keys=("OPENAI_KEY" "CLAUDE_KEY" "GEMINI_KEY" "DEEPSEEK_KEY")
  local found_any="false"

  for key_name in "${keys[@]}"; do
    local key_value
    key_value="$(app_setting_value "$app_name" "$key_name")"

    if [[ -z "$key_value" || "$key_value" == "null" ]]; then
      warn "$key_name is empty."
      continue
    fi

    found_any="true"

    if ! is_key_vault_reference "$key_value"; then
      fail "$key_name is set but not as Key Vault reference. Refusing onboarding to avoid key exposure."
    fi
  done

  if [[ "$found_any" != "true" ]]; then
    fail "No provider key references are configured. Add at least one provider key in Key Vault-backed app settings first."
  fi
}

resolve_group_id() {
  local app_name="$1"
  local resolved_group_id

  if [[ -n "$GROUP_INPUT" ]]; then
    if ! resolved_group_id="$(az ad group show --group "$GROUP_INPUT" --query id --output tsv 2>/dev/null)"; then
      fail "Could not resolve group from input '$GROUP_INPUT'. Check group name/id and directory permissions."
    fi

    if [[ -z "$resolved_group_id" || "$resolved_group_id" == "null" ]]; then
      fail "Could not resolve group id from input '$GROUP_INPUT'. Check group name/id and directory permissions."
    fi

    if ! is_uuid_like "$resolved_group_id"; then
      fail "Resolved group id is not a valid GUID: '$resolved_group_id'."
    fi

    printf '%s\n' "$resolved_group_id"
    return 0
  fi

  local csv
  csv="$(app_setting_value "$app_name" "TrialAccess__RequiredEntraGroupObjectIdsCsv")"
  csv="${csv// /}"

  if [[ -z "$csv" || "$csv" == "null" ]]; then
    fail "TrialAccess__RequiredEntraGroupObjectIdsCsv is empty. Pass --group or configure app settings."
  fi

  if [[ "$csv" == *,* ]]; then
    fail "Multiple group IDs configured in TrialAccess__RequiredEntraGroupObjectIdsCsv. Pass --group explicitly."
  fi

  if ! is_uuid_like "$csv"; then
    fail "TrialAccess__RequiredEntraGroupObjectIdsCsv is not a single valid GUID."
  fi

  printf '%s\n' "$csv"
}

resolve_user_id() {
  local lookup_input resolved_user_id
  local escaped_input filter_results filter_value
  local line candidate_ids_csv deduped_ids deduped_count
  local candidate_id candidate_details_csv candidate_detail

  if [[ -n "$USER_ID" ]] && ! is_uuid_like "$USER_ID"; then
    fail "--user-id must be a valid GUID."
  fi

  lookup_input="$USER_UPN"
  if [[ -n "$USER_ID" ]]; then
    lookup_input="$USER_ID"
  fi

  if resolved_user_id="$(az ad user show --id "$lookup_input" --query id --output tsv 2>/dev/null)"; then
    if [[ -n "$resolved_user_id" && "$resolved_user_id" != "null" ]]; then
      if ! is_uuid_like "$resolved_user_id"; then
        fail "Resolved user object ID is not a valid GUID: '$resolved_user_id'."
      fi

      printf '%s\n' "$resolved_user_id"
      return 0
    fi
  fi

  if [[ -n "$USER_ID" ]]; then
    fail "Could not resolve user object ID from '$lookup_input'. Check --user-id and directory permissions."
  fi

  candidate_ids_csv=""
  escaped_input="$(escape_odata_literal "$lookup_input")"
  for filter_value in \
    "userPrincipalName eq '${escaped_input}'" \
    "mail eq '${escaped_input}'" \
    "otherMails/any(m:m eq '${escaped_input}')"; do
    filter_results="$(az ad user list --filter "$filter_value" --query "[].id" --output tsv 2>/dev/null || true)"
    if [[ -z "$filter_results" ]]; then
      continue
    fi

    while IFS= read -r line; do
      if [[ -z "$line" || "$line" == "null" ]]; then
        continue
      fi

      candidate_ids_csv+="${line}"$'\n'
    done <<< "$filter_results"
  done

  deduped_ids="$(printf '%s' "$candidate_ids_csv" | awk 'NF' | sort -u)"
  deduped_count="$(printf '%s\n' "$deduped_ids" | awk 'NF' | wc -l | tr -d ' ')"

  if [[ "$deduped_count" -eq 0 ]]; then
    if [[ "$INVITE_IF_MISSING" == "true" ]]; then
      if [[ -n "$USER_ID" ]]; then
        fail "--invite-if-missing requires --user-upn (email), not --user-id."
      fi

      if [[ "$DRY_RUN" == "true" ]]; then
        warn "User '$lookup_input' was not found. Dry-run will not invite users."
        printf '%s\n' "$DRY_RUN_WOULD_INVITE_SENTINEL"
        return 0
      fi

      log "User '$lookup_input' not found in tenant. Inviting as guest because --invite-if-missing was provided."
      resolved_user_id="$(invite_guest_user "$lookup_input")"
      log "Guest invitation created user object ID: ${resolved_user_id}"
      printf '%s\n' "$resolved_user_id"
      return 0
    fi

    fail "Could not resolve user from '$lookup_input'. For external users, invite first or pass --invite-if-missing."
  fi

  if [[ "$deduped_count" -gt 1 ]]; then
    candidate_details_csv=""
    while IFS= read -r candidate_id; do
      if [[ -z "$candidate_id" ]]; then
        continue
      fi

      candidate_detail="$(az ad user show \
        --id "$candidate_id" \
        --query "[id,displayName,userPrincipalName,mail] | join(' | ', @)" \
        --output tsv 2>/dev/null || true)"
      if [[ -z "$candidate_detail" ]]; then
        candidate_detail="$candidate_id"
      fi
      candidate_details_csv+="  - ${candidate_detail}"$'\n'
    done <<< "$deduped_ids"

    fail "Multiple users matched '$lookup_input':"$'\n'"${candidate_details_csv}Pass --user-id explicitly to disambiguate."
  fi

  resolved_user_id="$(printf '%s\n' "$deduped_ids" | awk 'NF { print; exit }')"
  if ! is_uuid_like "$resolved_user_id"; then
    fail "Resolved user object ID is not a valid GUID: '$resolved_user_id'."
  fi

  printf '%s\n' "$resolved_user_id"
}

check_trial_access_flags() {
  local app_name="$1"
  local enabled enforce_groups
  enabled="$(app_setting_value "$app_name" "TrialAccess__Enabled")"
  enforce_groups="$(app_setting_value "$app_name" "TrialAccess__EnforceEntraGroupMembership")"

  if [[ "$enabled" != "true" ]]; then
    if [[ "$FORCE" == "true" ]]; then
      warn "TrialAccess__Enabled is '$enabled' (expected 'true'). Continuing because --force was provided."
    else
      fail "TrialAccess__Enabled is '$enabled' (expected 'true'). Use --force to override."
    fi
  fi

  if [[ "$enforce_groups" != "true" ]]; then
    if [[ "$FORCE" == "true" ]]; then
      warn "TrialAccess__EnforceEntraGroupMembership is '$enforce_groups' (expected 'true'). Continuing because --force was provided."
    else
      fail "TrialAccess__EnforceEntraGroupMembership is '$enforce_groups' (expected 'true'). Use --force to override."
    fi
  fi
}

contains_group_id() {
  local csv="$1"
  local group_id="$2"
  local compact
  compact="${csv// /}"
  [[ ",${compact}," == *",${group_id},"* ]]
}

parse_args() {
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --user-upn)
        USER_UPN="${2:-}"
        shift 2
        ;;
      --user-id)
        USER_ID="${2:-}"
        shift 2
        ;;
      --group)
        GROUP_INPUT="${2:-}"
        shift 2
        ;;
      --app-name)
        APP_NAME="${2:-}"
        shift 2
        ;;
      --app-rg)
        APP_RG="${2:-}"
        shift 2
        ;;
      --environment)
        ENVIRONMENT="${2:-}"
        shift 2
        ;;
      --invite-if-missing)
        INVITE_IF_MISSING="true"
        shift
        ;;
      --invite-redirect-url)
        INVITE_REDIRECT_URL="${2:-}"
        shift 2
        ;;
      --force)
        FORCE="true"
        shift
        ;;
      --dry-run)
        DRY_RUN="true"
        shift
        ;;
      --help|-h)
        usage
        exit 0
        ;;
      *)
        fail "Unknown argument: $1"
        ;;
    esac
  done

  if [[ -n "$USER_UPN" && -n "$USER_ID" ]]; then
    fail "Pass either --user-upn or --user-id, not both."
  fi

  if [[ -z "$USER_UPN" && -z "$USER_ID" ]]; then
    fail "Pass one of --user-upn or --user-id."
  fi

  if [[ "$INVITE_IF_MISSING" == "true" && -n "$USER_ID" ]]; then
    fail "--invite-if-missing can only be used with --user-upn."
  fi

  if [[ "$INVITE_IF_MISSING" == "true" && -z "$USER_UPN" ]]; then
    fail "--invite-if-missing requires --user-upn."
  fi

  if [[ -z "$INVITE_REDIRECT_URL" ]]; then
    fail "--invite-redirect-url must not be empty."
  fi

  if ! is_https_url "$INVITE_REDIRECT_URL"; then
    fail "--invite-redirect-url must be an https URL."
  fi

  local redirect_host
  redirect_host="$(url_host "$INVITE_REDIRECT_URL")"
  if [[ -z "$redirect_host" ]]; then
    fail "--invite-redirect-url host could not be parsed."
  fi

  if [[ "$redirect_host" != "$DEFAULT_INVITE_REDIRECT_HOST" && "$FORCE" != "true" ]]; then
    fail "--invite-redirect-url host '$redirect_host' is not in the default allowlist. Use --force to allow it."
  fi

  if [[ "$redirect_host" != "$DEFAULT_INVITE_REDIRECT_HOST" && "$FORCE" == "true" ]]; then
    warn "Using non-default invite redirect host '$redirect_host' because --force was provided."
  fi
}

main() {
  parse_args "$@"
  require_cmd az

  az account show >/dev/null
  log "Azure login/session detected."

  local resolved_app_name
  resolved_app_name="$(resolve_app_name)"
  log "Using App Service: ${resolved_app_name} (rg=${APP_RG})"

  validate_app_service_identity "$resolved_app_name"
  log "App Service managed identity is enabled."

  validate_server_managed_provider_keys "$resolved_app_name"
  log "Provider keys validated as Key Vault references."

  check_trial_access_flags "$resolved_app_name"

  local group_id
  group_id="$(resolve_group_id "$resolved_app_name")"
  if [[ -z "$group_id" || "$group_id" == "null" ]]; then
    fail "Could not resolve group id from input '$GROUP_INPUT'. Check group name/id and directory permissions."
  fi

  if ! is_uuid_like "$group_id"; then
    fail "Resolved group id is not a valid GUID: '$group_id'."
  fi

  log "Using tester group object ID: ${group_id}"

  local configured_groups_csv
  configured_groups_csv="$(app_setting_value "$resolved_app_name" "TrialAccess__RequiredEntraGroupObjectIdsCsv")"
  if [[ -n "$configured_groups_csv" && "$configured_groups_csv" != "null" ]] && ! contains_group_id "$configured_groups_csv" "$group_id"; then
    warn "Selected group is not present in TrialAccess__RequiredEntraGroupObjectIdsCsv."
  fi

  local resolved_user_id
  resolved_user_id="$(resolve_user_id)"
  if [[ "$resolved_user_id" == "$DRY_RUN_WOULD_INVITE_SENTINEL" ]]; then
    if [[ "$DRY_RUN" != "true" ]]; then
      fail "Internal error: unexpected dry-run invite sentinel in non-dry-run mode."
    fi

    log "Dry-run: user '$USER_UPN' would be invited as guest and then added to group '${group_id}'."
    log "Dry run complete. No invitation or membership changes were made."
    printf 'NEXT: run without --dry-run to send invitation and add group membership.\n'
    exit 0
  fi

  if [[ -z "$resolved_user_id" || "$resolved_user_id" == "null" ]]; then
    fail "Could not resolve user object ID. Check --user-upn/--user-id and directory permissions."
  fi

  if ! is_uuid_like "$resolved_user_id"; then
    fail "Resolved user object ID is not a valid GUID: '$resolved_user_id'."
  fi

  log "Resolved tester user object ID: ${resolved_user_id}"

  if [[ "$DRY_RUN" == "true" ]]; then
    log "Dry run complete. No membership changes were made."
    printf 'NEXT: ask tester to run `agon login` and start using Agon. No provider API keys are shared.\n'
    exit 0
  fi

  local already_member
  already_member="$(az ad group member check --group "$group_id" --member-id "$resolved_user_id" --query value --output tsv || echo "false")"
  if [[ "$already_member" == "true" ]]; then
    log "User is already a member of the tester group."
  else
    az ad group member add --group "$group_id" --member-id "$resolved_user_id"
    log "Added user to tester group."
  fi

  printf 'SUCCESS: tester onboarded without sharing provider keys.\n'
  printf 'Tester action: run `agon login` and use Agon normally.\n'
}

main "$@"
