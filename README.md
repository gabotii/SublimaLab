# SublimaLab

Custom sublimation e-commerce: customers design T-shirts, mugs and keychains with their own artwork and text, preview them live in 3D, and order them. Administrators run production from a dashboard.

> **Status: all 13 phases and the engineering items of the production-readiness backlog are complete.** This includes server prerendering, image re-encoding, two-step verification for admins, trusted proxies, OpenTelemetry, migration bundles, legal page templates and SEO. What remains needs the store owner: a payment provider (Stripe keys), an email provider, hosting, and the company's legal details. See [docs/production-readiness.md](docs/production-readiness.md).

## Tech stack

| Layer | Technology |
|---|---|
| Storefront | Blazor Web App hosted by the API (same origin): public pages prerendered on the server, then interactive WebAssembly; custom CSS design system |
| Design studio | React 19 + TypeScript (Vite), zustand state with undo/redo, loaded on demand |
| 3D preview | three.js, React Three Fiber and drei; GLB models generated in-repo |
| API | ASP.NET Core 10 (controllers), OpenAPI and Swagger UI |
| Domain and application | C# 14, Clean Architecture, FluentValidation |
| Data | EF Core 10. SQLite for local development and tests, PostgreSQL for Docker and production |
| Auth | ASP.NET Core Identity, HttpOnly cookie, antiforgery header, role-based policies, authenticator-app two-step verification |
| Tests | xUnit (unit and `WebApplicationFactory` integration), bUnit (components), Vitest (studio) |
| Ops | Docker (Node + .NET multi-stage build, PostgreSQL, Caddy TLS proxy, migration bundle), GitHub Actions CI, OpenTelemetry (OTLP) |

## Repository layout

```
src/
  SublimaLab.Domain/              Entities, value objects, business invariants (no dependencies)
  SublimaLab.Application/         Use cases, DTOs, validation, pricing, customization engine, admin services
  SublimaLab.Infrastructure/      EF Core, Identity, file storage, image inspection, payments, email
  SublimaLab.Migrations.Sqlite/   EF Core migrations for SQLite
  SublimaLab.Migrations.Postgres/ EF Core migrations for PostgreSQL
  SublimaLab.Api/                 HTTP layer, security middleware, background jobs; hosts and prerenders the storefront
  SublimaLab.Blazor/              Storefront and admin UI (Blazor, interactive WebAssembly)
  SublimaLab.Customizer/          Design studio (React + TypeScript + three.js); its tests live next to the code
tests/
  SublimaLab.UnitTests/           Domain, pricing, validation, uploads, storage, retention
  SublimaLab.IntegrationTests/    Full HTTP flows against the real app, including admin
  SublimaLab.Blazor.Tests/        Storefront component tests (bUnit)
docs/                             Architecture, API guide, roadmap, production-readiness review
docker/                           Dockerfile, docker-compose, Caddy proxy
scripts/                          Helper scripts (product images, scripts/serve.ps1 dev launcher)
```

## Getting started (local, no database server needed)

**Prerequisites:**

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- [Node.js 22+](https://nodejs.org) (the design studio is built automatically during `dotnet build`)
- A trusted HTTPS dev certificate (`dotnet dev-certs https --trust`)

```bash
git clone <repo-url> SublimaLab && cd SublimaLab
dotnet restore
dotnet run --project src/SublimaLab.Api --launch-profile https
```

On first start in Development, the API:

- creates `src/SublimaLab.Api/App_Data/sublimalab.dev.db` (SQLite) and applies migrations;
- seeds the catalog (10 products in 3 categories), the shipping methods and the `WELCOME10` discount code;
- creates a local admin account, **admin@sublimalab.local** / **LocalDevOnly-Admin1**. This account exists for local development only and is configured in `appsettings.Development.json`. Two-step verification isn't required for admins in Development (`Auth:RequireAdminTwoFactor` is `false` there and `true` everywhere else).

The **storefront** opens at **https://localhost:7132**. The admin dashboard is at **/admin** and Swagger UI at **/swagger**.

Emails (account confirmation, password reset, order confirmation) are not sent in Development. They are written as HTML files to `src/SublimaLab.Api/App_Data/mail/`, so you can open them and click the links.

To reset the local database, stop the API, delete `src/SublimaLab.Api/App_Data/`, and start it again.

### Trying it out

1. Open https://localhost:7132, pick a T-shirt and click **Customize this product**.
2. In the studio:
   - upload an image (drag and drop works) and add text or a template;
   - drag, resize and rotate elements; undo and redo with Ctrl+Z / Ctrl+Shift+Z;
   - switch to **3D preview** and rotate, zoom or turn the shirt around;
   - click **Add to cart**.
3. In the cart, try `WELCOME10`, then click **Checkout** and create an account. Open the confirmation email from `App_Data/mail/` and click its link.
4. Sign in. Your guest cart is kept. Complete the four checkout steps and pay with **Test card · approved**. Choosing **Test card · declined** first lets you try the retry flow.
5. Follow the order under **My account**. Sign in as the admin to move it through production, download the production artwork ZIP and add a tracking number.
6. Under **My account → Profile & security**, turn on two-step verification with any authenticator app, then sign out and back in.

> **Windows Smart App Control:** if a build or `dotnet run` fails with *"An Application Control policy has blocked this file"*, Windows blocked a freshly compiled DLL. Rebuilding usually gets past it; turning Smart App Control off avoids it entirely.

### Working on the design studio

The studio builds into `src/SublimaLab.Blazor/wwwroot/customizer` (git-ignored). `dotnet build` runs it automatically and incrementally. To work on it directly:

```bash
cd src/SublimaLab.Customizer
npm ci
npx vite build --watch   # rebuild on save; reload /customize in the browser
npm test                 # Vitest
npm run models           # regenerate the GLB models in wwwroot/models
```

## Tests

```bash
dotnet test                                     # unit, integration and component tests
cd src/SublimaLab.Customizer && npx vitest run  # 19 studio tests
```

- **Unit tests:** cart rules, the order state machine, payments, discounts, shipping, pricing, design validation, upload validation, image signature sniffing, storage path-traversal protection and retention.
- **Integration tests:** run the real application (migrations and seed data included) against a temporary SQLite file. They cover:
  - the full customer journey: register, confirm email, upload, customize, check out, pay, confirmation email, fulfilment, tracking;
  - admin: product creation and publishing with cache eviction, photo upload, the production ZIP, the dashboard and customer locking;
  - two-step verification (TOTP codes generated in the test), prerendered pages, robots.txt and the sitemap, proxy and telemetry configuration;
  - security checks.
- **Component tests:** product cards, cart, login open-redirect protection, CSRF token refresh, formatting.
- **Studio tests:** placement geometry (identical to the server's), resolution, undo/redo and gestures, layers, text measurement, and the API document format.

## Database and migrations

Each provider has its own migrations assembly, so the generated SQL stays provider-specific:

```bash
# After changing the model, add a migration for BOTH providers:
dotnet ef migrations add <Name> --context SqliteAppDbContext \
  --project src/SublimaLab.Migrations.Sqlite --startup-project src/SublimaLab.Migrations.Sqlite --output-dir Migrations
dotnet ef migrations add <Name> --context PostgresAppDbContext \
  --project src/SublimaLab.Migrations.Postgres --startup-project src/SublimaLab.Migrations.Postgres --output-dir Migrations
```

`dotnet ef` is pinned as a local tool (`dotnet tool restore`).

Migrations and seeding run at startup when `Database:MigrateOnStartup` is `true` (the Development default). In production, apply migrations as a deployment step with the **migration bundle** and set `Database:SeedOnStartup=true` instead, which seeds roles and the bootstrap admin without migrating:

```bash
dotnet ef migrations bundle --project src/SublimaLab.Migrations.Postgres --startup-project src/SublimaLab.Migrations.Postgres \
  --context PostgresAppDbContext --configuration Release --self-contained -r linux-x64 --output efbundle
./efbundle --connection "Host=...;Database=sublimalab;Username=...;Password=..."
```

CI builds this bundle as an artifact, and the Dockerfile's `migrations` target packages it as a small image.

## Docker (PostgreSQL)

```bash
cp docker/.env.example docker/.env   # then set real passwords
docker compose -f docker/docker-compose.yml --env-file docker/.env up --build
```

The image builds the studio in a Node stage, then publishes the API with the storefront. A one-off `migrate` service applies the PostgreSQL migrations before the API starts. The app is served at **https://localhost:8443** through Caddy, using Caddy's internal CA (trust it, or use `curl -k`). Authentication cookies are `Secure`, so it must always be reached over HTTPS.

## Configuration

All settings live in `appsettings*.json` and can be overridden with environment variables (`Section__Key`). Store secrets in user-secrets (`dotnet user-secrets`, already enabled for the API project) or your platform's secret store, never in source control.

| Setting | Purpose |
|---|---|
| `ConnectionStrings:Default` | Database connection string |
| `Database:Provider` | `Sqlite` or `Postgres` |
| `Database:MigrateOnStartup` | Apply migrations and seed data at startup |
| `Database:SeedOnStartup` | Seed roles, the bootstrap admin and the catalog without migrating (use with the migration bundle) |
| `App:PublicBaseUrl` | Storefront URL used in email links |
| `Auth:RequireConfirmedEmail` | Block sign-in until the email address is confirmed (default `true`) |
| `Auth:RequireAdminTwoFactor` | Admin features require a session signed in with two-step verification (default `true`; `false` in Development) |
| `Commerce:Currency`, `Commerce:TaxRate` | Store currency and sales tax rate |
| `Uploads:*` | Size, dimension and megapixel limits; low-resolution warning threshold |
| `Storage:RootPath` | Upload directory (local disk) |
| `DataProtection:KeysPath` | Where cookie and token keys are persisted (default `App_Data/keys`) |
| `Retention:Enabled` | Daily cleanup of unused guest uploads and stale guest carts (default `true`) |
| `Email:Provider` | `Pickup` (writes files) or `Smtp` (configure `Email:Smtp*`) |
| `Cors:AllowedOrigins` | Extra origins allowed to call the API with credentials (the storefront itself is same-origin) |
| `RateLimiting:*` | Per-minute limits: global, auth endpoints, uploads |
| `ReverseProxy:Enabled`, `ReverseProxy:KnownNetworks`, `ReverseProxy:KnownProxies` | Honour `X-Forwarded-*` headers, but only from these proxy ranges/addresses. Startup fails if enabled with neither set |
| `Prerendering:ApiBaseUrl` | Where the server calls itself while prerendering (default: one of Kestrel's own addresses) |
| `OTEL_EXPORTER_OTLP_ENDPOINT` (+ `OTEL_EXPORTER_OTLP_HEADERS`, `OTEL_SERVICE_NAME`) | Export traces, metrics and logs over OTLP; off when unset |
| `Seed:AdminEmail`, `Seed:AdminPassword` | Optional bootstrap admin account |

Store details on the contact and legal pages (company name, address, support email) are placeholders in `src/SublimaLab.Blazor/Services/StoreInfo.cs`. Replace them, have the pages reviewed, then set `IsPlaceholder = false`.

## Further reading

- [docs/architecture.md](docs/architecture.md): architecture and the key decisions behind it
- [docs/api.md](docs/api.md): endpoints, authentication flow, error format, design JSON schema
- [docs/roadmap.md](docs/roadmap.md): phase status and next steps
- [docs/production-readiness.md](docs/production-readiness.md): acceptance criteria, quality gates, security, performance and remaining work
