# 10 — Deploying to Azure

One script deploys the whole platform to your subscription. **No local Docker needed**: images are built in Azure with `az acr build`.

## Local → Azure mapping

| Local (docker-compose) | Azure | Why |
|---|---|---|
| Containers | **Azure Container Apps** (ACA) | Serverless containers on managed Kubernetes (KEDA + Envoy). Scale to zero. No cluster to run |
| RabbitMQ | **Azure Service Bus** (Standard) | Managed broker. MassTransit switches by config: `Messaging__Transport=AzureServiceBus` |
| MongoDB | **Cosmos DB for MongoDB** (serverless) | Same driver/code, pay-per-request |
| SQL Server | **Azure SQL Database** (2 × Basic) | One DB per service, still |
| Logs | **Log Analytics** | ACA streams console logs here |
| Image registry | **Azure Container Registry** (Basic) | |

*Why not AKS?* AKS is the right answer when you need full Kubernetes (custom operators, service mesh, node control) and the `infra/k8s` manifests deploy there unchanged. For a demo, ACA gives the same container model with almost no ops and near-zero idle cost.

## Deploy

```bash
az login
az account set --subscription "<your subscription>"
export SQL_ADMIN_PASSWORD='Choose-A-Strong-Passw0rd!'   # 8+ chars, upper, lower, digit, symbol
./infra/azure/deploy.sh                                  # defaults: rg-basecamp in uksouth
```

What it does (`infra/azure/deploy.sh` + `main.bicep`):
1. Creates the resource group and registers resource providers.
2. **Phase 1:** Bicep with `deployApps=false` creates the registry, Service Bus, Cosmos, SQL and the Container Apps environment.
3. **Phase 2:** `az acr build` builds all 6 images in the cloud (the portal is built with the gateway's public URL baked in).
4. **Phase 3:** Bicep with `deployApps=true` creates the container apps. Only `gateway` and `portal` have **external** ingress. Catalog, Inventory and Orders are internal-only, reachable by app name inside the environment.

Output: the gateway URL (e.g. `https://basecamp-gateway.<random>.uksouth.azurecontainerapps.io`) and the portal URL.

Point the handheld app at it:
```bash
cd mobile && EXPO_PUBLIC_API_URL=https://basecamp-gateway.<...>.azurecontainerapps.io npx expo start
```

## Cost (rough, UK South, idle demo)
| Resource | Approx. |
|---|---|
| Service Bus Standard | ~£8/month |
| Azure SQL Basic × 2 | ~£4/month each |
| Container Registry Basic | ~£4/month |
| Cosmos DB serverless | pennies at demo traffic |
| Container Apps | free monthly grant covers a light demo. Inventory, Orders and the ETL keep 1 replica warm so they keep consuming messages |
| Log Analytics | small, pay per GB |

**Expect roughly £25–35/month if left running.** Tear it down when you've finished:
```bash
az group delete -n rg-basecamp --yes --no-wait
```
Check current pricing in the Azure calculator. These are estimates.

## Troubleshooting
```bash
az containerapp logs show -g rg-basecamp -n basecamp-inventory --follow
az containerapp revision list -g rg-basecamp -n basecamp-gateway -o table
```
- **First request slow / 503**: apps scaled to zero are waking up. Retry after about 20s.
- **SQL login failures**: check the password meets complexity rules. The firewall rule `AllowAzureServices` must exist.
- **Service Bus errors at startup**: MassTransit creates topics on first run and needs the Manage claim (`RootManageSharedAccessKey`). Fine for a demo. In production, pre-provision the topology and use least-privilege keys or **managed identity**.

## Production hardening
- **Managed identities** instead of connection strings and ACR admin user (Service Bus, SQL and ACR all support Entra ID auth).
- **Key Vault** references for any remaining secrets.
- **Application Insights** + OpenTelemetry for distributed tracing across HTTP and Service Bus.
- **Private networking**: VNet-integrated ACA environment, private endpoints for SQL, Cosmos and Service Bus.
- **Front Door / API Management** in front of the gateway for WAF, rate limiting and a developer portal.
- **Pipeline**: Jenkins (or GitHub Actions / Azure DevOps) running `az deployment group what-if` on PRs and deploying Bicep on merge.
