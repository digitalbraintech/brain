# Deploy status — sub-project A (Azure Deploy Foundation)

## LIVE (2026-06-23) — first real Azure deploy done

A real `pulumi up` provisioned the full stack in subscription `08e2e8fa-…` / tenant
`10b9647a-…`, region `westeurope`, resource group `digitalbrain-rg`. Both apps are healthy.

### Endpoints
- Gateway (external): `https://digitalbrain-api.agreeablefield-fcde995f.westeurope.azurecontainerapps.io`
  - `/health` → `ok`; `/status` → `llmMode=azureopenai`.
- Azure OpenAI: `https://digitalbrainopenaiprod.openai.azure.com/` (chat deployment `chat` = gpt-4o-mini).
- ACR: `digitalbrainacrprod.azurecr.io` (images `digitalbrain-gateway:v3`, `digitalbrain-silo:v3`).

### Resources created (RG `digitalbrain-rg`)
- `azure-native:resources:ResourceGroup` digitalbrain-rg
- `azure-native:storage:StorageAccount` digitalbrainstprod (StorageV2, Standard_LRS, public network access)
- `azure-native:cognitiveservices:Account` digitalbrainopenaiprod (S0) + `Deployment` chat (gpt-4o-mini, GlobalStandard, cap 10)
- `azure-native:keyvault:Vault` digitalbrainkvprod (RBAC)
- `azure-native:operationalinsights:Workspace` digitalbrain-log-prod + `applicationinsights:Component` digitalbrain-ai-prod
- `azure-native:containerregistry:Registry` digitalbrainacrprod (Basic, admin enabled)
- `azure-native:app:ManagedEnvironment` digitalbrain-cae-prod
- `azure-native:app:ContainerApp` digitalbrain-api (gateway, external HTTP 8080) + digitalbrain-jobs (silo, no ingress / worker)
- Database / Cache / Network / EventHubs are intentionally **not** provisioned (services no-op).

### Silo (Orleans) runtime
The silo (`digitalbrain-jobs`) runs as an ingress-less worker: `Orleans Silo started`,
`Finished BecomeActive`, with Azure **Table clustering** (`OrleansSiloInstances`),
**Blob grain storage** (`grainstate`) and **Blob journaling** all initialized from the
injected connection strings. Journal *entries* require neuron activity (firing synapses);
there is no interaction path live yet (gateway `/status` cluster/storage probes are stubbed,
MCP app not deployed), so no journal blobs exist until an interaction path is added.

## How it's wired
- `Program.cs` invokes `InfrastructureDeployer.DeployAsync(settings)` inside `Deployment.RunAsync`.
  Image tag + placeholder mode come from `pulumi config` (`imageTag`, `usePlaceholderImages`).
- The container apps receive (via the Container Apps service) the NeuroOS runtime contract:
  `ConnectionStrings__clustering/grainstate/journal` (one StorageV2 conn string),
  `DigitalBrain__Llm__AzureOpenAIEndpoint/AzureOpenAIKey`, and `Provider=azureopenai`/`Model=chat`/`DIGITALBRAIN_ENV=cloud`.
- The silo self-wires Orleans clustering + grain storage + journal from those connection strings
  (`DigitalBrain.Silo/Program.cs`); it no longer depends on an Aspire AppHost.

## Fixes applied to the vendored DeploymentKit (all pre-existing, surfaced once DeployAsync actually ran)
1. Provision OpenAI in the orchestrator (was never wired); align its naming with `IResourceNamingService`.
2. Inject the runtime contract into the apps: the storage connection string + Azure OpenAI key are Container App
   **secrets** referenced via `SecretRef`; the OpenAI endpoint + `AdditionalEnvironmentVariables` are plain env vars.
3. Register 4 missing green/blue DI services (DI graph was incomplete).
4. Make `Database`/`Network` settings optional (drop `[Required]`); null-guard `NetworkService`.
5. KeyVault: skip the access-policy / `ARM_CLIENT_ID` path under RBAC.
6. Container Apps secrets/env: only wire the Postgres secret + env when a database exists (empty secret value is rejected).
7. Storage: `AllowPublicNetworkAccess` flag (network default action Allow) — required for no-VNet Container Apps.
8. Jobs app: no ingress (Orleans silo is a worker; an HTTP readiness probe it never answers kept the revision unhealthy).
9. Silo image base → `aspnet` (it transitively references `Microsoft.AspNetCore.App`).

## Environment for local `pulumi` (off the user profile)
```sh
export PULUMI_HOME=E:/tools/pulumi-home
export PULUMI_CONFIG_PASSPHRASE=digitalbrain-dryrun
export ARM_SUBSCRIPTION_ID=08e2e8fa-a9bf-4a1a-be54-56664d2c6cc9
export ARM_TENANT_ID=10b9647a-65af-44e0-9e55-d8f9fc93a381   # also AZURE_TENANT_ID
E:/tools/pulumi/pulumi/bin/pulumi.exe login file://E:/tools/pulumi-state
# stack dev. Image push: az acr admin creds via SDK_CONTAINER_REGISTRY_UNAME/PWORD + dotnet publish /t:PublishContainer
```

## Commands
```sh
pulumi up   --stack dev --cwd framework/deploy     # provision / update (INCURS COST)
pulumi config set imageTag <tag>; pulumi config set usePlaceholderImages false
# tear down when done:
pulumi destroy --stack dev --cwd framework/deploy
```

## Follow-ups (not blocking gateway-live)
- Un-stub the gateway `/status` cluster/storage/journal probes (currently return `unknown` / `-1`).
- Add an interaction path (deploy MCP app, or a gateway endpoint) to exercise neurons → then journal-entry survival is demonstrable.
