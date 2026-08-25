#!/usr/bin/env bash
#
# QuotesApi — Azure Monitor / Key Vault / alert provisioning.
#
# NOT EXECUTED. Azure CLI ("az") was not available in the environment
# this was authored in, so none of these resources have been created.
# This script is idempotent (checks before creating) and safe to run
# from a machine with the Azure CLI installed and `az login` completed.
#
# It never hardcodes a secret value: the Application Insights
# connection string is read from the resource Azure just created and
# written straight into Key Vault; it is never printed or persisted
# to a file by this script.
#
# Required: set ALERT_EMAIL before running — this script will not
# invent an address.
#
#   ALERT_EMAIL="you@example.com" ./scripts/setup-azure-monitor.sh
#
set -euo pipefail

RESOURCE_GROUP="${RESOURCE_GROUP:-ThinkSchool-Day4}"
LOCATION="${LOCATION:-eastus}"
LOG_ANALYTICS_WORKSPACE="${LOG_ANALYTICS_WORKSPACE:-quotes-api-workspace}"
APP_INSIGHTS_NAME="${APP_INSIGHTS_NAME:-quotes-api-insights}"
KEY_VAULT_NAME="${KEY_VAULT_NAME:-quotes-api-kv-$RANDOM}"
ACTION_GROUP_NAME="${ACTION_GROUP_NAME:-quotes-api-oncall}"
ALERT_NAME="${ALERT_NAME:-quotes-api-post-quotes-slow-response}"
SECRET_NAME="ApplicationInsightsConnectionString"

if [[ -z "${ALERT_EMAIL:-}" ]]; then
  echo "ERROR: ALERT_EMAIL is not set. Refusing to invent an email address." >&2
  echo "Re-run as: ALERT_EMAIL=\"you@example.com\" $0" >&2
  exit 1
fi

echo "== Subscription context =="
az account show --output table

echo "== Resource group: $RESOURCE_GROUP =="
if az group show --name "$RESOURCE_GROUP" &>/dev/null; then
  echo "Already exists — skipping creation."
else
  az group create --name "$RESOURCE_GROUP" --location "$LOCATION"
fi

echo "== Log Analytics workspace: $LOG_ANALYTICS_WORKSPACE =="
if az monitor log-analytics workspace show \
    --resource-group "$RESOURCE_GROUP" \
    --workspace-name "$LOG_ANALYTICS_WORKSPACE" &>/dev/null; then
  echo "Already exists — skipping creation."
else
  az monitor log-analytics workspace create \
    --resource-group "$RESOURCE_GROUP" \
    --workspace-name "$LOG_ANALYTICS_WORKSPACE" \
    --location "$LOCATION"
fi

WORKSPACE_ID=$(az monitor log-analytics workspace show \
  --resource-group "$RESOURCE_GROUP" \
  --workspace-name "$LOG_ANALYTICS_WORKSPACE" \
  --query id --output tsv)

echo "== Application Insights: $APP_INSIGHTS_NAME =="
if az monitor app-insights component show \
    --resource-group "$RESOURCE_GROUP" \
    --app "$APP_INSIGHTS_NAME" &>/dev/null; then
  echo "Already exists — skipping creation."
else
  az monitor app-insights component create \
    --resource-group "$RESOURCE_GROUP" \
    --app "$APP_INSIGHTS_NAME" \
    --location "$LOCATION" \
    --workspace "$WORKSPACE_ID" \
    --application-type web
fi

APP_INSIGHTS_ID=$(az monitor app-insights component show \
  --resource-group "$RESOURCE_GROUP" \
  --app "$APP_INSIGHTS_NAME" \
  --query id --output tsv)

CONNECTION_STRING=$(az monitor app-insights component show \
  --resource-group "$RESOURCE_GROUP" \
  --app "$APP_INSIGHTS_NAME" \
  --query connectionString --output tsv)

echo "== Key Vault: $KEY_VAULT_NAME =="
if az keyvault show --name "$KEY_VAULT_NAME" &>/dev/null; then
  echo "Already exists — skipping creation."
else
  az keyvault create \
    --resource-group "$RESOURCE_GROUP" \
    --name "$KEY_VAULT_NAME" \
    --location "$LOCATION" \
    --enable-rbac-authorization true
fi

VAULT_URI=$(az keyvault show --name "$KEY_VAULT_NAME" --query properties.vaultUri --output tsv)

echo "== Storing $SECRET_NAME in Key Vault (value not printed) =="
az keyvault secret set \
  --vault-name "$KEY_VAULT_NAME" \
  --name "$SECRET_NAME" \
  --value "$CONNECTION_STRING" \
  --output none

echo "== RBAC: grant current signed-in user 'Key Vault Secrets User' =="
# For local development against the real vault via `az login` /
# DefaultAzureCredential. Replace with the deployed app's managed
# identity principal ID once there is a hosting target.
CURRENT_USER_OBJECT_ID=$(az ad signed-in-user show --query id --output tsv)
KEY_VAULT_ID=$(az keyvault show --name "$KEY_VAULT_NAME" --query id --output tsv)

az role assignment create \
  --assignee-object-id "$CURRENT_USER_OBJECT_ID" \
  --assignee-principal-type User \
  --role "Key Vault Secrets User" \
  --scope "$KEY_VAULT_ID" \
  --output none || echo "Role assignment may already exist — continuing."

echo "== Action group: $ACTION_GROUP_NAME (email: [redacted from script output]) =="
if az monitor action-group show \
    --resource-group "$RESOURCE_GROUP" \
    --name "$ACTION_GROUP_NAME" &>/dev/null; then
  echo "Already exists — skipping creation."
else
  az monitor action-group create \
    --resource-group "$RESOURCE_GROUP" \
    --name "$ACTION_GROUP_NAME" \
    --short-name "quotesoncall" \
    --action email oncall "$ALERT_EMAIL"
fi

ACTION_GROUP_ID=$(az monitor action-group show \
  --resource-group "$RESOURCE_GROUP" \
  --name "$ACTION_GROUP_NAME" \
  --query id --output tsv)

echo "== Alert rule: $ALERT_NAME =="
# Average duration of POST /api/quotes over a 5-minute window,
# evaluated every 5 minutes. requests.duration in Application
# Insights is already in milliseconds — no unit conversion needed.
# auto-mitigate resolves the alert once traffic drops back under
# the threshold, so it stays a signal that action is required
# rather than a standing page.
if az monitor scheduled-query show \
    --resource-group "$RESOURCE_GROUP" \
    --name "$ALERT_NAME" &>/dev/null; then
  echo "Already exists — skipping creation."
else
  az monitor scheduled-query create \
    --resource-group "$RESOURCE_GROUP" \
    --name "$ALERT_NAME" \
    --description "Average response time of POST /api/quotes exceeded 500ms over 5 minutes" \
    --scopes "$APP_INSIGHTS_ID" \
    --condition "avg AvgDurationMs > 500" \
    --condition-query AvgDurationMs='requests | where name contains "POST /api/quotes" | summarize AvgDurationMs = avg(duration)' \
    --window-size 5m \
    --evaluation-frequency 5m \
    --severity 2 \
    --auto-mitigate true \
    --action-groups "$ACTION_GROUP_ID"
fi

echo "== Done =="
echo "Vault URI to put in configuration (KeyVault:VaultUri / KeyVault__VaultUri):"
echo "$VAULT_URI"
