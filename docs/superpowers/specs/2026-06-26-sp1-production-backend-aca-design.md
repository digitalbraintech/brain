# SP1 — First-cut public backend on Azure Container Apps (Topology B)

**Date:** 2026-06-26
**Status:** Design approved, pending spec review
**Scope:** One sub-project of the larger "go to production" push. Decomposition below; this spec covers **SP1 only**.

## Goal

Stand up the NeuroOS **kernel** as a publicly reachable backend on Azure Container Apps (ACA) with a
browser-capable **gRPC-Web** endpoint, and prove the full chain works: the Flutter web app (run locally
against the cloud host) makes a live gRPC-Web call, loads the live timeline, and creates a brain that shows
activity. Domain attach and Pages wiring are explicitly out of scope (SP2).

This is the **de-risking first cut**: deploy to the default `*.azurecontainerapps.io` host so the
distributed runtime (gRPC-Web + CORS + Orleans clustering on real Azure storage) is validated *before* any
DNS/registrar changes.

## Topology decision (settled)

**Topology B — two origins, independent pipelines.**

- `app/` → GitHub Pages at `digitalbrain.tech` (existing `deploy-flutter-web.yml`, unchanged in SP1). The
  web client points its gRPC channel at a remote api host (the cloud kernel).
- `brain/` → ACA, **external** ingress, serving gRPC-Web (browser) + native gRPC. The kernel does **not**
  serve the Flutter bundle (`DIGITALBRAIN_WEBROOT` stays unset). Cross-origin, so the kernel must emit CORS.

Rejected alternatives and why:
- *Topology A (kernel serves the bundle, one origin):* would require the kernel image to contain the app's
  web bundle, coupling two separate repos' CI via a cross-repo trigger. B keeps each repo's pipeline
  independent, which matches the "both repos auto-deploy via their own GH Actions" requirement.

## Where the larger effort is decomposed

Each gets its own spec → plan → implementation cycle. **This spec is SP1.**

- **SP1 (this doc)** — First-cut public backend on ACA: external gRPC-Web ingress, CORS, the
  checkpoint-key boot-crash fix, un-break `deploy.yml`, app `endpoint.dart` fix, verify end-to-end on the
  default ACA host.
- **SP2** — Attach `api.digitalbrain.tech` custom domain + managed cert; flip the Pages build's
  `KERNEL_ENDPOINT` to it; drop the dangling `api`/`asuid.api` DNS records.
- **SP3** — Hardening for real use: Key Vault for secrets, Stripe enablement decision, scale/cost tuning,
  E2E smoke in CI + the `silo`→`kernel` E2E fixture name fix.

## Findings that shape this spec

1. **The container crashes on boot today.** Bucket A (A5) made `AddKernelSecurity` fail fast when
   `environment.IsProduction()` and `DigitalBrain:Checkpoint:Key` is missing
   (`DigitalBrain.Kernel/Kernel/KernelServices.cs`). ACA leaves `ASPNETCORE_ENVIRONMENT` unset → ASP.NET
   Core defaults it to **Production** → the current Pulumi program (which sets `DIGITALBRAIN_ENV=cloud` but
   no checkpoint key) would fail-fast. The checkpoint-key secret is therefore **mandatory**, not optional.
2. **Serving is single-pipeline, single-port.** The kernel serves static files + gRPC-Web + native gRPC
   from one `WebApplication` (`DigitalBrain.Kernel/Program.cs`). Browser gRPC-Web runs over **HTTP/1.1**, so
   the ACA ingress must target the **`Http1AndHttp2`** Kestrel port (driven by `DIGITALBRAIN_WEB_PORT`), not
   the Http2-only port the current internal ingress uses.
3. **`deploy.yml` is broken.** It publishes `DigitalBrain.Silo/DigitalBrain.Silo.csproj`, which no longer
   exists (renamed to `DigitalBrain.Kernel`). The workflow would fail at the publish step. State backend in
   CI (`azblob://pulumi-state`) does not match the live stack (local file backend, per `deploy/DEPLOY-STATUS.md`).
4. **The app web client cannot point at a remote host.** `app/lib/grpc/endpoint.dart` lines 33–35 force
   `Uri.base.host` on web **even when `KERNEL_ENDPOINT` is set**, so it can never reach a different api host.

## Changes

### `brain/` — Pulumi `deploy/Program.cs`

- `Ingress.External = true`; `Transport = "Auto"`; `TargetPort` aligned to the kernel's HTTP/1.1+HTTP/2
  port. Set `DIGITALBRAIN_WEB_PORT` to that port so Kestrel binds it with `Http1AndHttp2`.
- Add a `DigitalBrain:Checkpoint:Key` ACA secret. Value is a base64 AES key stored as a **Pulumi secret
  config** (`Pulumi.dev.yaml`, encrypted) — stable across restarts so checkpoints stay decryptable. Injected
  as env `DigitalBrain__Checkpoint__Key` via `SecretRef`.
- Add env `ASPNETCORE_ENVIRONMENT=Production` (explicit; removes the implicit-default surprise). Keep
  `DIGITALBRAIN_ENV=cloud`.
- Leave `DIGITALBRAIN_WEBROOT` **unset** (Topology B — kernel does not serve the bundle).
- Keep `MinReplicas = 1` for the first cut (cheaper; Orleans Azure Table clustering works single-replica).
  HA 3-replica tuning is SP3.
- The `digitalbrain-silo` image-repository name may stay as-is to avoid state churn; cosmetic rename is SP3.

### `brain/` — kernel `Program.cs`

- Add `UseCors` **before** `UseGrpcWeb`, allowing origin `https://digitalbrain.tech`, exposing the gRPC-Web
  response headers (`grpc-status`, `grpc-message`, `grpc-status-details-bin`). The allowed-origins list is
  read from configuration (e.g. `DigitalBrain:Cors:AllowedOrigins`), not hardcoded, so SP2 can add the api
  host / localhost dev origins without code changes.
- Confirm the cloud Kestrel binding serves gRPC-Web on the `Http1AndHttp2` port that ACA targets (verify
  against `DIGITALBRAIN_WEB_PORT`; avoid a double-bind with the default ASPNETCORE port).

### `brain/` — `.github/workflows/deploy.yml`

- `DigitalBrain.Silo/DigitalBrain.Silo.csproj` → `DigitalBrain.Kernel/DigitalBrain.Kernel.csproj`; align the
  `ContainerRepository` value with `deploy/Program.cs`'s `SiloImageRepository`.
- Resolve the Pulumi state-backend mismatch: point CI at the same backend as the live stack (or migrate the
  stack to `azblob` and document it). Whichever is chosen must be stated in `deploy/DEPLOY-STATUS.md`.
- Required repo secrets/vars (set by the user, out of band): `DOCKERHUB_USERNAME` (var),
  `DOCKERHUB_TOKEN` (secret), Azure OIDC (`AZURE_CLIENT_ID`/`TENANT_ID`/`SUBSCRIPTION_ID` vars),
  `PULUMI_PASSPHRASE` (secret). The workflow assumes these exist; SP1 does not create them.

### `app/` — `lib/grpc/endpoint.dart`

- Fix the web path so a build-time `KERNEL_ENDPOINT` (absolute URL) **wins on web** instead of being
  overridden by `Uri.base.host`. When `KERNEL_ENDPOINT` is absent on web, keep the current same-origin
  fallback. The Pages workflow wiring of `KERNEL_ENDPOINT` is SP2.

## Verification / acceptance

1. **Build + boot guard (CI, pre-deploy):** the kernel image builds and the container boots **without** the
   checkpoint-key fail-fast. The `deploy.yml` `dotnet test` step is green (high-severity suite per the repo's
   verification ritual).
2. **Live chain (manual, against the deployed `*.azurecontainerapps.io` host):** run the Flutter web app
   locally with `KERNEL_ENDPOINT` pointed at the cloud host; a browser-origin gRPC-Web call (`listBrains` /
   `watchActivity`) returns successfully (CORS satisfied); the live timeline loads; creating a brain shows
   activity.
3. **Repo ritual:** `dotnet build`, relevant `dotnet test`, `aspire doctor` green before commit, per
   `brain/AGENTS.md`.

## Out of scope (do NOT do in SP1)

- Attaching `api.digitalbrain.tech` or any DNS/registrar change (SP2).
- Wiring `KERNEL_ENDPOINT` into the Pages workflow / changing `deploy-flutter-web.yml` (SP2).
- Key Vault, Stripe enablement, 3-replica HA scaling, E2E-in-CI, the `silo`→`kernel` E2E fixture fix (SP3).
- Retiring or redirecting GitHub Pages (Topology B keeps it).

## Risks / open items

- **ACA ingress + gRPC-Web:** verify (via Context7 / Aspire+ACA docs) that `Transport=Auto` external ingress
  correctly carries browser gRPC-Web over HTTP/1.1 to the targeted Kestrel port. If `Auto` misbehaves, fall
  back to `Http` transport.
- **Kestrel double-bind:** setting `DIGITALBRAIN_WEB_PORT` to the same port ASP.NET already binds via
  `ASPNETCORE_URLS` could conflict; confirm a single clean `Http1AndHttp2` binding on the ACA target port.
- **Checkpoint key rotation:** the key is fixed in Pulumi config for now; rotation/Key Vault is SP3.
- **CORS origin during first-cut verification:** acceptance runs the app from `localhost`, not
  `digitalbrain.tech`. The allowed-origins config must include the local dev origin used for verification
  (kept in config, removed/locked down in SP2/SP3).
