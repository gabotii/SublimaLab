# SublimaLab API guide

The interactive reference is Swagger UI at `/swagger` (Development only). The OpenAPI document is served at `/openapi/v1.json`.

## Conventions

- JSON uses camelCase. Enums are strings (`"PaymentPending"`). Dates are UTC ISO-8601. Money is a decimal in the store currency (`currency` field).
- IDs are UUIDs.
- List endpoints return `{ items, page, pageSize, totalCount, totalPages, hasNextPage }`.

### Authentication and CSRF

1. `GET /api/antiforgery/token` returns `{ token, headerName }`.
2. Send `X-XSRF-TOKEN: <token>` on every `POST`, `PUT` and `DELETE`.
3. `POST /api/auth/login` sets the HttpOnly auth cookie and returns `{ user, antiforgery, requiresTwoFactor }`. Use the new token from then on. Logout also returns a new token.
4. If the account has two-step verification, login returns `user: null` and `requiresTwoFactor: true` instead, and sets a 5-minute `__Host-sl.2fa` cookie. Post the code to `POST /api/auth/login/2fa` to finish signing in.

Guests receive an anonymous session cookie the first time they add to the cart or upload. On login, that cart and those uploads move into the account.

### Errors

Every error uses this body:

```json
{
  "code": "VALIDATION_ERROR",
  "message": "One or more fields are invalid.",
  "errors": { "contact.email": ["A valid email address is required."] },
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
}
```

| Status | Typical codes |
|---|---|
| 400 | `VALIDATION_ERROR`, `CSRF_TOKEN_INVALID` |
| 401 | `AUTHENTICATION_REQUIRED`, `INVALID_CREDENTIALS` |
| 403 | `FORBIDDEN`, `EMAIL_NOT_CONFIRMED`, `ACCOUNT_LOCKED`, `TWO_FACTOR_REQUIRED` (admin session signed in without a second factor) |
| 404 | `NOT_FOUND` (also returned for resources owned by someone else) |
| 409 | `UPLOAD_IN_USE`, `PAYMENT_ALREADY_FINALIZED`, `CONCURRENCY_CONFLICT`, `TWO_FACTOR_ALREADY_ENABLED` |
| 422 | `CART_EMPTY`, `DISCOUNT_INVALID`, `ORDER_NOT_PAYABLE`, `INVALID_STATUS_TRANSITION`, ... |
| 429 | `RATE_LIMITED` (sent with a `Retry-After` header) |

## Endpoints

### Catalog (public, cached for 60 s)

| Method | Path | Notes |
|---|---|---|
| GET | `/api/categories` | Active categories with product counts |
| GET | `/api/products` | Query parameters: `search`, `category` (slug), `minPrice`, `maxPrice`, `customizable`, `featured`, `sort` (`Featured`, `Newest`, `PriceAsc`, `PriceDesc`, `Name`), `page`, `pageSize` (max 48) |
| GET | `/api/products/{idOrSlug}` | Variants, images, specifications, customization rules (print areas, fonts, fees) |
| GET | `/api/shipping/methods` | Active shipping methods |

### Auth and account

| Method | Path | Auth |
|---|---|---|
| POST | `/api/auth/register` `{ email, password, firstName, lastName }` | public, rate limited |
| POST | `/api/auth/login` `{ email, password, rememberMe }` | public, rate limited |
| POST | `/api/auth/login/2fa` `{ code, rememberMe, isRecoveryCode }` | after a login that returned `requiresTwoFactor`; rate limited |
| POST | `/api/auth/logout` | any |
| GET | `/api/auth/session` returns `{ user }` (`null` when signed out, always 200) | any |
| GET | `/api/auth/me` | user |
| POST | `/api/auth/confirm-email` `{ userId, token }` | public |
| POST | `/api/auth/resend-confirmation` `{ email }` | public (always returns 202) |
| POST | `/api/auth/forgot-password` `{ email }` | public (always returns 202) |
| POST | `/api/auth/reset-password` `{ email, token, newPassword }` | public |
| GET/PUT | `/api/account/profile` | user |
| POST | `/api/account/change-password` | user |
| GET | `/api/account/two-factor` returns `{ enabled, recoveryCodesLeft, required }` | user |
| POST | `/api/account/two-factor/setup` returns `{ sharedKey, authenticatorUri, qrCodeSvg }` (a new key each time; stays off until enabled) | user |
| POST | `/api/account/two-factor/enable` `{ code }` returns `{ recoveryCodes }` (10 codes, shown once) | user, rate limited |
| POST | `/api/account/two-factor/disable` `{ code }` (authenticator or recovery code; refused for administrators while it's required) | user, rate limited |
| POST | `/api/account/two-factor/recovery-codes` `{ code }` returns new `{ recoveryCodes }` | user, rate limited |
| GET/POST | `/api/account/addresses` | user |
| PUT/DELETE | `/api/account/addresses/{id}` | user |

### Uploads

| Method | Path | Notes |
|---|---|---|
| POST | `/api/uploads` | `multipart/form-data` with fields `file` (PNG/JPG/WebP) and `kind` (`Artwork` or `Preview`). Returns `{ id, url, width, height, qualityWarning }` |
| GET | `/api/uploads/{id}` | Metadata (owner or admin) |
| GET | `/api/uploads/{id}/content` | Image bytes (owner or admin) |
| DELETE | `/api/uploads/{id}` | Owner only; returns 409 if the image is used by a design, cart item or order |

### Designs

| Method | Path | Auth |
|---|---|---|
| POST | `/api/designs/validate` `{ productId, design }` returns `{ isValid, errors, warnings }` | public |
| GET/POST | `/api/designs` | user |
| GET/PUT/DELETE | `/api/designs/{id}` | user (owner) |

### Cart (guest or user)

| Method | Path | Body |
|---|---|---|
| GET | `/api/cart?shippingMethod=standard` | |
| POST | `/api/cart/items` | `{ productId, variantId, quantity, design?, designId?, previewUploadId? }` |
| PUT | `/api/cart/items/{id}` | `{ quantity, variantId?, design?, previewUploadId? }` |
| DELETE | `/api/cart/items/{id}` | |
| POST/DELETE | `/api/cart/discount` | `{ code }` |

Every cart response includes server-computed `unitPrice`, `customizationPrice`, `lineTotal` and `totals { subtotal, discount, shipping, tax, total }`.

### Orders and payments (user)

| Method | Path | Body |
|---|---|---|
| POST | `/api/orders` | `{ idempotencyKey, contact { fullName, email, phone }, shippingAddress { line1, line2?, city, region, postalCode, country }, shippingMethodCode, saveAddress }` |
| GET | `/api/orders?page=&pageSize=` | |
| GET | `/api/orders/{id}` | Includes items, the designs as ordered, payments, shipment and timeline |
| POST | `/api/orders/{id}/cancel` | Only while `Pending` or `PaymentPending` |
| POST | `/api/payments` | `{ orderId }` starts a payment and returns a `clientSecret` for providers that need one |
| POST | `/api/payments/{id}/confirm` | `{ paymentToken }` |

Test gateway tokens:

| Token | Result |
|---|---|
| `tok_test_success` (or no token) | Payment succeeds |
| `tok_test_declined` | Payment fails: card declined |
| `tok_test_insufficient_funds` | Payment fails: insufficient funds |

A failed payment leaves the order payable. Start a new payment to retry.

Order lifecycle:

```
Pending → PaymentPending → Paid → InProduction → ReadyToShip → Shipped → Delivered
   └──────────┴──────────────┴→ Cancelled
```

### Admin (role `Admin`)

When `Auth:RequireAdminTwoFactor` is on (the default outside Development), admin endpoints also require a session that signed in with two-step verification; other sessions get 403 `TWO_FACTOR_REQUIRED`.

| Method | Path | Body / notes |
|---|---|---|
| GET | `/api/admin/dashboard` | Revenue (all time and 30 days), daily sales, pipeline counts, best sellers, recent orders |
| GET | `/api/admin/orders?status=&search=&page=&pageSize=` | |
| GET | `/api/admin/orders/{id}` | `{ order, customerId, nextStatuses, canShip, hasCustomization }` |
| PUT | `/api/admin/orders/{id}/status` | `{ status, note? }`; only valid transitions are accepted |
| POST | `/api/admin/orders/{id}/ship` | `{ carrier, trackingNumber }` |
| GET | `/api/admin/orders/{id}/artwork` | Production ZIP: `README.txt`, `order.json`, and per item `design.json`, `production-spec.json`, `artwork/*` and the preview |
| GET | `/api/admin/categories` | All categories, including inactive ones |
| GET | `/api/admin/products?search=&categoryId=&active=&page=` | |
| GET / POST | `/api/admin/products`, `/api/admin/products/{id}` | Create: `{ categoryId, name, slug, shortDescription?, description?, basePrice, isActive, isFeatured, productionTimeDays, specifications? }` |
| PUT | `/api/admin/products/{id}` | Same body as create |
| PUT | `/api/admin/products/{id}/status` | `{ isActive }`; activation requires an active variant |
| PUT | `/api/admin/products/{id}/variants` | `{ variants: [{ id?, sku, colorName, colorHex, size?, priceAdjustment, isActive }] }`. Variants left out are deactivated, never deleted |
| PUT | `/api/admin/products/{id}/customization` | `{ isEnabled, kind, modelUrl?, baseFee, additionalPrintAreaFee, maxElementsPerArea, minRecommendedDpi, printAreas: [{ key, name, widthMm, heightMm, shape }] }` |
| POST | `/api/admin/products/{id}/images` | Multipart `file` (PNG/JPG/WebP, up to 8 MB) and optional `altText` |
| DELETE | `/api/admin/products/{id}/images/{imageId}` | |
| GET | `/api/admin/customers?search=&page=` | Accounts with order count and total spent |
| GET | `/api/admin/customers/{id}` | Profile, orders, addresses |
| PUT | `/api/admin/customers/{id}/lock` | `{ locked }`. Locking signs the customer out everywhere; admins can't be locked |

Every product change evicts the storefront's catalog output cache. Admins can read any upload through `/api/uploads/{id}/content`; this is how they view customer artwork.

### Public media

| Method | Path | Notes |
|---|---|---|
| GET | `/api/product-images/{id}` | Product photos uploaded by administrators (cached for one day, sandboxed CSP) |

### Crawlers

| Method | Path | Notes |
|---|---|---|
| GET | `/robots.txt` | Disallows private areas (`/account`, `/admin`, `/cart`, `/checkout`, `/customize/`, `/api/`) and links the sitemap |
| GET | `/sitemap.xml` | Home, shop, help pages, active categories and products (with `lastmod`); cached with the catalog |

### Health

`GET /health/live` reports liveness. `GET /health/ready` also checks the database.

## Design document schema (v1)

```json
{
  "schemaVersion": 1,
  "surfaces": [
    {
      "printArea": "front",
      "elements": [
        { "type": "image", "id": "art-1", "uploadId": "0199...", "x": 150, "y": 200, "width": 200, "height": 250, "rotation": 0 },
        { "type": "text", "id": "txt-1", "text": "SublimaLab", "fontFamily": "Poppins", "fontSize": 32,
          "color": "#8B2CF5", "bold": true, "italic": false, "align": "Center", "letterSpacing": 0.05,
          "x": 150, "y": 50, "width": 180, "height": 24, "rotation": -5 }
      ]
    }
  ]
}
```

- `x` and `y` give the element's **centre** in millimetres from the print area's top-left corner. `width` and `height` are the unrotated box in mm. `rotation` is in degrees, clockwise.
- Element order is layer order; the first element is the bottom layer.
- The rotated bounding box must fit inside the print area (0.5 mm tolerance), and `printArea` must match a key of the product's print areas.
- Fonts must come from `customization.fonts`. Colours are `#RRGGBB`. Text is limited to 120 characters.
- Images whose effective resolution is below the product's `minRecommendedDpi` produce a `LOW_RESOLUTION` **warning**, not an error.
