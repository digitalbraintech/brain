# Aspire startup and test isolation

## Missing dashboard URL on 5 September 2026

The normal AppHost dashboard configuration was unchanged. The failed launch was
caused by a concurrent E2E host advertising the same AppHost project identity.

The parent `aspire start` process began its background child at 14:31:21 UTC. At
14:31:51 it attached to PID 27540 through the project's auxiliary backchannel.
That process was a test host with its dashboard disabled. The parent reported
success without a dashboard URL and exited at 14:31:53. Its actual child was still
building under `dotnet` PID 372. At 14:31:54 that child noticed its launcher had
exited before readiness and cancelled the build.

The evidence is in the local Aspire logs:

- `cli_20260905T143121_66849e84.log`
- `cli_20260905T143121933_detach-child_902a1a8b149c43cbb5921d3bc5a54fdd.log`

Both are under `C:\Users\vhorb\.aspire\logs`.

Aspire's [testing builder disables its dashboard by default](https://aspire.dev/testing/overview/).
In 13.5.3, its [auxiliary backchannel uses the AppHost file path as the discovery key](https://github.com/microsoft/aspire/blob/v13.5.3/src/Aspire.Hosting/Backchannel/AuxiliaryBackchannelService.cs).
That combination explains why a live test could be mistaken for the requested
development instance.

## Fix in this repository

`BrainAppHostFixture` gives test-host discovery the actual test assembly identity
after the resource model is constructed. Normal CLI startup therefore cannot
select it as the development AppHost. The fixture also randomizes proxyless
project endpoints, including the kernel; E2E runs no longer claim port 5080.
Tests continue to use `CreateHttpClient` and service discovery.

## Normal use

From the repository root:

```powershell
aspire start --apphost src/Aspire/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj --non-interactive
aspire wait kernel --apphost src/Aspire/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj --non-interactive
aspire describe --apphost src/Aspire/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj --format Json --non-interactive
```

`aspire start` prints the actual dashboard URL. Use `aspire ps --format Json` to
inspect running instances. Do not infer readiness merely from a spawned process.
The dashboard can be ready while containers, the kernel or the native Flutter
build are still starting; `aspire wait kernel` checks backend readiness.

The environment diagnostic passed the CLI/AppHost version, .NET SDK, trusted
development certificate, Docker and DCP checks. The optional VS Code extension
was absent; that was unrelated to the failure.

Normal startup was verified while an isolated test host was live: the CLI returned
the normal dashboard and the E2E suite completed successfully on its separate
kernel port. Final startup, coexistence and desktop results are recorded in
[the validation record](programmable-behaviors-validation.md).
