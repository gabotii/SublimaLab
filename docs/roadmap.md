# Roadmap

| Phase | Scope | Status |
|---|---|---|
| 1 | Architecture and solution setup | ✅ Done |
| 2 | Domain models and database (EF Core, migrations, seed data) | ✅ Done |
| 3 | API and authentication | ✅ Done |
| 4 | Product catalog (storefront and admin management) | ✅ Done |
| 5 | Blazor storefront | ✅ Done |
| 6 | React customization studio | ✅ Done |
| 7 | Three.js 3D visualization | ✅ Done |
| 8 | Cart and checkout | ✅ Done |
| 9 | Orders and customer accounts (including saved designs) | ✅ Done |
| 10 | Admin dashboard | ✅ Done |
| 11 | Testing and security hardening | ✅ Done (192 automated tests; dependency scans clean) |
| 12 | Performance and responsive optimization | ✅ Done (see production-readiness §4 for measurements) |
| 13 | Production-readiness review | ✅ Done ([production-readiness.md](production-readiness.md)) |

## What each phase delivered

- **Phase 6, design studio** (`src/SublimaLab.Customizer`, React + TypeScript):
  - Artwork upload: drag and drop or picker, client checks, server sniffing, low-resolution warnings, replace.
  - Text with 8 fonts, color, bold, italic, alignment and letter spacing; ready-made templates.
  - Drag, resize and rotate with mouse, touch or keyboard; layers, duplicate, delete, undo/redo.
  - Print-area guides with out-of-bounds feedback; live estimate; Save design; Add to cart or Update cart item.
- **Phase 7, 3D preview:**
  - GLB models generated in-repo for tees, mugs and keychains; the design is painted as a live canvas texture.
  - Orbit, touch and pinch; Front/Back/Zoom/Reset controls; studio lighting and contact shadows.
  - Lazy-loaded chunk, demand rendering, full disposal of GPU resources; thumbnail capture for the cart.
- **Phase 10, admin:**
  - Dashboard with a 30-day revenue chart and its table view, pipeline tiles and best sellers.
  - Orders: filters, details, valid status transitions, shipping with tracking, production artwork ZIP.
  - Products: create and edit, variants, pricing, print areas and customization, photos, activation.
  - Customers: search, details, lock/unlock.
- **Phase 11, testing and security:**
  - Admin API tests, retention tests, studio unit tests.
  - Daily retention job for guest uploads and carts; persistent Data Protection keys.
  - Vulnerability scans in CI.
- **Phase 12, performance:**
  - Static app shell (CLS from 0.61 to 0.001); self-hosted subset fonts; invariant globalization.
  - Brotli/gzip for static assets; caching per asset type; output cache with eviction.
  - Lighthouse accessibility, best practices and SEO all at 100.

## Production hardening (after the 13 phases)

| Item | Status |
|---|---|
| Image re-encoding (metadata and hidden payloads removed) | ✅ Done |
| Two-step verification (authenticator app), required for admins | ✅ Done |
| Trusted proxies only for forwarded headers | ✅ Done |
| OpenTelemetry traces, metrics and logs (OTLP) | ✅ Done |
| Migration bundle as a deployment step | ✅ Done |
| Server prerendering of public pages | ✅ Done |
| Contact, terms, privacy, shipping & returns pages (templates) | ✅ Done |
| robots.txt, sitemap.xml, product JSON-LD | ✅ Done |

## Needs the store owner

See [production-readiness.md](production-readiness.md) §6:

- **Stripe keys** for the real payment gateway.
- **An email provider** account.
- **Hosting**, which settles secrets, shared key storage, blob storage and backups.
- **Company details and a legal review** for the legal pages.

## Next steps (post-launch backlog)

1. **Landing-page performance:** static SSR with interactive islands for home, catalog and product pages (P1b), a CDN for the WebAssembly runtime (P2), and real-user metrics (P4).
2. **Accounts:** self-service account deletion and data export.
3. **Product growth:**
   - Production-file rendering service (print-ready PNG/PDF from `design.json` + `production-spec.json`).
   - More product types (configuration plus a GLB model).
   - Admin-managed discount codes and shipping methods.
