# Deploy status — sub-project A (Azure Deploy Foundation)

## LIVE (2026-06-23) — Elon Algorithm Pass 1 applied

The vendored `DeploymentKit` (308 files / ~33k LOC) was deleted and replaced by a single ~237-line
`Pulumi.AzureNative` program (`Program.cs`) that provisions **only** what the runtime uses. A real
`pulumi up` on stack `dev` reduced the live footprint from 17 to 10 Pulumi resources (8 real Azure
resources). Both kept resources and the silo are healthy.

### Resources (RG `digitalbrain-rg`, westeurope)
- `azure-native:resources:ResourceGroup` digitalbrain-rg
- `azure-native:storage:StorageAccount` digitalbrainstprod (StorageV2, Standard_LRS, public network access)
- `azure-native:cognitiveservices:Account` digitalbrainopenaiprod (S0) + `Deployment` chat (gpt-4o-mini, GlobalStandard, cap 10)
- `azure-native:operationalinsights:Workspace` digitalbrain-log-prod + `applicationinsights:Component` digitalbrain-ai-prod
- `azure-native:app:ManagedEnvironment` digitalbrain-cae-prod
- `azure-native:app:ContainerApp` digitalbrain-jobs (silo, internal Http2 ingress on port 8080 / gRPC gateway)

### Deleted in Pass 1
Key Vault (`digitalbrainkvprod`), ACR (`digitalbrainacrprod`), the gateway app (`digitalbrain-api`,
external HTTP — `api.digitalbrain.tech` no longer served), and the empty DeploymentKit component wrappers
(Network / Database / app-registry / app-runtime). The undeployed MCP "Bot" slot and the two-phase
placeholder-image deploy are gone.

### Image registry
The silo image lives in **public Docker Hub**: `docker.io/vhorbachov/digitalbrain-silo:v4`
(built with `dotnet publish -t:PublishContainer --os linux --arch x64`, pushed via the docker CLI). ACA currently runs revision `digitalbrain-jobs--0000005` on the previous image (v3). Image **v4** (which adds the co-hosted gRPC gateway + needs the internal Http2 ingress) is built and pushed to Docker Hub but **not yet deployed** — apply with `pulumi up` to roll it out.

### Silo (Orleans) runtime
The silo now exposes an internal-only Http2 ingress on port 8080 serving the gRPC `DigitalBrainGateway` (Ask/Fire/Timeline/Health).

The silo (`digitalbrain-jobs`) runs with Azure **Table clustering**
(`OrleansSiloInstances`), **Blob grain storage** (`grainstate`) and **Blob journaling**, all wired from
the injected `ConnectionStrings__clustering/grainstate/journal` (one StorageV2 connection string, a
Container App secret) plus `DigitalBrain__Llm__AzureOpenAIEndpoint/AzureOpenAIKey` and
`Provider=azureopenai`/`Model=chat`/`DIGITALBRAIN_ENV=cloud`.

## How it's wired
- `Program.cs` (`Deployment.RunAsync(Provision)`) declares each resource directly with `Pulumi.AzureNative`.
  Kept resources reuse their original logical names so they match the existing state; the env + silo carry
  an `Alias { ParentUrn = ...DeploymentKitApp::digitalbrain-app-runtime-prod }` so Pass 1 re-parented them
  to the stack root **in place** (no destructive replace).
- Image tag comes from `pulumi config imageTag` (default in `Pulumi.dev.yaml`) or `DIGITALBRAIN_IMAGE_TAG`.
- No Key Vault, ACR, gateway, monitoring agents, or placeholder phase.

## Environment for local `pulumi` (off the user profile)
```sh
export PULUMI_HOME=E:/tools/pulumi-home
export PULUMI_CONFIG_PASSPHRASE=digitalbrain-dryrun
export ARM_SUBSCRIPTION_ID=08e2e8fa-a9bf-4a1a-be54-56664d2c6cc9
export ARM_TENANT_ID=10b9647a-65af-44e0-9e55-d8f9fc93a381   # also AZURE_TENANT_ID
E:/tools/pulumi/pulumi/bin/pulumi.exe login file://E:/tools/pulumi-state
# stack dev. Kernel image: dotnet publish DigitalBrain.Kernel -t:PublishContainer --os linux --arch x64
#   -p:ContainerRepository=vhorbachov/digitalbrain-silo -p:ContainerImageTag=<tag>  then docker push.
```

## Commands
```sh
pulumi preview --stack dev --cwd framework/deploy     # no-spend plan
pulumi up      --stack dev --cwd framework/deploy     # provision / update (INCURS COST)
pulumi destroy --stack dev --cwd framework/deploy     # tear down
```

## Follow-ups

### (2026-06-26) SP1 CI standardization
- **Ingress:** The silo now serves gRPC-Web externally on the public ingress (ACA Http2 endpoint). The gateway is gone; all traffic flows through the silo's co-hosted gRPC Gateway.
- **CI state backend:** CI (`deploy.yml`) now explicitly declares `cloud-url: azblob://pulumi-state`. Stack state must be migrated from the local file backend to Azure Blob Storage; this is a runbook step (Task 5).
- **Secret requirement:** `DigitalBrain:Checkpoint:Key` (ECDSA signing key for pack trust) is now required in the Container App secrets, added in Task 3.

### Pre-SP1 (2026-06-23)
- **DNS:** remove the dangling `api` / `asuid.api` records at the registrar — the gateway they pointed to is deleted.
- **CI (`deploy.yml`):** flipped to `push:[main]` → `pulumi up`, but it targets `azblob://pulumi-state`
  while the live stack is on the **local file backend**. Migrate the stack state to azblob (or point CI at
  the same backend) and add repo `DOCKERHUB_USERNAME` (var) + `DOCKERHUB_TOKEN` (secret) before enabling auto-deploy.
- No interaction path is live yet (silo is a worker; the gateway is gone) — journal entries require neuron activity.
