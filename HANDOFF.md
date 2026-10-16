# Handoff (updated 2026-10-01)

All 13 spec phases and every engineering item in the production-readiness backlog are done. What's left needs the store owner (see below). Nothing is committed to git yet.

**State:** builds with 0 warnings (Debug and Release); `dotnet format` is clean; 102 unit, 69 integration, 33 bUnit and 19 Vitest tests pass; no known vulnerable packages (NuGet and npm). Browser scripts for purchase, studio, admin, two-step verification, prerendering and legal pages pass on desktop and mobile.

## Done in the hardening round

| Item | Where |
|---|---|
| S1 image re-encoding | `Application/Abstractions/IImageSanitizer.cs`, `Infrastructure/Imaging/SkiaImageSanitizer.cs` (SkiaSharp) |
| S5 two-step verification (TOTP, recovery codes), required for admins | `IdentityService`, `Api/Http/MultiFactorAuthorization.cs`, `Blazor/Pages/Account/TwoFactorSettings.razor`, `Login.razor` code step, `AdminLayout` gate |
| S6 trusted proxies | `Api/Hosting/ReverseProxySetup.cs` |
| O2 migration bundle | CI step, Dockerfile `migrations` target, compose `migrate` service, `Database:SeedOnStartup` |
| O3 OpenTelemetry | `Api/Hosting/TelemetrySetup.cs`, `Application/Common/StoreMetrics.cs` |
| P1 prerendering | `Api/Components/App.razor`, `Api/Hosting/StorefrontRendering.cs`, `Blazor/Routes.razor`, `Blazor/Services/PrerenderCache.cs`, `StorefrontServices.cs` |
| Legal pages | `Blazor/Pages/Legal/*`, placeholders in `Blazor/Services/StoreInfo.cs` |
| SEO | `Application/Seo/SeoService.cs`, `Api/Controllers/SeoController.cs`, `Blazor/Components/ProductJsonLd.razor` |

The design is documented in `docs/architecture.md` (decisions 8, 12, 17 and 18) and the status in `docs/production-readiness.md`.

## Needs the owner (can't be done without them)

- **S2, Stripe:** test API keys (`pk_test_…`, `sk_test_…`) and a webhook secret. Then implement `IPaymentGateway` for Stripe: Payment Element on checkout, plus a signature-verified webhook marked `[IgnoreAntiforgeryToken]`.
- **O5, email:** an SMTP or transactional provider account (Postmark, SendGrid, SES, …). SMTP is already implemented.
- **Hosting:** a server or cloud account and a domain. This settles S3 (production admin secrets), S4 (shared Data Protection keys when scaling out), O1 (blob storage via `IFileStorage`) and O4 (backups). Docker isn't installed locally, so the Docker/PostgreSQL path is unverified; CI builds both images.
- **Legal text:** real company details in `StoreInfo.cs`, a lawyer's review, then `IsPlaceholder = false`.

## Optional next engineering steps

- P1b: static SSR with interactive islands for home, catalog and product pages. This removes the lab-only mobile LCP repaint described in production-readiness §4.
- Self-service account deletion and data export.
- A CDN for `_framework`, and real-user metrics (web-vitals).

## Machine notes

- **Smart App Control** blocks freshly built DLLs at random ("An Application Control policy has blocked this file", sometimes shown as a test "hang" or "Could not load assembly"). Rebuild with `-p:InformationalVersion=1.0.0-r$RANDOM` and retry. Use `--blame-hang-timeout 90s` with `dotnet test` so a blocked test host fails instead of hanging.
- **Migration bundle on Windows:** `dotnet ef migrations bundle --no-build` locks itself after a custom-version build. Build it from a clean copy of the sources, or rely on CI and Docker (Linux), where it works normally.
- **Dev server:** stop it before building the Api project; it locks the files in `bin`. Start it with `powershell -File scripts/serve.ps1` (https://localhost:7132; `-StopOnly` stops it). Dev admin login: admin@sublimalab.local / LocalDevOnly-Admin1. 2FA isn't required in Development.
- **Fast builds:** add `-p:SkipCustomizerBuild=true` to skip the npm build when the studio hasn't changed.
- **File edits:** don't edit files with PowerShell `Set-Content`; it double-encodes UTF-8. Use the Edit tool or Python with `encoding='utf-8'`. New files must use LF line endings (run `dotnet format` if needed). Long Bash heredocs with Python sometimes fail to parse; write the script to a file instead.
