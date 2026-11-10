#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# Deploy Basecamp Retail to your Azure subscription.
#
# Prereqs: Azure CLI (`az login`), nothing else - images are built IN Azure
# with `az acr build`, so you don't even need Docker locally.
#
#   export SQL_ADMIN_PASSWORD='Choose-A-Strong-Passw0rd!'
#   ./infra/azure/deploy.sh                 # defaults: rg-basecamp, uksouth
#   RG=my-rg LOCATION=westeurope ./infra/azure/deploy.sh
#
# Tear down (stops all costs):  az group delete -n rg-basecamp --yes --no-wait
# ---------------------------------------------------------------------------
set -euo pipefail

RG="${RG:-rg-basecamp}"
LOCATION="${LOCATION:-uksouth}"
PREFIX="${PREFIX:-basecamp}"
TAG="${TAG:-$(date +%Y%m%d%H%M)}"
: "${SQL_ADMIN_PASSWORD:?Set SQL_ADMIN_PASSWORD first}"

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
BICEP="$ROOT/infra/azure/main.bicep"

echo "==> Resource group $RG ($LOCATION)"
az group create -n "$RG" -l "$LOCATION" -o none

# Container Apps / Service Bus etc. providers (first use in a subscription).
for ns in Microsoft.App Microsoft.OperationalInsights Microsoft.ContainerRegistry Microsoft.ServiceBus Microsoft.DocumentDB Microsoft.Sql; do
  az provider register -n "$ns" -o none
done

echo "==> Phase 1: registry, Service Bus, Cosmos DB (Mongo), Azure SQL, Container Apps env"
ACR=$(az deployment group create -g "$RG" -f "$BICEP" \
  -p prefix="$PREFIX" deployApps=false sqlAdminPassword="$SQL_ADMIN_PASSWORD" \
  --query properties.outputs.acrName.value -o tsv)
DOMAIN=$(az containerapp env show -g "$RG" -n "$PREFIX-env" --query properties.defaultDomain -o tsv)
GATEWAY_URL="https://$PREFIX-gateway.$DOMAIN"

echo "==> Phase 2: building images in ACR ($ACR), tag $TAG"
build() { # name project
  local entry; entry="$(basename "$2" .csproj).dll"
  az acr build -r "$ACR" -t "$1:$TAG" --build-arg PROJECT="$2" --build-arg ENTRY="$entry" "$ROOT/backend" -o none
  echo "    built $1"
}
build catalog   src/Services/Catalog/Catalog.Api/Catalog.Api.csproj
build inventory src/Services/Inventory/Inventory.Api/Inventory.Api.csproj
build orders    src/Services/Orders/Orders.Api/Orders.Api.csproj
build epos-etl  src/Services/Etl/Epos.Etl/Epos.Etl.csproj
build gateway   src/Gateway/Gateway/Gateway.csproj
az acr build -r "$ACR" -t "admin-portal:$TAG" --build-arg API_BASE_URL="$GATEWAY_URL" "$ROOT/web" -o none
echo "    built admin-portal"

echo "==> Phase 3: container apps"
az deployment group create -g "$RG" -f "$BICEP" \
  -p prefix="$PREFIX" deployApps=true imageTag="$TAG" sqlAdminPassword="$SQL_ADMIN_PASSWORD" \
  --query properties.outputs -o table

cat <<EOF

Done.
  API gateway:   $GATEWAY_URL          (try $GATEWAY_URL/api/products)
  Admin portal:  https://$PREFIX-portal.$DOMAIN
  Handheld app:  EXPO_PUBLIC_API_URL=$GATEWAY_URL npx expo start   (in /mobile)

First requests may take ~20s while apps scale up from zero.
EOF
