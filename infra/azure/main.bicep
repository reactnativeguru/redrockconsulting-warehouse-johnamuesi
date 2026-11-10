// ---------------------------------------------------------------------------
// Azure deployment (Infrastructure as Code, Bicep).
//
// Why Azure Container Apps (ACA) instead of AKS for this demo?
//   ACA = Kubernetes under the hood (KEDA + Envoy), but no cluster to manage and
//   a free monthly allowance. AKS is the right call when you need full k8s control
//   (the infra/k8s manifests would deploy to AKS unchanged).
//
// Managed equivalents of our local containers:
//   RabbitMQ   -> Azure Service Bus (Standard: topics needed for publish/subscribe)
//                 MassTransit switches transport by config: Messaging__Transport=AzureServiceBus
//   MongoDB    -> Azure Cosmos DB for MongoDB (serverless, pay-per-request)
//   SQL Server -> Azure SQL Database (two Basic DBs: one per service)
//
// Two-phase deploy (see deploy.sh):
//   1. deployApps=false -> create registry + data/messaging resources
//   2. build images INTO the registry with `az acr build`
//   3. deployApps=true  -> create the container apps
// ---------------------------------------------------------------------------
targetScope = 'resourceGroup'

@description('Short prefix for resource names (lowercase letters/numbers).')
@maxLength(10)
param prefix string = 'basecamp'

param location string = resourceGroup().location

@description('Set false for the first run (images do not exist yet).')
param deployApps bool = true

param imageTag string = 'latest'

@secure()
@description('Azure SQL admin password.')
param sqlAdminPassword string

param sqlAdminLogin string = 'basecampadmin'

var suffix = uniqueString(resourceGroup().id)
var acrName = '${prefix}acr${suffix}'

// ---------------- observability ----------------
resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${prefix}-logs'
  location: location
  properties: { sku: { name: 'PerGB2018' }, retentionInDays: 30 }
}

// ---------------- container registry ----------------
resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: acrName
  location: location
  sku: { name: 'Basic' }
  // Admin user keeps the demo simple. Production: managed identity + AcrPull role.
  properties: { adminUserEnabled: true }
}

// ---------------- messaging ----------------
resource serviceBus 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: '${prefix}-sb-${suffix}'
  location: location
  sku: { name: 'Standard', tier: 'Standard' }
}

// MassTransit creates its own topics/subscriptions/queues on startup, so it
// needs Manage rights. (Production: pre-provision topology, use Send/Listen only.)
resource sbAuth 'Microsoft.ServiceBus/namespaces/AuthorizationRules@2022-10-01-preview' existing = {
  parent: serviceBus
  name: 'RootManageSharedAccessKey'
}

// ---------------- MongoDB (Cosmos DB) ----------------
resource cosmos 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' = {
  name: '${prefix}-cosmos-${suffix}'
  location: location
  kind: 'MongoDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    apiProperties: { serverVersion: '7.0' }
    capabilities: [{ name: 'EnableServerless' }]
    locations: [{ locationName: location, failoverPriority: 0 }]
    consistencyPolicy: { defaultConsistencyLevel: 'Session' }
  }
}

// ---------------- SQL ----------------
resource sql 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: '${prefix}-sql-${suffix}'
  location: location
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion: '1.2'
  }
}

resource sqlAllowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sql
  name: 'AllowAzureServices'
  properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
}

resource inventoryDb 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sql
  name: 'Inventory'
  location: location
  sku: { name: 'Basic', tier: 'Basic' }
}

resource ordersDb 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sql
  name: 'Orders'
  location: location
  sku: { name: 'Basic', tier: 'Basic' }
}

// ---------------- Container Apps environment ----------------
resource env 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${prefix}-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

var sqlConn = 'Server=tcp:${sql.properties.fullyQualifiedDomainName},1433;User Id=${sqlAdminLogin};Password=${sqlAdminPassword};Encrypt=True;TrustServerCertificate=False'

// Secrets every .NET service receives (stored as Container Apps secrets, not plain env).
var commonSecrets = [
  { name: 'acr-password', value: acr.listCredentials().passwords[0].value }
  { name: 'servicebus', value: sbAuth.listKeys().primaryConnectionString }
  { name: 'mongo', value: cosmos.listConnectionStrings().connectionStrings[0].connectionString }
  { name: 'inventory-db', value: '${sqlConn};Database=Inventory' }
  { name: 'orders-db', value: '${sqlConn};Database=Orders' }
]

var commonEnv = [
  { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
  { name: 'Messaging__Transport', value: 'AzureServiceBus' }
  { name: 'ConnectionStrings__ServiceBus', secretRef: 'servicebus' }
]

// name -> extra env + ingress mode. Only the gateway is public ("external").
// NOTE: a for-loop's source array must be known at compile time, so nothing in
// here may reference runtime values (like the environment's domain) - those are
// added inside the loop body instead.
var apps = [
  {
    name: 'catalog'
    external: false
    env: [ { name: 'ConnectionStrings__CatalogDb', secretRef: 'mongo' } ]
  }
  {
    name: 'inventory'
    external: false
    env: [ { name: 'ConnectionStrings__InventoryDb', secretRef: 'inventory-db' } ]
  }
  {
    name: 'orders'
    external: false
    env: [
      { name: 'ConnectionStrings__OrdersDb', secretRef: 'orders-db' }
      { name: 'Services__CatalogUrl', value: 'http://${prefix}-catalog/' }
    ]
  }
  {
    name: 'gateway'
    external: true
    env: [
      // Inside a Container Apps environment, apps reach each other by app name.
      { name: 'ReverseProxy__Clusters__catalog__Destinations__d1__Address', value: 'http://${prefix}-catalog/' }
      { name: 'ReverseProxy__Clusters__inventory__Destinations__d1__Address', value: 'http://${prefix}-inventory/' }
      { name: 'ReverseProxy__Clusters__orders__Destinations__d1__Address', value: 'http://${prefix}-orders/' }
      { name: 'Cors__Origins__1', value: 'http://localhost:3000' }
      { name: 'Cors__Origins__2', value: 'http://localhost:8081' }
    ]
  }
]

// Runtime-dependent gateway setting (allowed in a var used only inside properties).
var gatewayRuntimeEnv = [
  { name: 'Cors__Origins__0', value: 'https://${prefix}-portal.${env.properties.defaultDomain}' }
]

resource containerApps 'Microsoft.App/containerApps@2024-03-01' = [for app in apps: if (deployApps) {
  name: '${prefix}-${app.name}'
  location: location
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      ingress: { external: app.external, targetPort: 8080, transport: 'auto' }
      registries: [{ server: acr.properties.loginServer, username: acr.listCredentials().username, passwordSecretRef: 'acr-password' }]
      secrets: commonSecrets
    }
    template: {
      containers: [
        {
          name: app.name
          image: '${acr.properties.loginServer}/${app.name}:${imageTag}'
          env: concat(commonEnv, app.env, app.name == 'gateway' ? gatewayRuntimeEnv : [])
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          probes: [
            { type: 'Readiness', httpGet: { path: '/health', port: 8080 }, initialDelaySeconds: 10 }
          ]
        }
      ]
      // Scale to zero when idle = near-zero cost for a demo. (Set minReplicas: 1
      // for services with consumers that must keep listening to the bus.)
      scale: { minReplicas: app.name == 'inventory' || app.name == 'orders' ? 1 : 0, maxReplicas: 3 }
    }
  }
}]

// The ETL worker has no ingress. On Azure the "folder" would really be Blob Storage
// (or Azure Files); this container runs it against its local filesystem as a demo.
resource etlApp 'Microsoft.App/containerApps@2024-03-01' = if (deployApps) {
  name: '${prefix}-epos-etl'
  location: location
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      registries: [{ server: acr.properties.loginServer, username: acr.listCredentials().username, passwordSecretRef: 'acr-password' }]
      secrets: commonSecrets
    }
    template: {
      containers: [
        {
          name: 'epos-etl'
          image: '${acr.properties.loginServer}/epos-etl:${imageTag}'
          env: concat(commonEnv, [{ name: 'DOTNET_ENVIRONMENT', value: 'Production' }])
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
}

resource portalApp 'Microsoft.App/containerApps@2024-03-01' = if (deployApps) {
  name: '${prefix}-portal'
  location: location
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      ingress: { external: true, targetPort: 80 }
      registries: [{ server: acr.properties.loginServer, username: acr.listCredentials().username, passwordSecretRef: 'acr-password' }]
      secrets: [{ name: 'acr-password', value: acr.listCredentials().passwords[0].value }]
    }
    template: {
      containers: [
        {
          name: 'portal'
          image: '${acr.properties.loginServer}/admin-portal:${imageTag}'
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
        }
      ]
      scale: { minReplicas: 0, maxReplicas: 2 }
    }
  }
}

output acrName string = acr.name
output acrLoginServer string = acr.properties.loginServer
output environmentDomain string = env.properties.defaultDomain
output gatewayUrl string = 'https://${prefix}-gateway.${env.properties.defaultDomain}'
output portalUrl string = 'https://${prefix}-portal.${env.properties.defaultDomain}'
