# Production-readiness review

This review checks SublimaLab against the brief's acceptance criteria and against what a real launch needs. Each item lists what is in place, how it was verified, and what is still open. It was first written for Phase 13, then updated after the hardening round that closed every engineering item. All verification was done on 2026-10-01 against the local build.

## 1. Acceptance criteria

### Customer journey

| Criterion | Status | Verified by |
|---|---|---|
| Visit on desktop or mobile | ✅ | Headless-Edge screenshots at 390 px and 1280–1600 px; no horizontal overflow on any page |
| Browse T-shirts, mugs, keychains | ✅ | `CatalogTests`, browser flow |
| Select a customizable T-shirt | ✅ | Browser flow: product page → *Customize this product* |
| Upload own artwork | ✅ | Studio drag-and-drop or file picker; client checks plus server signature sniffing (`UploadServiceTests`, `SecurityTests`) |
| Add custom text | ✅ | Studio Text tool and templates; 8 licensed fonts |
| Move, resize, rotate | ✅ | Pointer/touch handles, keyboard nudging; undo/redo (`design.test.ts`) |
| Select colour and size | ✅ | Studio product options; the 3D body is re-tinted live |
| Live 3D T-shirt | ✅ | GLB model, with the design rendered as a live canvas texture on the print surfaces |
| Rotate and zoom in 3D | ✅ | Orbit, touch and pinch, plus Front/Back/Zoom/Reset controls |
| Add the customized product to the cart | ✅ | Includes a server-validated design and a 3D thumbnail |
| Review customization | ✅ | Cart shows the preview, the "Your design" badge and an *Edit design* link (reopens the studio in update mode) |
| Checkout with test payment | ✅ | 4-step checkout; the declined-then-approved retry flow is tested in browser and API |
| Order confirmation | ✅ | Confirmation page and confirmation email (`CustomerJourneyTests`) |
| Order history | ✅ | Account → Orders, plus order detail with a tracking timeline |

### Administrator

| Criterion | Status | Verified by |
|---|---|---|
| Log into the admin dashboard | ✅ | `/admin` requires the Admin role and, outside Development, two-step verification; customers see "Access denied" (`TwoFactorTests`) |
| Manage products, variants, pricing | ✅ | Product editor (details, variants, photos); `AdminTests` |
| Configure printable areas | ✅ | Customization tab; the studio and 3D model follow the configured sizes |
| View orders and customer designs | ✅ | Admin order page shows previews, a readable element list and the original artwork |
| Download production artwork | ✅ | ZIP with `design.json`, `production-spec.json`, original uploads and the preview (`AdminTests`) |
| Update order status, add tracking | ✅ | Only valid next statuses are offered (domain state machine); ship form |
| Manage customers | ✅ | List and search, detail with orders and addresses, lock/unlock (sessions revoked) |

## 2. Quality gates

| Gate | Result |
|---|---|
| `dotnet build` (warnings are errors, .NET analyzers, code-style enforcement) | ✅ 0 warnings |
| `dotnet format --verify-no-changes` | ✅ |
| Unit tests (xUnit) | ✅ 102 |
| Integration tests (real HTTP pipeline, migrations, SQLite, prerendering) | ✅ 69 |
| Storefront component tests (bUnit) | ✅ 33 |
| Studio unit tests (Vitest) and TypeScript `tsc --noEmit` (strict) | ✅ 19 |
| End-to-end browser scripts (purchase, studio, admin, two-step verification, prerendering, legal pages; desktop and mobile) | ✅ no console errors and no failed requests |
| `dotnet list package --vulnerable --include-transitive`, `npm audit` | ✅ 0 known vulnerabilities |
| Release publish with WebAssembly trimming | ✅ no trim warnings |
| PostgreSQL migration bundle (`dotnet ef migrations bundle`) | ✅ builds and runs (applying it to a live PostgreSQL server is unverified here) |

## 3. Security review

**In place:**
- **Authentication and authorization:**
  - Cookie auth (`__Host-` prefix, HttpOnly, Secure, SameSite=Lax), with lockout after 5 failed sign-ins.
  - Two-step verification (authenticator app, recovery codes) for every account, required for administrators outside Development.
  - Locking an account revokes its sessions immediately (security stamp).
  - Role policy on every admin endpoint, and ownership checks in every customer query.
- **Request handling:**
  - CSRF tokens required on every unsafe request.
  - Rate limits on authentication, uploads and globally. Forwarded client IPs are accepted only from configured proxies.
  - Request body limit of 1 MB by default, with explicit larger limits only for uploads.
  - All request data validated server-side (FluentValidation); prices and totals are never trusted from the client.
  - Order placement is idempotent and orders use optimistic concurrency.
- **Uploads:**
  - The real file type is detected from the file's contents; dimension and megapixel caps apply.
  - Every image is decoded and re-encoded before storage: metadata, ICC profiles and hidden payloads are removed; corrupt files are rejected.
  - Storage keys are generated by the server and protected against path traversal.
  - Files are served with `nosniff` and a sandboxed Content Security Policy.
- **Browser hardening:**
  - Strict CSP for the storefront: scripts from the same origin only, plus `'wasm-unsafe-eval'`; no inline scripts.
  - `X-Frame-Options: DENY`, Referrer-Policy and Permissions-Policy headers.
  - HSTS outside Development; the `Server` header is removed.
- **Data exposure:** one consistent error format with no stack traces. Antiforgery-bearing JSON responses are never compressed (BREACH).
- **Storage retention:** guest uploads and carts are cleaned up daily; production artwork is retained.
- **Accounts:** registration and recovery responses never reveal whether an email address has an account.

| # | Item | Status |
|---|---|---|
| S1 | Re-encode uploaded images | ✅ Done: `SkiaImageSanitizer` runs inline at upload, with a concurrency limit |
| S2 | Real payment provider | ⏳ **Needs the owner:** Stripe test keys and a webhook secret. Then implement `IPaymentGateway` for Stripe (Payment Element plus a signature-verified webhook marked `[IgnoreAntiforgeryToken]`) |
| S3 | Production admin credentials | ⏳ **Needs hosting:** set `Seed__AdminEmail` / `Seed__AdminPassword` from the platform's secret store, then turn on 2FA at first sign-in (it's enforced) |
| S4 | Shared Data Protection keys | ⏳ **Needs hosting:** single-instance deployments are fine as is (keys persist to a volume). Before scaling out, persist keys to shared storage and protect them at rest |
| S5 | Two-step verification for administrators | ✅ Done |
| S6 | Trust only known proxies | ✅ Done: `ReverseProxy:KnownNetworks/KnownProxies`; misconfiguration fails at startup |

## 4. Performance

**Measured** (Release build, Lighthouse 12, headless Edge, home page):

| Metric | Before Phase 12 | Phase 12 (static shell) | With prerendering |
|---|---|---|---|
| First Contentful Paint (simulated mobile) | 2.6 s | 1.4 s | 1.6 s |
| Total Blocking Time (simulated mobile) | — | ~9 s | **2.5 s** |
| Total Blocking Time (DevTools-throttled mobile) | — | — | **110 ms** |
| Cumulative Layout Shift | 0.611 | 0.001 | **0** |
| Lighthouse performance, mobile (simulated / DevTools throttling) | 19 | 45 / — | 44 / **72** |
| Lighthouse performance, desktop | — | — | **79** (LCP 2.0 s) |
| Accessibility / Best practices / SEO | 100 / 100 / 100 | 100 / 100 / 100 | 100 / 100 / 100 |

Prerendering means public pages now arrive as complete HTML: product data, titles and JSON-LD are visible to crawlers without JavaScript, and the browser makes no extra API calls when WebAssembly starts.

*Why mobile LCP still reads high in the lab:* Blazor doesn't hydrate in place; it replaces the prerendered DOM with identical new nodes when WebAssembly starts. On Lighthouse's slow simulated connection the runtime (about 3 MB) arrives after 15–20 s, and that repaint is counted as the LCP. In the browser, without network throttling, LCP is 0.63 s with 4× CPU slowdown (the prerendered hero paragraph). Real users usually tap or scroll before the swap, which ends LCP measurement.

**Done:**
- Server prerendering of public pages, with prerendered API responses handed to the browser.
- A static loading shell for pages that render only in the browser.
- Self-hosted subset fonts with preloads.
- Invariant globalization (no ICU download).
- Brotli/gzip for static assets.
- A per-asset-type caching policy (immutable fingerprinted chunks).
- Output caching for the catalog, evicted when an administrator changes products.
- The studio and three.js loaded only on `/customize`, with the 3D engine in a separate lazy chunk (about 118 KB and 320 KB compressed respectively).
- Demand-driven WebGL rendering and full GPU resource disposal.

| # | Recommendation | Expected effect |
|---|---|---|
| P1 | Prerendering | ✅ Done |
| P1b | Render home, catalog and product pages as **static SSR with small interactive islands** (header, add to cart), so the WebAssembly runtime isn't needed to keep their DOM | Removes the lab LCP repaint and the runtime download on landing pages; a larger change to how those pages handle interaction |
| P2 | Serve `_framework` and `/customizer` from a CDN with Brotli | Lower latency worldwide |
| P3 | Evaluate WebAssembly AOT for the checkout bundle only | Faster interactions, at a larger download |
| P4 | Add RUM (web-vitals) to measure field LCP/INP | Track real users rather than lab estimates |

## 5. Operations

**In place:**
- `/health/live` and `/health/ready` probes (the database check is tagged `ready`).
- Structured JSON logs in production, with trace ids returned in error bodies.
- OpenTelemetry traces, metrics (including orders, payments and uploads) and logs over OTLP, enabled by `OTEL_EXPORTER_OTLP_ENDPOINT`.
- A PostgreSQL migration bundle (CI artifact and a `migrations` Docker target); docker-compose runs it as a one-off `migrate` service before the API starts.
- Multi-stage Docker image: a Node stage builds the studio, the .NET SDK stage publishes, and the runtime image runs as a non-root user.
- docker-compose with PostgreSQL and a Caddy TLS proxy.
- Migrations per provider, with optional apply-on-startup.
- CI: restore, code-style check, build, vulnerability scan, .NET tests, migration bundle, studio type check and tests, Docker images.

**Not verified here:** the Docker image and compose stack (Docker isn't installed on the development machine), and the PostgreSQL migrations against a live server. Both should be the first CI job in a cloud environment.

| # | Item | Status |
|---|---|---|
| O1 | Blob storage for uploads (S3 or Azure Blob) to scale horizontally | ⏳ **Needs hosting choice.** `IFileStorage` is the only seam to implement; a single instance with a volume works today |
| O2 | Migrations as a deployment step | ✅ Done (migration bundle) |
| O3 | OpenTelemetry export | ✅ Done. Alerting rules (error rate, checkout failures) are set up in whichever backend receives the data |
| O4 | Backups of the database and upload storage | ⏳ **Needs hosting:** managed PostgreSQL backups plus storage snapshots or versioning |
| O5 | Transactional email | ⏳ **Needs the owner:** a provider account. SMTP is implemented (`Email:Provider=Smtp`); add SPF/DKIM for the sending domain |

**Content:** contact, terms, privacy and shipping & returns pages are in place as templates (`Services/StoreInfo.cs` holds the placeholders). The company details and a legal review are required before launch.

## 6. Verdict

The application meets every functional acceptance criterion in the brief, with automated tests and browser verification behind each one, and every engineering item from this review is done. It's ready for a **staging deployment and a closed beta using the test gateway** today.

Going live with real payments needs only items that depend on the store owner:
1. Stripe keys, so the Stripe gateway (S2) can be implemented and tested.
2. An email provider (O5).
3. A hosting choice, which settles secrets, key storage, blob storage and backups (S3, S4, O1, O4), and lets the Docker/PostgreSQL path be verified end to end.
4. The company's legal details and a review of the legal pages.
