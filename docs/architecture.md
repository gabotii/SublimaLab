# Architecture

This document records how SublimaLab is structured and why. Each decision lists the alternatives that were rejected.

## Overview

```
                 ┌──────────────────────────────┐
 Browser ───────►│ SublimaLab.Api               │  HTTP, auth cookie, CSRF, rate limits,
 (Blazor +       │  controllers / middleware,   │  error mapping, OpenAPI, prerendering
  React studio)  │  storefront host             │
                 └──────────────┬───────────────┘
                                │ calls
                 ┌──────────────▼───────────────┐
                 │ SublimaLab.Application       │  use cases, DTOs, validation,
                 │  services, pricing, design   │  pricing, customization engine
                 │  validation engine           │
                 └──────┬───────────────▲───────┘
                        │ uses          │ implements ports
                 ┌──────▼──────┐  ┌─────┴──────────────────────┐
                 │ Domain      │  │ SublimaLab.Infrastructure  │  EF Core, Identity, storage,
                 │ entities &  │  │                            │  image sniffing, payments,
                 │ invariants  │  │                            │  email
                 └─────────────┘  └────────────────────────────┘
```

Dependencies point inwards. The Domain references nothing; Application references only the Domain; Infrastructure implements the ports (interfaces) that Application defines; the API composes everything.

## Key decisions

### 1. Pragmatic Clean Architecture, without repository boilerplate

Application services query EF Core through `IAppDbContext`, using `DbSet<T>` and LINQ projections. There are no generic repositories, because `DbSet` already is a repository and unit of work, and projections keep queries efficient.

Application services are concrete classes (`CartService`, `OrderService`...). Each has a single implementation and is tested against a real database. Interfaces are reserved for real seams with the outside world: `IFileStorage`, `IPaymentGateway`, `IEmailSender`, `IImageInspector`, `IIdentityService`, `ICurrentUser`, `IClientSession`.

*Rejected:* MediatR, CQRS pipelines and per-entity repositories. They add indirection without solving a current problem.

### 2. Rich aggregates where invariants matter, plain data elsewhere

`Cart`, `Order`, `Payment` and `DiscountCode` enforce their own rules: quantity limits, line merging, the order status state machine, and payments that can be finalised only once. Catalog entities (`Product`, `ProductVariant`...) are simple data holders. They are validated at the application boundary, because their rules are CRUD validation, not behaviour.

### 3. Designs are versioned JSON documents

A design (`DesignDocument`) is a list of surfaces (one per print area), each with an ordered list of polymorphic elements (`image`, `text`). It is stored as a JSON column on saved designs, cart items and order items.

- It is always read and written as a whole; nothing queries individual elements.
- New element types (shapes, QR codes, clip-art) are new C# records plus a JSON discriminator. They need no table migration.
- The order item keeps a **verbatim snapshot**, so the exact design ordered is preserved forever.
- `schemaVersion` allows forward migration of old documents.

Coordinates are in **millimetres** relative to the print area, which makes production output resolution-independent (`px = mm / 25.4 × dpi`). Images reference uploads **by id**, never by URL, so a client cannot point a design at arbitrary content.

*Deviation from the brief:* the brief suggested a `DesignElement` table. JSON serves these access patterns better (see above), so `DesignElement` is a domain type rather than a table.

### 4. One customization engine for every product

There is no `TShirtCustomizer` / `MugCustomizer` class hierarchy. Each product has a `CustomizationConfiguration`: print areas (size, shape, UV mapping onto the 3D model), fees, element limits, a minimum recommended DPI, a 3D model URL and a `CustomizerKind` hint for the frontend renderer. `DesignValidationService` validates any design against any configuration. Adding a product type means adding configuration, not code.

`ProductionSpec` (frozen print-area dimensions plus artwork metadata, including SHA-256) is stored per order item. A future production service can render print-ready PNG, PDF or SVG files from `Design` plus `ProductionSpec` alone, without depending on the frontend. That service would replace the brief's `GeneratePreviewAsync` method.

### 5. Prices are computed server-side, always

Carts store only product, variant, quantity and design; they never store a price. `PriceCalculator` (pure functions) and `CartPricer` compute every total from the live catalog, for both cart display and checkout. Request DTOs contain no price fields, so client-supplied values are simply ignored (integration tests prove it). Payment confirmation also checks that the provider's reported amount matches the order total.

Rules: unit price = base price + variant adjustment. Customization = base fee + an extra fee for each additional print area. Tax applies to the discounted merchandise subtotal; shipping is untaxed. Free shipping applies above a per-method threshold.

### 6. Cookie authentication with antiforgery, not JWT in the browser

The frontend runs in the browser and is served from the same site as the API. An **HttpOnly, Secure, SameSite=Lax** Identity cookie (`__Host-` prefix) keeps tokens out of JavaScript, which removes token theft through XSS. CSRF is handled with an antiforgery token sent in the `X-XSRF-TOKEN` header on every unsafe request (`AntiforgeryApiFilter`). Identity provides lockout after 5 failures, security-stamp revalidation and data-protected tokens for email confirmation and password reset.

Account-recovery endpoints never reveal whether an email address is registered. Registering an existing address returns the normal response and emails the owner instead.

### 7. Guests can shop and design; checkout requires an account

A random 256-bit session id in an HttpOnly cookie owns guest carts and uploads. On sign-in, `GuestSessionMerger` moves the cart lines into the account and claims the uploads, so no work is lost at the login step. Orders require an account. This keeps order history and tracking secure without exposing order lookup by email plus number.

### 8. Uploads are treated as hostile

- Type detection uses binary signatures (PNG, JPEG, WebP). The extension, the declared MIME type and the sniffed format must all agree.
- Dimensions are read from headers without decoding. Minimum size, maximum size and maximum megapixels guard against decompression bombs. Animated WebP is rejected.
- The size limit is enforced while streaming, so a false `Content-Length` doesn't bypass it.
- Storage keys are generated by the server (`yyyy/MM/{guid}.ext`), re-validated by regex, and must resolve inside the storage root, which prevents path traversal.
- Files are served only to their owner or an admin, with `nosniff` and a sandboxed CSP.
- Files referenced by orders are marked *retained* and cannot be deleted.

- **Every stored image is re-encoded** (`IImageSanitizer`, implemented with SkiaSharp). The file is fully decoded, converted to sRGB and rotated upright from its EXIF orientation, then encoded again in the same format (JPEG quality 95, lossless PNG, WebP quality 95). Metadata (EXIF/GPS), ICC profiles, comments, extra frames and appended payloads are dropped, and truncated or corrupt files are rejected. Decoding is limited to a few images in parallel. Admin product photos go through the same step.

### 9. Two database providers, one model

SQLite gives zero-setup local development and fast, realistic integration tests. PostgreSQL is the production target. `AppDbContext` defines the model once; `SqliteAppDbContext` adds SQLite-specific conventions (decimals stored as REAL so they can be sorted and compared in SQL, UTC `DateTime` handling). Migrations live in **separate assemblies per provider**.

### 10. Consistent errors

Every failure returns `{ code, message, errors?, traceId }`. Application exceptions map to status codes:

| Status | Meaning |
|---|---|
| 400 | Validation |
| 401 | Not authenticated |
| 403 | Forbidden |
| 404 | Not found, also used for resources you don't own |
| 409 | Conflict or concurrency |
| 422 | Business rule violation |
| 429 | Rate limited |
| 500 | Unexpected error |

Unexpected exceptions are logged with full detail but returned as a generic 500, so stack traces never leak. Orders use an optimistic concurrency token.

### 11. Blazor and React boundaries

- **Blazor** (a Blazor Web App: server prerendering plus interactive WebAssembly) for the storefront, account and admin: catalog, product pages, cart, checkout, orders, dashboard.
- **React + TypeScript** (Vite, lazy-loaded) for the customization studio only: design editor, state history (undo/redo), Three.js / React Three Fiber preview. It mounts inside a Blazor page through JS interop and calls the same API with the same cookies.
- No feature is implemented in both. Business rules (pricing, validation) stay in the API; the studio calls `POST /api/designs/validate` for authoritative checks and DPI warnings.
- The storefront never downloads Three.js: the studio bundle loads only when the customer opens the customizer.

### 12. The storefront is served by the API host (one origin), prerendered where it pays off

`SublimaLab.Api` hosts the storefront as a Blazor Web App: `Components/App.razor` is the HTML document, and the router and pages live in `SublimaLab.Blazor` (the WebAssembly project). Unknown `/api/*` routes still return the JSON 404 error. Because the app and API share one origin:

- the HttpOnly auth cookie, the `__Host-` prefix, SameSite and the antiforgery token work with no CORS or cross-site configuration;
- deployment is a single container behind TLS.

App pages get their own Content Security Policy:

- scripts only from `'self'` plus `'wasm-unsafe-eval'`, which WebAssembly requires;
- fonts from Google Fonts;
- inline *styles* allowed (for colour swatches); inline *scripts* never allowed.

**Prerendering.** The render mode is chosen per request (`Hosting/StorefrontRendering.cs`):

- *Public pages* (home, shop, categories, products, help and legal pages, and the 404 page) are prerendered: the server sends complete HTML, with title, product data and JSON-LD, and WebAssembly takes over in place once it has loaded. Visitors see content without waiting for the .NET runtime, and crawlers index real pages.
- *Private and per-visitor pages* (account, admin, cart, checkout, design studio) render only in the browser, behind a static loading shell. Their `[Authorize]` attributes are enforced by the storefront router, and the API authorizes every call itself.
- Prerendering runs the storefront's own `ApiClient` on the server. It calls the app over loopback with the visitor's `Cookie` header, so the signed-in header and cart badge render correctly, and it carries a per-process secret header that exempts these calls from rate limiting. There's no second, server-only data path to keep in sync.
- The GET responses used during prerendering are embedded in the page (persistent component state). The browser replays each one once instead of fetching it again, so the page doesn't flash back to a loading state when WebAssembly starts.
- Unknown URLs match the not-found page's catch-all route, which calls `NavigationManager.NotFound()`: crawlers get a real 404 status with the HTML page.

*Trade-off:* the first visit still downloads the .NET runtime (about 3 MB Brotli-compressed, cached afterwards) before pages become interactive, but it no longer blocks content.

### 13. Storefront client design

- **Client contract models.** The storefront has its own models of the public HTTP contract (`Models/ApiModels.cs`) and does not reference server assemblies, which would ship server code to every browser. JSON uses a **source-generated** `JsonSerializerContext`, so it needs no reflection and stays correct when the WebAssembly bundle is trimmed.
- **`ApiClient`** adds the `X-XSRF-TOKEN` header and refreshes the token once on `CSRF_TOKEN_INVALID`. It turns error bodies into `ApiException`, so forms can show field errors next to the matching inputs.
- **State:** `AuthService` (an `AuthenticationStateProvider` backed by `GET /api/auth/session`), `CartState` (a server-priced cart that replaces itself after every mutation), `CatalogCache` (categories and shipping methods) and `ToastService`. `StorefrontServices.AddStorefront()` registers them for both hosts (browser and prerendering server); `PrerenderCache` hands prerendered responses to the browser.
- **Validation:** forms validate in the browser with DataAnnotations for instant feedback. The API remains the authority, and its field errors are mapped back onto the form.
- **Product art:** product illustrations come from a single SVG sprite (`wwwroot/images/art/sprite.svg`). The product body uses `currentColor`, so selecting a colour recolours it live. The static images used by the seed data are generated from the same sprite (`scripts/generate-product-images.ps1`).
- **Accessibility:**
  - skip link, landmarks, focus moved to the `h1` on navigation;
  - visible focus rings and 44 px touch targets;
  - labelled form fields;
  - a polite `aria-live` region for toasts and cart updates;
  - an accessible confirmation dialog (focus on the safe action, Escape cancels);
  - support for `prefers-reduced-motion`.

### 14. Design studio (React) and its contract with the storefront

The studio (`src/SublimaLab.Customizer`) is built by Vite as an ES module library into `wwwroot/customizer`. The Blazor page `/customize/{slug}` loads a small host module (`js/customizer-host.js`), which dynamically imports the studio and mounts it into a container created by JavaScript. Blazor and React therefore never touch each other's DOM nodes, and unmounting is deferred so React isn't torn down mid-render. The studio talks to the API directly, using the same cookie and antiforgery token. It calls back into Blazor only to refresh the cart or to ask for a sign-in.

- **State:** a zustand store per mount holds the design document, the selection and the undo/redo history. Drags, resizes and rotations update the store transiently and commit one undo step per gesture. Components subscribe to narrow slices, so only what changed re-renders.
- **One renderer:** `design/render.ts` draws a print surface onto a canvas. The 2D editor, the 3D textures and the cart thumbnail all use it, so the preview matches the order exactly.
- **Same rules as the server:** placement maths (rotated bounding box, 0.5 mm tolerance), DPI and the document format mirror the C# implementation. The client gives instant feedback; the API decides.
- **No business logic in the browser:** prices shown in the studio are labelled estimates, and the cart response is authoritative.

### 15. 3D preview

- **Models:** generated by `scripts/generate-models.mjs` (three.js geometry plus a minimal GLB writer). This keeps licensing clean and the files small (60–116 KB).
- **Convention:** a `body` mesh is tinted with the selected colour, `detail` meshes stay as modelled, and `print-<key>` meshes have UVs spanning exactly one print area. Node extras record the modelled size, so the viewer rescales a surface when an administrator changes the print-area dimensions.
- **Textures:** each print surface gets a `CanvasTexture` sized once (three.js allocates immutable GPU storage) and repainted in place. The scene is never rebuilt. Rendering is on demand (`frameloop="demand"`), and textures, materials, geometries and the cached GLTF are disposed on unmount.
- **Loading:** three.js, React Three Fiber and drei form one lazily imported chunk, downloaded only when the 3D tab is first shown. A DOM progress overlay, an error boundary and WebGL detection provide graceful fallbacks; the 2D editor and checkout keep working without WebGL.

### 16. Admin

The admin UI is Blazor pages under `/admin`. A folder-level `_Imports.razor` applies the admin layout and the `Admin` role requirement to every page, and the API enforces the `AdminOnly` policy independently.

- Order actions come from the domain state machine (`nextStatuses`), so the UI never offers an invalid transition.
- The production ZIP is assembled server-side from immutable order data. A future rendering service can consume it without the storefront.
- Customer accounts are reached through an `IUserDirectory` port, so the application layer doesn't depend on Identity.

### 17. Operations and performance decisions

- **Retention:** a background worker runs `RetentionService` daily, removing guest uploads unused for 30 days and stale guest carts. Artwork referenced by orders is retained.
- **Keys:** Data Protection keys persist to `App_Data/keys` (a Docker volume).
- **Compression:** Brotli/gzip is applied to static asset MIME types only. API JSON is left uncompressed to avoid BREACH.
- **Caching:** fingerprinted studio chunks are immutable for a year, models and images are cached for a day, and entry points revalidate.
- **First paint:** public pages are prerendered (decision 12); browser-only pages show a static shell that matches the rendered header. Fonts are self-hosted subsets, and globalization is invariant (no ICU download). Lighthouse measurements are in [production-readiness.md](production-readiness.md).
- **Observability:** OpenTelemetry traces, metrics and logs are exported over OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set (`Hosting/TelemetrySetup.cs`): ASP.NET Core, HttpClient, runtime and Npgsql instrumentation, plus business counters (`StoreMetrics`: orders placed, payments by outcome, uploads by outcome).
- **Proxies:** `X-Forwarded-For`/`-Proto` are honoured only from `ReverseProxy:KnownNetworks`/`KnownProxies`. Enabling proxy mode without a trusted range fails at startup, because a spoofed client IP would defeat rate limiting.
- **SEO:** `/robots.txt` and `/sitemap.xml` are generated from configuration and the live catalog; product pages carry schema.org `Product` JSON-LD.

### 18. Two-step verification

Identity's authenticator (TOTP) support, exposed through the API (`/api/account/two-factor*`, `/api/auth/login/2fa`) and the storefront ("Profile & security" tab, plus a code step in sign-in).

- Setup creates a new key each time, shows it as a QR code (rendered server-side as SVG) and a typed key, and turns 2FA on only after the first valid code. It then returns 10 one-time recovery codes.
- A session that completed a second factor carries the `amr=mfa` claim, which is kept when the cookie is revalidated. When `Auth:RequireAdminTwoFactor` is on (the default; off in Development), the `AdminOnly` policy requires it: an admin who signed in with only a password gets 403 `TWO_FACTOR_REQUIRED`, and the admin UI explains how to set it up. Administrators can't turn it off.
- Turning it on or off changes the security stamp, which signs out other sessions, and sends a notification email.

## Security checklist (implemented)

- HTTPS redirection and HSTS (outside Development); `__Host-` cookies.
- Identity password policy (10+ characters, mixed case, digit) and lockout.
- Role-based `AdminOnly` policy, plus two-step verification for administrators. Ownership is checked in every service query.
- Uploaded images are re-encoded before storage (metadata and hidden payloads removed).
- Forwarded headers accepted only from configured proxies.
- Antiforgery on all unsafe requests. CORS allows only configured origins.
- Rate limiting: global, authentication endpoints and uploads.
- Security headers: CSP, `X-Frame-Options: DENY`, `nosniff`, Referrer-Policy, Permissions-Policy, COOP. The `Server` header is removed.
- Request body limit of 1 MB by default; 16 MB only for uploads.
- Input validation (FluentValidation) on every request. JSON formatter errors don't leak type information.
- No card data ever reaches the server (provider tokens only). Secrets come from user-secrets or the environment.
