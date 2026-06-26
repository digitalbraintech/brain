# SP1 — First-cut public backend on ACA Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the NeuroOS kernel a publicly reachable Azure Container Apps backend with a browser-capable gRPC-Web endpoint, and prove the Flutter web app can make a live call against it.

**Architecture:** Topology B (two origins). `app/` stays on GitHub Pages; `brain/` runs on ACA with an external ingress that serves gRPC-Web (browser) + native gRPC. Cross-origin, so the kernel emits CORS. Deploy first to the default `*.azurecontainerapps.io` host (no DNS) to validate the chain before SP2 attaches `api.digitalbrain.tech`.

**Tech Stack:** .NET 11 (net11.0), ASP.NET Core + `Grpc.AspNetCore.Web`, Orleans 10.2, Pulumi.AzureNative (C#), Azure Container Apps, Flutter web (gRPC-Web over `grpc_or_grpcweb`).

**Spec:** `docs/superpowers/specs/2026-06-26-sp1-production-backend-aca-design.md`

## Global Constraints

- Target framework **net11.0**; Aspire **13.4.6**; Orleans **10.2** (preview journaling). No `Version="*"` — central versions in `Directory.Packages.props`.
- **Context7 before any framework API** (Pulumi.AzureNative, ASP.NET Core CORS/gRPC-Web, Flutter) — training data lags releases.
- **Typed C# only.** No `.ino`.
- **Self-explanatory names; no vacuous `/// <summary>`.** Small inline comments only where non-obvious.
- **Relative paths** in all repo files; never leak user-profile paths.
- Verification ritual after changes: `dotnet build`, relevant `dotnet test` (high-severity filter), `aspire doctor`.
- gRPC route under test: `/digitalbrain.DigitalBrainGateway/Health` (proto `package digitalbrain;`, service `DigitalBrainGateway`).
- Pulumi config namespace: `digitalbrain-deploy` (so `config.Get("checkpointKey")` ⇒ `digitalbrain-deploy:checkpointKey`).

## File Structure

| File | Responsibility | Action |
|---|---|---|
| `brain/.github/workflows/deploy.yml` | CI: build kernel image, push, `pulumi up` | Modify (un-break Silo→Kernel, repo name, state backend) |
| `brain/DigitalBrain.Kernel/Program.cs` | Host pipeline: add browser CORS around gRPC-Web | Modify |
| `brain/DigitalBrain.Kernel/appsettings.json` | Default allowed CORS origins | Modify |
| `brain/DigitalBrain.Tests/Gateway/GatewayCorsTests.cs` | Assert CORS preflight on the gRPC route | Create |
| `brain/deploy/Program.cs` | Pulumi: external ingress, web port, checkpoint-key secret, env | Modify |
| `brain/deploy/Pulumi.dev.yaml` | Stack config (image tag + checkpoint key secret) | Modify (via `pulumi config set`) |
| `brain/deploy/DEPLOY-STATUS.md` | Record the new public-ingress + state-backend decisions | Modify |
| `app/lib/grpc/endpoint.dart` | Resolve kernel endpoint; honor remote host on web | Modify (extract pure helper) |
| `app/test/grpc/endpoint_test.dart` | Unit-test the pure resolver | Create |

---

### Task 1: Branch + un-break `deploy.yml`

**Files:**
- Modify: `brain/.github/workflows/deploy.yml`
- Modify: `brain/deploy/DEPLOY-STATUS.md`

**Interfaces:**
- Consumes: nothing.
- Produces: a CI workflow that publishes `DigitalBrain.Kernel` (not the deleted `DigitalBrain.Silo`) and targets the agreed Pulumi state backend (`azblob`). Image repository string `vhorbachov/digitalbrain-silo` is kept verbatim (must match `deploy/Program.cs` `SiloImageRepository`).

- [ ] **Step 1: Branch already prepared (done by controller)**

The branch `prod/sp1-public-backend` is already created off `hardening/bucket-d` (NOT `main` — `main` lacks the gRPC-Web + `DIGITALBRAIN_WEB_PORT` foundation this plan depends on). It carries the SP1 spec + plan commits and a clean working tree. Verify before editing:
```bash
cd brain
git rev-parse --abbrev-ref HEAD   # must print: prod/sp1-public-backend
git status -s                     # must be clean
```

- [ ] **Step 2: Fix the stale project path and image repo in `deploy.yml`**

In `brain/.github/workflows/deploy.yml`, the "Publish silo image" step references the renamed project. Replace:
```yaml
      - name: Publish silo image
        run: |
          dotnet publish DigitalBrain.Silo/DigitalBrain.Silo.csproj -c Release /t:PublishContainer \
            -p:ContainerRegistry=docker.io \
            -p:ContainerRepository=vhorbachov/digitalbrain-silo \
            -p:ContainerImageTag="${TAG}"
```
with:
```yaml
      - name: Publish kernel image
        run: |
          dotnet publish DigitalBrain.Kernel/DigitalBrain.Kernel.csproj -c Release /t:PublishContainer \
            -p:ContainerRegistry=docker.io \
            -p:ContainerRepository=vhorbachov/digitalbrain-silo \
            -p:ContainerImageTag="${TAG}"
```
(The repository name stays `digitalbrain-silo` so the existing Pulumi `SiloImageRepository` and stack state keep matching — a rename is SP3.)

Also fix the test step's project path if needed (it already references `DigitalBrain.Tests/DigitalBrain.Tests.csproj` — leave as-is).

- [ ] **Step 3: Point CI at the `azblob` state backend explicitly**

Confirm the `pulumi/actions@v6` step uses `cloud-url: azblob://pulumi-state`. The current `deploy.yml` already sets this implicitly via the action's `cloud-url`; if absent, add under the `with:` block:
```yaml
        with:
          command: up
          stack-name: dev
          work-dir: deploy
          cloud-url: azblob://pulumi-state
```
The one-time state migration from the local file backend to `azblob` is a runbook step (Task 5) the user runs once; this change only declares CI's intent.

- [ ] **Step 4: Record the decisions in `DEPLOY-STATUS.md`**

Append a dated note under `## Follow-ups` stating: (a) ingress is now **external** (public) serving gRPC-Web; (b) CI state backend is **azblob** and the stack was migrated off the local file backend; (c) a `DigitalBrain:Checkpoint:Key` secret is now required (added in Task 3). Use self-explanatory prose, no placeholders.

- [ ] **Step 5: Verify YAML parses and references resolve**

Run:
```bash
cd brain
grep -n "DigitalBrain.Kernel/DigitalBrain.Kernel.csproj" .github/workflows/deploy.yml
test -f DigitalBrain.Kernel/DigitalBrain.Kernel.csproj && echo "kernel csproj exists"
! test -d DigitalBrain.Silo && echo "no stale Silo dir"
```
Expected: the grep matches, both echoes print.

- [ ] **Step 6: Commit**

```bash
cd brain
git add .github/workflows/deploy.yml deploy/DEPLOY-STATUS.md
git commit -m "fix(ci): publish DigitalBrain.Kernel (was renamed Silo); azblob state backend"
```

---

### Task 2: Kernel browser CORS for gRPC-Web (TDD)

**Files:**
- Create: `brain/DigitalBrain.Tests/Gateway/GatewayCorsTests.cs`
- Modify: `brain/DigitalBrain.Kernel/Program.cs:113-143` (pipeline section)
- Modify: `brain/DigitalBrain.Kernel/appsettings.json`

**Interfaces:**
- Consumes: existing `WebApplicationFactory<Program>` test host (used by `GatewayGrpcWireTests`, collection `"silo-host"`).
- Produces: a named CORS policy `"browser"` applied app-wide via `app.UseCors("browser")`; allowed origins read from config key `DigitalBrain:Cors:AllowedOrigins` (string array); exposed headers include `Grpc-Status`, `Grpc-Message`, `Grpc-Encoding`, `Grpc-Accept-Encoding`.

- [ ] **Step 1: Write the failing CORS preflight test**

Create `brain/DigitalBrain.Tests/Gateway/GatewayCorsTests.cs`:
```csharp
using System.Net.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DigitalBrain.Tests.Gateway;

[Collection("silo-host")]
public class GatewayCorsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public GatewayCorsTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Preflight_FromBrowserOrigin_AllowsOriginOnGrpcRoute()
    {
        var client = _factory.CreateClient();
        using var preflight = new HttpRequestMessage(
            HttpMethod.Options, "/digitalbrain.DigitalBrainGateway/Health");
        preflight.Headers.Add("Origin", "https://digitalbrain.tech");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", "content-type,x-grpc-web");

        var response = await client.SendAsync(preflight);

        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins));
        Assert.Contains("https://digitalbrain.tech", origins);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:
```bash
cd brain
dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~GatewayCorsTests" -v minimal
```
Expected: FAIL — no `Access-Control-Allow-Origin` header (CORS not configured yet).

- [ ] **Step 3: Register the CORS policy and apply it in the pipeline**

In `brain/DigitalBrain.Kernel/Program.cs`, add the service registration near the other `builder.Services` calls (after `builder.Services.AddGrpc();`):
```csharp
var corsOrigins = builder.Configuration
    .GetSection("DigitalBrain:Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "https://digitalbrain.tech" };

builder.Services.AddCors(options => options.AddPolicy("browser", policy => policy
    .WithOrigins(corsOrigins)
    .AllowAnyMethod()
    .AllowAnyHeader()
    .WithExposedHeaders("Grpc-Status", "Grpc-Message", "Grpc-Encoding", "Grpc-Accept-Encoding")));
```

Then in the pipeline section, place `UseCors` after `UseRouting` and before `UseGrpcWeb` so preflight is handled for the gRPC-Web routes. Change:
```csharp
app.UseRouting();
app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });
```
to:
```csharp
app.UseRouting();
app.UseCors("browser");
app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });
```

- [ ] **Step 4: Add the default origins to `appsettings.json`**

In `brain/DigitalBrain.Kernel/appsettings.json`, add (merge into the existing `DigitalBrain` section if present):
```json
  "DigitalBrain": {
    "Cors": {
      "AllowedOrigins": [ "https://digitalbrain.tech" ]
    }
  }
```
(SP1 verification from `localhost` adds its dev origin via env/config at deploy time — Task 3 — not here.)

- [ ] **Step 5: Run the test to verify it passes**

Run:
```bash
cd brain
dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~GatewayCorsTests" -v minimal
```
Expected: PASS.

- [ ] **Step 6: Run the gateway suite to confirm no regression**

Run:
```bash
cd brain
dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~Gateway" -v minimal
```
Expected: PASS (CORS + existing wire tests).

- [ ] **Step 7: Commit**

```bash
cd brain
git add DigitalBrain.Kernel/Program.cs DigitalBrain.Kernel/appsettings.json DigitalBrain.Tests/Gateway/GatewayCorsTests.cs
git commit -m "feat(kernel): browser CORS for gRPC-Web (configurable origins, grpc headers exposed)"
```

---

### Task 3: Pulumi — external ingress, web port, checkpoint-key secret, env

**Files:**
- Modify: `brain/deploy/Program.cs`
- Modify: `brain/deploy/Pulumi.dev.yaml` (via `pulumi config set --secret`, done in Task 5 runbook; code reads it here)

**Interfaces:**
- Consumes: the kernel's cloud Kestrel binding, which adds an `Http1AndHttp2` listener on `DIGITALBRAIN_WEB_PORT` (`DigitalBrain.Kernel/Program.cs:32-36`).
- Produces: a public ACA container app. New env on the container: `ASPNETCORE_ENVIRONMENT=Production`, `DIGITALBRAIN_WEB_PORT=8080`, `DIGITALBRAIN_ENV=cloud` (kept), `DigitalBrain__Checkpoint__Key` (via `SecretRef` to new secret `digitalbrain-checkpoint-key`). Ingress: `External=true`, `TargetPort=8080`, `Transport="Auto"`.

- [ ] **Step 1: Add the checkpoint-key secret constant and config read**

In `brain/deploy/Program.cs`, add a secret name constant beside the others:
```csharp
    private const string CheckpointKeySecret = "digitalbrain-checkpoint-key";
```
And in `Provision()`, after the `imageTag` read, read the checkpoint key from Pulumi secret config (fail loudly if unset — checkpoints must be encrypted in Production):
```csharp
        var checkpointKey = config.GetSecret("checkpointKey")
            ?? throw new System.InvalidOperationException(
                "Pulumi config 'digitalbrain-deploy:checkpointKey' is required. " +
                "Set it once: pulumi config set --secret digitalbrain-deploy:checkpointKey <base64-32-bytes>.");
```

- [ ] **Step 2: Make the ingress external and browser-capable**

In the `silo` `App.ContainerApp` `Ingress`, change:
```csharp
                Ingress = new AppInputs.IngressArgs
                {
                    External = false,
                    TargetPort = 8080,
                    Transport = "Http2"
                },
```
to:
```csharp
                Ingress = new AppInputs.IngressArgs
                {
                    External = true,
                    TargetPort = 8080,
                    Transport = "Auto"
                },
```
(`Auto` lets ACA carry browser gRPC-Web over HTTP/1.1 to the same port that also serves native gRPC over HTTP/2.)

- [ ] **Step 3: Add the checkpoint-key secret to the ingress secrets**

In the `Configuration.Secrets` collection, add:
```csharp
                    new AppInputs.SecretArgs { Name = CheckpointKeySecret, Value = checkpointKey },
```

- [ ] **Step 4: Add the new environment variables to the container**

In the container `Env` collection, add these alongside the existing entries:
```csharp
                            new AppInputs.EnvironmentVarArgs { Name = "ASPNETCORE_ENVIRONMENT", Value = "Production" },
                            new AppInputs.EnvironmentVarArgs { Name = "DIGITALBRAIN_WEB_PORT", Value = "8080" },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Checkpoint__Key", SecretRef = CheckpointKeySecret },
```
Leave `DIGITALBRAIN_WEBROOT` unset (Topology B — kernel does not serve the bundle).

- [ ] **Step 5: Build the Pulumi program to verify it compiles**

Run:
```bash
cd brain/deploy
dotnet build DigitalBrain.Deploy.csproj -c Release
```
Expected: build succeeds, 0 errors. (A real `pulumi preview`/`up` requires Azure creds + the config secret and is a Task 5 runbook step.)

- [ ] **Step 6: Commit**

```bash
cd brain
git add deploy/Program.cs
git commit -m "feat(deploy): public external ACA ingress (Auto transport) + checkpoint-key secret + Production env"
```

---

### Task 4: App `endpoint.dart` — honor a remote host on web (TDD)

**Files:**
- Modify: `app/lib/grpc/endpoint.dart`
- Create: `app/test/grpc/endpoint_test.dart`

**Interfaces:**
- Consumes: nothing app-internal.
- Produces: a pure, testable resolver `(_) resolveEndpointFrom({required bool isWeb, required Uri base, String? kernelEndpoint, String? aspireKernelUrl})` returning `(String host, int port, bool secure)`. Public `resolveKernelEndpoint()` delegates to it with real `kIsWeb` / `Uri.base`. On web, a non-empty `kernelEndpoint` (absolute URL) wins over `Uri.base`.

- [ ] **Step 1: Write the failing test for the pure resolver**

Create `app/test/grpc/endpoint_test.dart`:
```dart
import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_flutter/grpc/endpoint.dart';

void main() {
  group('resolveEndpointFrom', () {
    test('web honors a configured absolute KERNEL_ENDPOINT host', () {
      final (host, port, secure) = resolveEndpointFrom(
        isWeb: true,
        base: Uri.parse('https://digitalbrain.tech/'),
        kernelEndpoint: 'https://api.digitalbrain.tech',
        aspireKernelUrl: null,
      );
      expect(host, 'api.digitalbrain.tech');
      expect(secure, isTrue);
    });

    test('web with no config falls back to same-origin host', () {
      final (host, _, secure) = resolveEndpointFrom(
        isWeb: true,
        base: Uri.parse('https://digitalbrain.tech/'),
        kernelEndpoint: '',
        aspireKernelUrl: null,
      );
      expect(host, 'digitalbrain.tech');
      expect(secure, isTrue);
    });

    test('non-web honors a configured KERNEL_ENDPOINT', () {
      final (host, port, secure) = resolveEndpointFrom(
        isWeb: false,
        base: Uri.parse('http://localhost/'),
        kernelEndpoint: 'https://localhost:59066',
        aspireKernelUrl: null,
      );
      expect(host, 'localhost');
      expect(port, 59066);
      expect(secure, isTrue);
    });
  });
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:
```bash
cd app
flutter test test/grpc/endpoint_test.dart
```
Expected: FAIL — `resolveEndpointFrom` is undefined.

- [ ] **Step 3: Extract the pure resolver and fix the web-host bug**

Rewrite `app/lib/grpc/endpoint.dart` so the public function delegates to a pure helper, and the web branch honors a configured absolute endpoint instead of always using `Uri.base.host`:
```dart
import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:digitalbrain_flutter/telemetry/platform_env.dart';

(String host, int port, bool secure) resolveKernelEndpoint() {
  final base = Uri.base;

  if (kIsWeb) {
    final portParam = base.queryParameters['port'] ?? getEnv('KERNEL_PORT');
    if (portParam != null && portParam.isNotEmpty) {
      final p = int.tryParse(portParam);
      if (p != null) {
        return (base.host, p, base.scheme == 'https');
      }
    }
  }

  const configured = String.fromEnvironment('KERNEL_ENDPOINT');
  final aspireUrl = kIsWeb ? null : getEnv('services__kernel__https__0');

  return resolveEndpointFrom(
    isWeb: kIsWeb,
    base: base,
    kernelEndpoint: configured.isEmpty ? null : configured,
    aspireKernelUrl: aspireUrl,
  );
}

(String host, int port, bool secure) resolveEndpointFrom({
  required bool isWeb,
  required Uri base,
  String? kernelEndpoint,
  String? aspireKernelUrl,
}) {
  if (kernelEndpoint != null && kernelEndpoint.isNotEmpty) {
    final u = Uri.parse(kernelEndpoint);
    if (u.host.isEmpty) {
      throw StateError(
        'KERNEL_ENDPOINT="$kernelEndpoint" has no host. Expected an absolute '
        'URL, e.g. https://api.digitalbrain.tech.',
      );
    }
    final port = u.hasPort ? u.port : (u.scheme == 'https' ? 443 : 80);
    return (u.host, port, u.scheme == 'https');
  }

  if (isWeb) {
    final port = base.hasPort ? base.port : (base.scheme == 'https' ? 443 : 80);
    return (base.host, port, base.scheme == 'https');
  }

  if (aspireKernelUrl == null || aspireKernelUrl.isEmpty) {
    throw StateError(
      'DigitalBrain desktop client requires services__kernel__https__0 '
      '(or --dart-define=KERNEL_ENDPOINT). Set it, e.g. '
      r"flutter run -d windows --dart-define=KERNEL_ENDPOINT='https://localhost:59066'.",
    );
  }
  final u = Uri.parse(aspireKernelUrl);
  return (u.host, u.port, u.scheme == 'https');
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run:
```bash
cd app
flutter test test/grpc/endpoint_test.dart
```
Expected: PASS (3 tests).

- [ ] **Step 5: Analyze for lint/regressions**

Run:
```bash
cd app
flutter analyze lib/grpc/endpoint.dart test/grpc/endpoint_test.dart
```
Expected: no issues.

- [ ] **Step 6: Commit**

```bash
cd app
git checkout -b prod/sp1-remote-endpoint
git add lib/grpc/endpoint.dart test/grpc/endpoint_test.dart
git commit -m "fix(grpc): web client honors configured KERNEL_ENDPOINT host (extracted pure resolver)"
```

---

### Task 5: One-time infra prep + end-to-end verification runbook

This task is **operational** — it runs credentialed, cost-incurring commands and a manual browser check. No unit test; the deliverable is a verified live deploy. The user runs the credentialed steps (or confirms creds are available to the session) per the earlier execution decision (GH Actions does the recurring deploy; this is the one-time bootstrap + first verification).

**Files:** none (uses `brain/deploy` + the deployed app).

- [ ] **Step 1: Generate and set the checkpoint key (once)**

Run (PowerShell, off the user profile per repo convention):
```powershell
$key = [Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Maximum 256 }))
cd brain/deploy
pulumi config set --secret digitalbrain-deploy:checkpointKey $key --stack dev
```
Expected: the secret is stored encrypted in `Pulumi.dev.yaml`.

- [ ] **Step 2: Migrate Pulumi state local-file → azblob (once)**

So CI (GitHub runner) and local share one backend. Preserves live resources via export/import:
```bash
# from the local file backend (current source of truth)
pulumi login file://E:/tools/pulumi-state
pulumi stack export --stack dev > E:/tools/sp1-stack-dev.json
# switch to azblob and import
pulumi login azblob://pulumi-state
pulumi stack init dev          # if not present on azblob
pulumi stack import --stack dev --file E:/tools/sp1-stack-dev.json
```
Expected: `pulumi stack --stack dev` on the azblob backend lists the existing resources (RG, storage, OpenAI, env, container app).

- [ ] **Step 3: Add the local dev origin to allowed CORS origins for first-cut verification**

First-cut verification runs the app from `localhost`, not `digitalbrain.tech`. Add the dev origin via stack config so the deployed kernel accepts it (env beats appsettings). Add to `deploy/Program.cs` container `Env` a temporary entry, OR set it as config-driven. Simplest: add an env var entry mapping to the config array's first override. Since `GetSection(...).Get<string[]>()` reads indexed keys, inject:
```
DigitalBrain__Cors__AllowedOrigins__0 = https://digitalbrain.tech
DigitalBrain__Cors__AllowedOrigins__1 = http://localhost:<flutter-web-port>
```
Add both as `EnvironmentVarArgs` in `deploy/Program.cs`, then rebuild. (Remove the localhost entry in SP2/SP3.)

- [ ] **Step 4: Build + push the kernel image and deploy**

```powershell
cd brain
dotnet publish DigitalBrain.Kernel/DigitalBrain.Kernel.csproj -c Release /t:PublishContainer `
  -p:ContainerRegistry=docker.io -p:ContainerRepository=vhorbachov/digitalbrain-silo -p:ContainerImageTag=sp1
docker push docker.io/vhorbachov/digitalbrain-silo:sp1
cd deploy
pulumi config set digitalbrain-deploy:imageTag sp1 --stack dev
pulumi up --stack dev --yes
```
Expected: `pulumi up` succeeds; output `siloApp` and an external ingress FQDN (`*.azurecontainerapps.io`).

- [ ] **Step 5: Confirm the container booted (no checkpoint-key crash)**

```bash
az containerapp logs show -n digitalbrain-jobs -g digitalbrain-rg --tail 50
```
Expected: Orleans silo started; no `DigitalBrain:Checkpoint:Key is required in Production` fatal; no crash loop.

- [ ] **Step 6: Verify gRPC-Web health over the public ingress**

```bash
FQDN=$(cd brain/deploy && pulumi stack output siloApp 2>/dev/null; echo)   # or read the ingress FQDN from the portal
curl -sS -i -X OPTIONS "https://<ingress-fqdn>/digitalbrain.DigitalBrainGateway/Health" \
  -H "Origin: https://digitalbrain.tech" -H "Access-Control-Request-Method: POST"
```
Expected: `Access-Control-Allow-Origin: https://digitalbrain.tech` in the response headers.

- [ ] **Step 7: End-to-end browser check (the acceptance gate)**

```bash
cd app
flutter run -d chrome --dart-define=KERNEL_ENDPOINT=https://<ingress-fqdn>
```
Expected: app loads; the live timeline connects (gRPC-Web `watchActivity`/`listBrains` succeeds, no CORS error in the browser console); creating a brain shows activity. **This is the SP1 done-criterion.**

- [ ] **Step 8: Record outcome**

Append the verified ingress FQDN and the date to `brain/deploy/DEPLOY-STATUS.md`, and note SP1 complete + SP2 (domain attach) as next. Commit:
```bash
cd brain
git add deploy/DEPLOY-STATUS.md deploy/Program.cs
git commit -m "docs(deploy): record SP1 live verification (public ingress FQDN, e2e green)"
```

---

## Self-Review

**Spec coverage:**
- Pulumi external ingress + web port + checkpoint key + env → Task 3 ✓
- Kernel CORS → Task 2 ✓
- `deploy.yml` un-break (Silo→Kernel, repo, state backend) → Task 1 ✓
- App `endpoint.dart` remote-host fix → Task 4 ✓
- Verification (boot guard, live gRPC-Web, browser e2e) → Task 5 ✓
- Deferred items (domain, Pages wiring, Key Vault, Stripe, HA scale, E2E-in-CI) → not present as tasks ✓ (correctly out of scope)

**Placeholder scan:** No "TBD/TODO/handle edge cases". Task 5 uses `<ingress-fqdn>` / `<flutter-web-port>` — these are genuine runtime values discovered during the operational run, not unspecified design, and each step says where they come from.

**Type consistency:** `resolveEndpointFrom(...)` signature is identical in Task 4 Steps 1 and 3. Pulumi constant `CheckpointKeySecret` and env `DigitalBrain__Checkpoint__Key` consistent across Task 3 steps. CORS policy name `"browser"` consistent across Task 2 steps. gRPC route `/digitalbrain.DigitalBrainGateway/Health` consistent (Task 2 + Task 5).

**Note for executor:** Tasks 1–4 are code/config and fully verifiable locally (`dotnet build`/`dotnet test`/`flutter test`). Task 5 is the credentialed operational run; per the execution decision the recurring deploy is GH-Actions-driven, so Task 5's manual `pulumi up` is the one-time bootstrap + first acceptance check, after which pushes to `main` deploy automatically.
