# Segment 08 — Controllers, admin surface: review findings

Scope review against the [Segment 08 checklist](CODE_REVIEW_ROADMAP.md#segment-08--controllers--admin-surface) of [CODE_REVIEW_ROADMAP.md](CODE_REVIEW_ROADMAP.md). This segment covers the five admin-facing controllers — [AdminController.cs](backend/RestaurantApi/Controllers/AdminController.cs), [MenuManagementController.cs](backend/RestaurantApi/Controllers/MenuManagementController.cs), [MenuScanController.cs](backend/RestaurantApi/Controllers/MenuScanController.cs), [OrganizationsController.cs](backend/RestaurantApi/Controllers/OrganizationsController.cs), [TablesController.cs](backend/RestaurantApi/Controllers/TablesController.cs) — plus [ProductValidator.cs](backend/RestaurantApi/Validators/ProductValidator.cs) and the request DTOs they bind ([MenuManagementRequests.cs](backend/RestaurantApi/DTO/Requests/MenuManagementRequests.cs), [MenuScanRequests.cs](backend/RestaurantApi/DTO/Requests/MenuScanRequests.cs), [OrganizationRequests.cs](backend/RestaurantApi/DTO/Requests/OrganizationRequests.cs)).

Reviewer: Claude (Opus 4.7). Date: 2026-05-21. Branch reviewed: `main` at `fb59c7e`.

## Verdict

**6 findings.** The admin surface is in solid shape on the load-bearing items: every controller is class-`[Authorize]`, the route `{restaurantId:guid}` is constrained on every action that needs a tenant, the request DTOs deliberately omit `Id` / `RestaurantId` / `CreatedAt` so PATCH/PUT cannot smuggle them, and tenant isolation runs end-to-end through [TenantResolutionMiddleware](backend/RestaurantApi/Tenancy/TenantResolutionMiddleware.cs) + the global query filters from segment 04 / 06. `MenuManagementController.BulkCreate` correctly wraps the multi-step category-merge + product-insert in a transaction with an upfront validation pass. `MenuScanController` enforces a 10-file count, a 5 MB per-file ceiling, and a request-size limit — all client-friendly 413 responses. No `IgnoreQueryFilters`, no `SuppressResolution`, no `async void` anywhere in this segment.

Where it falls short clusters around four themes:

1. **Bulk endpoints are not bounded.** [F1](#f1--high--bulkcreate-has-no-max-batch-size-explicit-checklist-fail) — `BulkCreate` checks `Items.Count == 0` and nothing else. A 28 MB JSON body holds ~190k product entries (default Kestrel `MaxRequestBodySize`); the controller validates them all upfront into a `List`, then opens a single transaction and `Add`s every row. The checklist names this item literally ("enforce a max batch size") and the failure case is an explicit DoS amplifier on a tenant-scoped endpoint.
2. **The directives surface returns EF entities.** [F2](#f2--medium--admincontroller-returns-ef-entities-on-six-endpoints-instead-of-dtos-explicit-checklist-fail) — six endpoints on `AdminController` return the tracked `AIDirective` / `RecommendationDirective` directly from `Ok(...)`. The checklist says "Returning DTOs (not entities)". Today the impact is bounded (no lazy-load proxies are wired up, so no nav-property loop), but `RestaurantId` round-trips to the browser unnecessarily and any future nav property added to either model becomes an immediate response-shape change.
3. **File upload trusts headers.** [F3](#f3--medium--menuscan-trusts-client-supplied-content-type-no-magic-byte-sniffing), [F4](#f4--medium--menuscan-accepts-pdfs-with-no-page-count-limit-checklist-names-no-pdf-zip-bombs) — `MenuScanController` MIME-checks the `Content-Type` header (client-controlled) without magic-byte sniffing, and there is no PDF page-count cap; the checklist names both ("allowed MIME types" and "no PDF zip-bombs"). The 5 MB per-file ceiling does not bound page count — a single 5 MB PDF can hold thousands of compressed pages, each of which the downstream Python service feeds to a paid vision LLM.
4. **Input validation gaps.** [F5](#f5--medium--validateimageurl-accepts-javascript-data-and-file-schemes), [F6](#f6--low--no-named-authorization-policies-defense-in-depth-missing) — `ProductValidator.ValidateImageUrl` calls `Uri.TryCreate(..., UriKind.Absolute, ...)` which accepts `javascript:`, `data:`, and `file:` schemes; the segment's stated goal is "inputs are validated". And `AddAuthorization()` is called with no named policies, so every controller in this segment falls back to "any authenticated caller plus whatever the middleware enforces" — defense-in-depth, not a gaping hole, but the checklist names it.

What does check out: every controller is class-decorated `[Authorize]` ([AdminController.cs:16](backend/RestaurantApi/Controllers/AdminController.cs#L16), [MenuManagementController.cs:13](backend/RestaurantApi/Controllers/MenuManagementController.cs#L13), [MenuScanController.cs:10](backend/RestaurantApi/Controllers/MenuScanController.cs#L10), [OrganizationsController.cs:15](backend/RestaurantApi/Controllers/OrganizationsController.cs#L15), [TablesController.cs:15](backend/RestaurantApi/Controllers/TablesController.cs#L15)); no `[AllowAnonymous]` overrides anywhere in `Controllers/`; route templates use `{restaurantId:guid}` so a non-Guid 404s before model binding ([MenuManagementController.cs:14](backend/RestaurantApi/Controllers/MenuManagementController.cs#L14)); request DTOs deliberately omit `Id` and `RestaurantId` ([MenuManagementRequests.cs:10-15](backend/RestaurantApi/DTO/Requests/MenuManagementRequests.cs#L10-L15), [MenuManagementRequests.cs:27-35](backend/RestaurantApi/DTO/Requests/MenuManagementRequests.cs#L27-L35), [MenuScanRequests.cs:8-13](backend/RestaurantApi/DTO/Requests/MenuScanRequests.cs#L8-L13)); `MenuManagementController` uses `AdminProductResponse` / `AdminCategoryResponse` everywhere ([MenuManagementController.cs:373-384](backend/RestaurantApi/Controllers/MenuManagementController.cs#L373-L384)); status codes are consistent (`400` for validation, `403`/`404` from middleware for cross-tenant, `409` for state conflicts like "category has products" or "table has orders"); `BulkCreate` validates the entire payload upfront before opening the transaction so a single bad row rolls back before any DB writes ([MenuManagementController.cs:394-406](backend/RestaurantApi/Controllers/MenuManagementController.cs#L394-L406)); `MenuScanController` configures `[RequestSizeLimit]` to the count×size product plus 1 MB of slack ([MenuScanController.cs:31](backend/RestaurantApi/Controllers/MenuScanController.cs#L31)); the AI service `HttpClient` is named/typed (no `new HttpClient()`) and gets a 10 s timeout for the directive parsers and a 6 min timeout for the vision extractor ([Program.cs:88-128](backend/RestaurantApi/Program.cs#L88-L128)).

---

## Checklist coverage

| Item | Status | Evidence |
|---|---|---|
| 🔥 Every action has `[Authorize]` or `[AllowAnonymous]` w/ justifying comment | ✓ Pass | Class-level `[Authorize]` on all five controllers (lines cited above). `grep` for `AllowAnonymous` across `Controllers/` returns zero matches. |
| Role-restricted actions use a policy | ❌ Fail — see [F6](#f6--low--no-named-authorization-policies-defense-in-depth-missing) | [Program.cs:80](backend/RestaurantApi/Program.cs#L80) is `AddAuthorization()` with no overload — no named policies exist. `OrganizationsController` uses `_access.IsOrgOwnerAsync(UserId, orgId)` inline ([OrganizationsController.cs:91](backend/RestaurantApi/Controllers/OrganizationsController.cs#L91), [114](backend/RestaurantApi/Controllers/OrganizationsController.cs#L114), [138](backend/RestaurantApi/Controllers/OrganizationsController.cs#L138), [151](backend/RestaurantApi/Controllers/OrganizationsController.cs#L151), [177](backend/RestaurantApi/Controllers/OrganizationsController.cs#L177)) — could be a policy. |
| Route `{restaurantId:guid}` is reconciled against memberships | ✓ Pass | Middleware enforces via [TenantResolutionMiddleware.cs:56-65](backend/RestaurantApi/Tenancy/TenantResolutionMiddleware.cs#L56-L65), which calls `_access.CanAccessRestaurantAsync` (`OrgOwner` of the owning org **or** `RestaurantAdmin` of that restaurant, [TenantAccess.cs:26-39](backend/RestaurantApi/Tenancy/TenantAccess.cs#L26-L39)). Controllers do not override or short-circuit. |
| Bulk endpoints enforce a max batch size and reject mixed-tenant payloads | ❌ Fail (batch size) / ⚠ Implicit (tenant) — see [F1](#f1--high--bulkcreate-has-no-max-batch-size-explicit-checklist-fail) | Max-size: absent ([MenuManagementController.cs:391](backend/RestaurantApi/Controllers/MenuManagementController.cs#L391) only rejects empty). Cross-tenant: foreign-tenant `CategoryId`s fail the `AnyAsync` check at [line 453](backend/RestaurantApi/Controllers/MenuManagementController.cs#L453) (filtered by the global query filter) and surface as 409 `missingCategoryIds`, so payloads cannot land cross-tenant — but the rejection is implicit, not validated upfront. |
| File upload: max size, MIME, no zip-bombs, file count | ⚠ Partial — see [F3](#f3--medium--menuscan-trusts-client-supplied-content-type-no-magic-byte-sniffing), [F4](#f4--medium--menuscan-accepts-pdfs-with-no-page-count-limit-checklist-names-no-pdf-zip-bombs) | Count (10) and per-file size (5 MB): ✓ ([MenuScanController.cs:14-15](backend/RestaurantApi/Controllers/MenuScanController.cs#L14-L15)). MIME allow-list exists but checks the client header ([line 49](backend/RestaurantApi/Controllers/MenuScanController.cs#L49)), not magic bytes. PDF page-count: not enforced. |
| DTOs do not bind sensitive fields (`Id`, `RestaurantId`, `CreatedAt`) | ✓ Pass | Every request DTO inspected omits these. `CreateInvitationRequest.RestaurantId` is intentional — it identifies the target location within the org and is validated against `orgId` at [OrganizationsController.cs:153-156](backend/RestaurantApi/Controllers/OrganizationsController.cs#L153-L156). |
| Returning DTOs, not entities | ❌ Fail — see [F2](#f2--medium--admincontroller-returns-ef-entities-on-six-endpoints-instead-of-dtos-explicit-checklist-fail) | `AdminController` returns entities directly on 6 endpoints ([lines 57, 95, 107, 118, 174, 185](backend/RestaurantApi/Controllers/AdminController.cs#L57)). Every other controller uses response DTOs. |
| `400` for validation, `403` for cross-tenant, `404` for not-found-or-not-owned | ✓ Pass | Cross-tenant: the global query filter returns null on `FindAsync`/`FirstOrDefaultAsync`, producing 404 from `return NotFound()` rather than leaking row existence (e.g. [TablesController.cs:71-72](backend/RestaurantApi/Controllers/TablesController.cs#L71-L72), [MenuManagementController.cs:285-287](backend/RestaurantApi/Controllers/MenuManagementController.cs#L285-L287)). Membership failure short-circuits to 403 at [middleware](backend/RestaurantApi/Tenancy/TenantResolutionMiddleware.cs#L61). Validation is 400 throughout. |
| PATCH/PUT preserve `RestaurantId` | ✓ Pass (via DTO design) | `UpdateProductRequest` / `UpdateCategoryRequest` / `UpdateOrderStatusRequest` do not expose a `RestaurantId` property, so model binding cannot mutate it. NB: this is the *only* defense — see segment 07 [F1](CODE_REVIEW_SEGMENT_07_DOMAIN_MODELS.md#f1--high--savechanges-does-not-block-restaurantid-mutation-on-modified-entries) for the `StampTenant` gap that makes DTO discipline load-bearing. |
| No `async void` methods | ✓ Pass | `grep` for `async void` in `Controllers/` returns zero matches. |

---

## F1 — HIGH — `BulkCreate` has no max batch size (explicit checklist fail)

[MenuManagementController.cs:386-520](backend/RestaurantApi/Controllers/MenuManagementController.cs#L386-L520) enforces only a non-empty floor:

```csharp
if (request.Items.Count == 0)
    return BadRequest(new { message = "At least one item is required." });

// Validate every item upfront so a roll-back happens before any DB writes.
var errors = new List<object>();
for (var i = 0; i < request.Items.Count; i++)
{
    var item = request.Items[i];
    var nameResult = ProductValidator.ValidateName(item.Name);
    if (!nameResult.IsValid) errors.Add(new { index = i, field = "name", message = nameResult.Error });
    ...
}
```

There is no ceiling on `request.Items.Count` and no ceiling on `request.NewCategories.Count`. The endpoint:

1. Loads every existing `Category` row into memory ([line 413](backend/RestaurantApi/Controllers/MenuManagementController.cs#L413): `await _db.Categories.ToListAsync(...)`).
2. Iterates all items, accumulating an error list.
3. Opens a transaction at [line 408](backend/RestaurantApi/Controllers/MenuManagementController.cs#L408).
4. Tracks every `Category` and `Product` for insert into the EF change tracker before a single `SaveChangesAsync`.

The Kestrel default `MaxRequestBodySize` is 30,000,000 bytes; an entry of `{"name":"X","price":1,"categoryId":"<guid>"}` is ~70 bytes, so the controller will happily accept on the order of 400 000 items in one body. The cost shape is roughly: O(items) memory in the change tracker, O(items × categories) DB round-trips for the `maxSortByCategory` lookup loop ([lines 469-475](backend/RestaurantApi/Controllers/MenuManagementController.cs#L469-L475) — once per *unique* category, cached, so actually O(unique categories)), and a single fat `INSERT` batch that locks the `products` / `categories` rows for the duration.

This is a tenant-scoped endpoint — only authenticated members can call it — so the practical risk is "a single malicious or buggy admin client wedges their own tenant's writes". But the segment 08 checklist names this item explicitly:

> Bulk endpoints (`bulk-create-products`) enforce a max batch size and reject mixed-tenant payloads.

The "reject mixed-tenant payloads" half *is* enforced (indirectly): a `CategoryId` from another tenant fails the `_db.Categories.AnyAsync(c => c.Id == realId)` lookup at [line 453](backend/RestaurantApi/Controllers/MenuManagementController.cs#L453) because the query filter scopes it to the current tenant, and the response is `409 missingCategoryIds`. So cross-tenant attempts can't land — but the max-size half is wide open.

[BulkCreateProductsTests.cs](backend/RestaurantApi.Tests/BulkCreateProductsTests.cs) does not have a batch-size test (grep for `MaxBatch` / `too many` / `Items.Count > ` returns zero matches), so this wasn't caught.

Minor adjacent observation: `request.Items` is typed `IReadOnlyList<BulkCreateProductItem>` ([MenuScanRequests.cs:16](backend/RestaurantApi/DTO/Requests/MenuScanRequests.cs#L16)) and is dereferenced at [line 391](backend/RestaurantApi/Controllers/MenuManagementController.cs#L391) without a null check; an empty body deserializes to a record with `null` `Items`, producing a `NullReferenceException` → 500. Not security, but the upfront guard should be `request.Items?.Count is null or 0`.

---

## F2 — MEDIUM — `AdminController` returns EF entities on six endpoints instead of DTOs (explicit checklist fail)

The checklist:

> Returning DTOs (not entities) so EF lazy-load doesn't leak nav properties to JSON.

`AdminController` violates this on every directive endpoint:

| Endpoint | Line | Returned type |
|---|---|---|
| `GET /directives` | [57](backend/RestaurantApi/Controllers/AdminController.cs#L57) | `List<AIDirective>` |
| `POST /directives` | [95](backend/RestaurantApi/Controllers/AdminController.cs#L95) | `AIDirective` |
| `PATCH /directives/{id}` | [107](backend/RestaurantApi/Controllers/AdminController.cs#L107) | `AIDirective` |
| `GET /recommendation-directives` | [118](backend/RestaurantApi/Controllers/AdminController.cs#L118) | `List<RecommendationDirective>` |
| `POST /recommendation-directives` | [174](backend/RestaurantApi/Controllers/AdminController.cs#L174) | `RecommendationDirective` |
| `PATCH /recommendation-directives/{id}` | [185](backend/RestaurantApi/Controllers/AdminController.cs#L185) | `RecommendationDirective` |

Both entities implement `ITenantScoped` ([AIDirective.cs:5](backend/RestaurantApi/Models/AIDirective.cs#L5), [RecommendationDirective.cs:5](backend/RestaurantApi/Models/RecommendationDirective.cs#L5)) so every response leaks the tenant's own `RestaurantId` (informational, since the caller already knows it from the URL — still off-shape).

The acute risk the checklist guards against — nav-property serialization loop via lazy-load proxies — is currently not active: [Program.cs](backend/RestaurantApi/Program.cs) does not call `UseLazyLoadingProxies()`. So a `JsonSerializerException` on cyclic graphs would not fire today. But:

- Adding a nav collection to either entity (e.g. an `IsRevoked` audit trail) immediately changes the JSON shape of all six endpoints.
- The other admin endpoints on this controller (e.g. `GET /similarity-rules` at [line 200](backend/RestaurantApi/Controllers/AdminController.cs#L200), `GET /flash-deals` at [line 417](backend/RestaurantApi/Controllers/AdminController.cs#L417)) project to anonymous types — same controller, two patterns. The directive endpoints are the outliers and should be brought in line with `AdminProductResponse` etc.

Treatment is mechanical: define `AdminDirectiveResponse` and `AdminRecommendationDirectiveResponse` records (mirroring [`AdminProductResponse`](backend/RestaurantApi/DTO/Responses/MenuResponse.cs)) and project at the LINQ layer.

---

## F3 — MEDIUM — `MenuScan` trusts client-supplied `Content-Type`; no magic-byte sniffing

[MenuScanController.cs:49](backend/RestaurantApi/Controllers/MenuScanController.cs#L49):

```csharp
if (!AllowedContentTypes.Contains(photo.ContentType))
    return BadRequest(new { message = $"Unsupported content type '{photo.ContentType}'." });
```

`IFormFile.ContentType` is the `Content-Type` header that the multipart-form-data client sent — entirely client-controlled. A request with a `.exe`, `.svg`, `.zip`, or arbitrary bytes labeled `Content-Type: image/png` passes the check. The bytes are then read into memory ([line 51-53](backend/RestaurantApi/Controllers/MenuScanController.cs#L51-L53)) and forwarded to the Python AI service ([line 62](backend/RestaurantApi/Controllers/MenuScanController.cs#L62)).

The segment 08 checklist names this item:

> File upload endpoints (`MenuScanController`) enforce: max size, allowed MIME types, no PDF zip-bombs, file count limit.

"Allowed MIME types" admits two readings: header-based (what's implemented) or content-based. In an OWASP / production context the content-based reading is the load-bearing one — the header is trivially forged.

What practically goes wrong here:

- Bytes arrive at the Python `/menu/extract` (segment 14/15) labeled `image/png`. If `vision_extractor.py` calls a vision LLM, the provider rejects bad bytes — bounded blast radius.
- If a future change calls Pillow / pypdf / unoconv on the bytes before the LLM hop, every well-known image-library CVE comes into scope.
- Application logs go through this code path: [line 73-80](backend/RestaurantApi/Controllers/MenuScanController.cs#L73-L80) emits `EventId(7001, "MenuScan")` with the file count and total bytes — useful for forensics, but doesn't include a content-type-vs-bytes mismatch signal.

The cheap fix is sniffing the first 8-12 bytes against a small table (`FF D8 FF` for JPEG, `89 50 4E 47` for PNG, `52 49 46 46 ... 57 45 42 50` for WebP, `25 50 44 46` for PDF — HEIC's `ftypheic` box sits at offset 4) and rejecting on mismatch. The existing per-file loop at [line 44](backend/RestaurantApi/Controllers/MenuScanController.cs#L44) already touches every byte stream, so the cost is negligible.

[MenuScanControllerTests.cs](backend/RestaurantApi.Tests/MenuScanControllerTests.cs) sends literal strings as PNGs with `Content-Type: image/png` set — and they pass ([line 51](backend/RestaurantApi.Tests/MenuScanControllerTests.cs#L51), [line 87](backend/RestaurantApi.Tests/MenuScanControllerTests.cs#L87)). The tests are written *to* the current behavior, which confirms the gap rather than catching it.

---

## F4 — MEDIUM — `MenuScan` accepts PDFs with no page-count limit (checklist names "no PDF zip-bombs")

The byte-size check at [MenuScanController.cs:46-48](backend/RestaurantApi/Controllers/MenuScanController.cs#L46-L48) caps each file at 5 MB. A 5 MB PDF can hold thousands of pages because PDF's content streams are zlib-compressed (default `/FlateDecode`); a fitted single-glyph page can compress to under 200 bytes. The empirical ceiling on a small-text PDF is roughly 5 MB → 20-30k pages.

The downstream Python service ([backend/ai-scripts/routers/menu_extraction.py](backend/ai-scripts/routers/menu_extraction.py), reviewed in segment 15) is what paginates and dispatches one vision LLM call per page. If the API gateway accepts 10 × 5 MB PDFs, the worst case is on the order of 10 × 30 000 = 300 000 paid vision-LLM calls per request. The segment 15 checklist asks for a page-count cap there too, but the segment 08 checklist names it here:

> File upload endpoints (`MenuScanController`) enforce: max size, allowed MIME types, **no PDF zip-bombs**, file count limit.

The fix at this layer is a page-count peek — `pdf-parse`'s C# equivalent is `PdfPig` or `iText`, but a header sniff for the `/Count <N>` page-tree entry suffices for a fast rejection without pulling a full PDF parser. Or push the responsibility into segment 15 and document the assumption here.

Adjacent observation: the `application/pdf` allow-list is the same allow-list as images ([line 16-19](backend/RestaurantApi/Controllers/MenuScanController.cs#L16-L19)) — a single shared `AllowedContentTypes` for both image and PDF. If PDFs need stricter handling (page-count cap, magic-byte check), the structure invites it: split into `AllowedImageTypes` / `AllowedDocumentTypes` and route to different validators.

---

## F5 — MEDIUM — `ValidateImageUrl` accepts `javascript:`, `data:`, and `file:` schemes

[ProductValidator.cs:39-47](backend/RestaurantApi/Validators/ProductValidator.cs#L39-L47):

```csharp
public static ValidationResult<string?> ValidateImageUrl(string? raw)
{
    if (string.IsNullOrWhiteSpace(raw)) return ValidationResult<string?>.Ok(null);
    if (raw.Length > ImageUrlMaxLength)
        return ValidationResult<string?>.Fail($"ImageUrl must be at most {ImageUrlMaxLength} characters.");
    if (!Uri.TryCreate(raw, UriKind.Absolute, out _))
        return ValidationResult<string?>.Fail("ImageUrl must be a valid absolute URL.");
    return ValidationResult<string?>.Ok(raw);
}
```

`Uri.TryCreate(_, UriKind.Absolute, _)` returns `true` for any RFC 3986 absolute URI, including `javascript:alert(1)`, `data:text/html,<script>...</script>`, and `file:///etc/passwd`. The function is called from `CreateProduct` ([MenuManagementController.cs:234](backend/RestaurantApi/Controllers/MenuManagementController.cs#L234)), `UpdateProduct` ([line 319](backend/RestaurantApi/Controllers/MenuManagementController.cs#L319)), `CreateCategory` ([line 85](backend/RestaurantApi/Controllers/MenuManagementController.cs#L85)), and `UpdateCategory` ([line 138](backend/RestaurantApi/Controllers/MenuManagementController.cs#L138)).

Downstream render path (out of scope here, in scope for segments 18 / 22): the admin SPA's `MenuManagement.vue` and the customer SPA both bind `imageUrl` to `<img :src>`. Modern browsers refuse to navigate `<img>` to `javascript:` / non-`data:` non-http schemes, so the JPG-vs-XSS path is mostly blocked at the renderer. But:

- If the URL ever migrates to a non-`<img>` context (e.g. an anchor `<a :href>` for "view full image", or an inline `style="background-image:url(...)"`), `javascript:` is live.
- A `data:image/svg+xml;base64,...` URL embeds arbitrary SVG, which permits `<script>` in some renderers.
- A `file://` URL has no XSS value on a remote target but can confuse internal admin tooling.

The validator is the right layer to reject these — Vue's escaping defends one renderer, not the contract. Restrict to `https`/`http` (and optionally `data:image/...` if data URIs are intentional for the import wizard) via `Uri.TryCreate(..., out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)`.

Note: the segment 08 checklist does not name URL-scheme validation specifically; the goal "inputs are validated" is broad. Flagging this here because it sits on the controller validation seam and is cheap to harden.

---

## F6 — LOW — No named authorization policies; defense-in-depth missing

The checklist:

> Role-restricted actions use a policy (e.g. `[Authorize(Policy = "RestaurantAdmin")]`), not string-comparison inside the method.

The actual state:

- [Program.cs:80](backend/RestaurantApi/Program.cs#L80): `builder.Services.AddAuthorization();` — no named policies registered.
- Every `[Authorize]` attribute falls back to "any authenticated principal".
- Tenant membership is enforced one layer up at [TenantResolutionMiddleware.cs:56-65](backend/RestaurantApi/Tenancy/TenantResolutionMiddleware.cs#L56-L65), which 403s any caller whose memberships don't include the `restaurantId` in the route. That covers the practical attack ("user A reaches user B's admin endpoints").
- `OrganizationsController` differentiates `OrgOwner` vs `RestaurantAdmin` via inline `if (!await _access.IsOrgOwnerAsync(UserId, orgId)) return Forbid();` ([line 91](backend/RestaurantApi/Controllers/OrganizationsController.cs#L91), [114](backend/RestaurantApi/Controllers/OrganizationsController.cs#L114), [138](backend/RestaurantApi/Controllers/OrganizationsController.cs#L138), [151](backend/RestaurantApi/Controllers/OrganizationsController.cs#L151), [177](backend/RestaurantApi/Controllers/OrganizationsController.cs#L177)) — five repeats of the same string-comparison-shaped check that a policy collapses into one attribute.

Why this is LOW, not MEDIUM:

- The middleware *is* enforcing the same boundary the checklist's policy would enforce. Removing the middleware (or routing past it via a non-`restaurantId` URL) would be required for the gap to bite.
- `AdminController`, `MenuManagementController`, `MenuScanController`, `TablesController` are all uniformly accessible to "any member" — there is no `RestaurantAdmin`-vs-`OrgOwner` distinction designed in, so policies wouldn't *gate* anything; they would *document* the intent and survive a future refactor that drops the middleware.

Why it's worth flagging at all: the checklist names it, and the inline `IsOrgOwnerAsync` repetition is a code-smell that a single `[Authorize(Policy = "OrgOwner")]` would erase. Treat as cleanup, not a security gap.

---

## Cross-segment notes

- The PATCH/PUT pass-through in this segment leans entirely on DTO discipline (no `RestaurantId` field on `Update*Request`). Segment 07 [F1](CODE_REVIEW_SEGMENT_07_DOMAIN_MODELS.md#f1--high--savechanges-does-not-block-restaurantid-mutation-on-modified-entries) is the corresponding gap on the persistence side: even with clean DTOs here, a service or future endpoint that sets `entity.RestaurantId = otherTenant` and saves will not be blocked by `StampTenant`. The two findings compound: harden either side and the surface narrows.
- The `Invitation.Token` returned by `OrganizationsController.ListInvitations` ([line 145](backend/RestaurantApi/Controllers/OrganizationsController.cs#L145), [InvitationResponse.Token](backend/RestaurantApi/DTO/Responses/OrganizationResponses.cs)) inherits segment 07 [F5](CODE_REVIEW_SEGMENT_07_DOMAIN_MODELS.md#f5--medium--qrtoken-is-a-guid-122-bits-checklist-asks-for-128)'s 122-bit `Guid` entropy concern (same generator used for `QrToken`). Listed for tracking, not a new finding.
- `AdminController.TriggerFlashDeal` ([line 437-442](backend/RestaurantApi/Controllers/AdminController.cs#L437-L442)) accepts `[FromBody] Guid orderId` with no explicit tenant check. The call delegates to `FlashDealService.EvaluateEligibilityAsync` which reads `_context.Orders.FirstOrDefaultAsync(o => o.Id == orderId)` ([FlashDealService.cs:92-94](backend/RestaurantApi/Services/FlashDealService.cs#L92-L94)) — the global query filter scopes it, so a foreign `orderId` short-circuits at the `order == null` return. The tenant boundary holds; verified, not a finding.

---

## Sign-off

- 🔥 items in this segment: one (`[Authorize]` coverage). Outcome: **pass**.
- Other findings tracked above as `[REV-08]` candidates: F1 (high), F2-F5 (medium), F6 (low).
- Verification commands from the roadmap ([Segment 08](CODE_REVIEW_ROADMAP.md#segment-08--controllers--admin-surface)) are not listed in the roadmap entry for this segment — none run.
