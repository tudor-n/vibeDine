# Segment 11 — Email subsystem: review findings

Scope review against the [Segment 11 checklist](CODE_REVIEW_ROADMAP.md#segment-11--email-subsystem) of [CODE_REVIEW_ROADMAP.md](CODE_REVIEW_ROADMAP.md). The subsystem is four files in [backend/RestaurantApi/Services/Email/](backend/RestaurantApi/Services/Email/) — [IEmailSender.cs](backend/RestaurantApi/Services/Email/IEmailSender.cs), [SmtpEmailSender.cs](backend/RestaurantApi/Services/Email/SmtpEmailSender.cs), [SmtpOptions.cs](backend/RestaurantApi/Services/Email/SmtpOptions.cs), [AppUrlOptions.cs](backend/RestaurantApi/Services/Email/AppUrlOptions.cs) — plus its sole caller, [AuthController.SendConfirmationEmailAsync](backend/RestaurantApi/Controllers/AuthController.cs#L222-L265), DI wiring in [Program.cs:82-85](backend/RestaurantApi/Program.cs#L82-L85), and the dev-time SMTP config in [appsettings.Development.json:19-29](backend/RestaurantApi/appsettings.Development.json#L19-L29) and [docker-compose.yml:35-47, 51-55](docker-compose.yml#L35-L55).

Reviewer: Claude (Opus 4.7). Date: 2026-05-21. Branch reviewed: `main` at `fb59c7e`.

## Verdict

**7 findings.** The 🔥 checklist item passes cleanly: confirmation links are built from `_appUrls.AdminBaseUrl` ([AuthController.cs:229-230](backend/RestaurantApi/Controllers/AuthController.cs#L229-L230)), the `Host`/`Origin` request headers are never read in the email path, and `Request.Scheme` / `Request.Host` appear nowhere near `SendEmailAsync` (grep confirms). The two free-form interpolations in the body — `user.FullName` and the link — are safe: `FullName` is run through `System.Net.WebUtility.HtmlEncode` ([line 236](backend/RestaurantApi/Controllers/AuthController.cs#L236)), the link's variable parts are a `Guid` (`user.Id`) and a `WebEncoders.Base64UrlEncode`'d token ([line 227](backend/RestaurantApi/Controllers/AuthController.cs#L227)) — neither can contain `<`, `"`, `&`, or `\n`. SMTP credentials live in `SmtpOptions.Username`/`Password` ([SmtpOptions.cs:9-10](backend/RestaurantApi/Services/Email/SmtpOptions.cs#L9-L10)) and are bound from `Smtp__*` env vars; nothing is hard-coded. The send is fully awaited inside the controller — not fire-and-forget — and `MailKit` cleanly handles `Disconnect(true, ct)` on the happy path ([SmtpEmailSender.cs:41](backend/RestaurantApi/Services/Email/SmtpEmailSender.cs#L41)).

The findings cluster around three themes:

1. **The transport is unguarded for production.** [F1](#f1--high--smtp-transport-defaults-to-plaintext-no-startup-guard-flags-unsafe-tls-outside-development-) — `SmtpOptions` defaults `UseStartTls = false` and `UseSsl = false` ([SmtpOptions.cs:11-12](backend/RestaurantApi/Services/Email/SmtpOptions.cs#L11-L12)); the only config file in the repo is [appsettings.Development.json](backend/RestaurantApi/appsettings.Development.json) (there is no `appsettings.json`), and the docker-compose env block hard-codes `Smtp__UseStartTls: "false"` ([docker-compose.yml:39-40](docker-compose.yml#L39-L40)) for the dev Mailpit target. Nothing in `Program.cs` validates that `UseStartTls || UseSsl` once `env.IsDevelopment() == false` — the roadmap calls this out explicitly. A prod deployment that simply mirrors compose env-var names will send credentials and confirmation tokens in cleartext.
2. **One missed email is one locked-out user.** [F2](#f2--high--register--resendconfirmation-always-respond-confirmation_sent-even-when-smtp-throws) — `SendConfirmationEmailAsync` wraps the SMTP call in `try { ... } catch (Exception ex) { _logger.LogError(...) }` ([AuthController.cs:256-264](backend/RestaurantApi/Controllers/AuthController.cs#L222-L265)) and never signals upstream. `Register` and `ResendConfirmation` both still return `200 { "Status": "confirmation_sent", ... "Ți-am trimis un nou link de confirmare pe email." }` ([line 55-60](backend/RestaurantApi/Controllers/AuthController.cs#L55-L60), [line 79-84](backend/RestaurantApi/Controllers/AuthController.cs#L79-L84), [line 126](backend/RestaurantApi/Controllers/AuthController.cs#L126)). The account is created, marked unconfirmed, and the user cannot log in (`Login` requires `EmailConfirmed`, [line 136-144](backend/RestaurantApi/Controllers/AuthController.cs#L136-L144)) — but the API claims success. [F3](#f3--medium--no-retry-or-outbox-a-transient-smtp-failure-loses-the-email-permanently) — there is no retry/outbox; one transient `SocketException` and the link is gone forever (the roadmap's `↳` follow-up question, answered: yes, one missed email is gone forever).
3. **Email coverage is incomplete by design.** [F4](#f4--medium--invitation-flow-creates-the-row-but-never-emails-the-invitee) — `OrganizationsController.InviteCoOwner` creates the `Invitation` row and returns `AcceptPath = "/accept-invite?token={i.Token}"` to **the inviter only** ([OrganizationsController.cs:158-171](backend/RestaurantApi/Controllers/OrganizationsController.cs#L158-L171), [line 214](backend/RestaurantApi/Controllers/OrganizationsController.cs#L214)); the invitee receives nothing from the system. [Invitation.cs:6-7](backend/RestaurantApi/Models/Invitation.cs#L6-L7) documents this as intentional ("Accepted via `Token`; no email infrastructure required (copyable link)"), but that comment predates `IEmailSender` landing in `21c59f8`. Once the SMTP path exists, the design rationale is gone — the segment-11 review naturally raises whether the invitee should now be emailed.

The remaining low-severity items are operational hygiene: [F5](#f5--low--subject-and-body-are-hard-coded-romanian-regardless-of-the-recipients-locale) — the entire email (subject and body) is Romanian only, even though the app ships RO/EN parity in the frontend (segment 20); [F6](#f6--low--html-only-message-no-plain-text-alternative) — the message has no `text/plain` alternative, which raises spam scoring and breaks accessibility tooling; [F7](#f7--low--mailpits-smtp-port-1025-is-published-to-the-host-not-only-the-ui-port-8025) — `docker-compose.yml` publishes Mailpit's SMTP listener `1025:1025` to the host alongside the intended UI `8025:8025`, contradicting the Segment 01 checklist item ("Mailpit only exposes port 8025 to the host network").

What does check out, beyond the verdict opener: `SmtpEmailSender` is registered `Scoped` ([Program.cs:85](backend/RestaurantApi/Program.cs#L85)) — fine for the once-per-request usage and matches the lifetime of `AppDbContext` if the sender is ever called inside a unit-of-work. `MailKit`'s `SmtpClient` is correctly disposed via `using` ([SmtpEmailSender.cs:27](backend/RestaurantApi/Services/Email/SmtpEmailSender.cs#L27)) and `Disconnect(true, ct)` is `true` (sends a `QUIT`). The token survives the URL round-trip (`WebEncoders.Base64UrlEncode` → `Base64UrlDecode` on confirm, [AuthController.cs:103](backend/RestaurantApi/Controllers/AuthController.cs#L103)), and the encoded form is alphabet-safe so no `Uri.EscapeDataString` is needed even though one wouldn't hurt. The `CancellationToken` parameter on `SendEmailAsync` is threaded through all four MailKit calls (`ConnectAsync`, `AuthenticateAsync`, `SendAsync`, `DisconnectAsync`).

---

## Checklist coverage

| Item | Status | Evidence |
|---|---|---|
| 🔥 Confirmation/reset links use the configured `App__AdminBaseUrl` — never reflect a `Host` or `Origin` header from the request | ✓ Pass | `var baseUrl = (_appUrls.AdminBaseUrl ?? "http://localhost:3001").TrimEnd('/');` ([AuthController.cs:229-230](backend/RestaurantApi/Controllers/AuthController.cs#L229-L230)). No `Request.Host`/`Request.Scheme`/`Origin` is read in the email path — grep across [AuthController.cs](backend/RestaurantApi/Controllers/AuthController.cs) and [Services/Email/](backend/RestaurantApi/Services/Email/) is empty for those tokens. |
| Tokens in URLs are URL-encoded | ✓ Pass | The Identity token is wrapped through `WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(raw))` ([AuthController.cs:227](backend/RestaurantApi/Controllers/AuthController.cs#L227)) — Base64URL is `[A-Za-z0-9_-]` only, no encoding needed. `user.Id` is a `Guid` which serializes as `[0-9a-f-]`. The `&` separator is literal. Decoded on confirm via `Base64UrlDecode` ([line 103](backend/RestaurantApi/Controllers/AuthController.cs#L103)). |
| Email body templates (HTML or text) escape user-provided fields | ✓ Pass | Only one user-provided field is interpolated into the HTML body: `Bună {System.Net.WebUtility.HtmlEncode(user.FullName)}` ([AuthController.cs:236](backend/RestaurantApi/Controllers/AuthController.cs#L236)). `{link}` is built from a trusted base URL plus URL-safe segments (see above). Subject is constant. |
| SMTP credentials come from config, never hardcoded | ✓ Pass | `SmtpOptions.Username`/`Password` are nullable strings bound from `Smtp__*` env vars via `Configure<SmtpOptions>` ([Program.cs:83](backend/RestaurantApi/Program.cs#L83), [SmtpOptions.cs:9-10](backend/RestaurantApi/Services/Email/SmtpOptions.cs#L9-L10)). No literal credentials anywhere — grep `Password\s*=\s*"` in [Services/Email/](backend/RestaurantApi/Services/Email/) returns zero hits. |
| On send failure: log + return a generic "we'll retry" response, do not surface SMTP errors to the user | ⚠ Partial — see [F2](#f2--high--register--resendconfirmation-always-respond-confirmation_sent-even-when-smtp-throws), [F3](#f3--medium--no-retry-or-outbox-a-transient-smtp-failure-loses-the-email-permanently) | The failure is logged ([AuthController.cs:262-263](backend/RestaurantApi/Controllers/AuthController.cs#L262-L263)) and not surfaced ✓. But "we'll retry" is a lie — there is no retry, and the success response is misleading. |
| In production, `Smtp__UseStartTls` is `true`. In dev (Mailpit), it's correctly `false` — confirm a check exists that flags `false` outside dev | ❌ Fail — see [F1](#f1--high--smtp-transport-defaults-to-plaintext-no-startup-guard-flags-unsafe-tls-outside-development-) | `UseStartTls` and `UseSsl` both default to `false` in [SmtpOptions.cs:11-12](backend/RestaurantApi/Services/Email/SmtpOptions.cs#L11-L12). No `IValidateOptions<SmtpOptions>`, no `.ValidateOnStart()`, no `if (!env.IsDevelopment()) ...` check anywhere in [Program.cs](backend/RestaurantApi/Program.cs). There is no `appsettings.json` (only `appsettings.Development.json`) so prod relies entirely on env vars being remembered. |
| Sending is awaited and not fire-and-forget on a Task — otherwise dropped sends are invisible | ✓ Pass | `await _email.SendEmailAsync(...)` inside `try` ([AuthController.cs:258](backend/RestaurantApi/Controllers/AuthController.cs#L258)). Inside the sender, every MailKit call is awaited and `client` is disposed via `using` ([SmtpEmailSender.cs:35-41](backend/RestaurantApi/Services/Email/SmtpEmailSender.cs#L35-L41)). Trade-off: the HTTP response blocks on SMTP; acceptable for now given the volume, but the right home for this is a queued sender. |
| Subject and from-name are localized only if relevant; otherwise document the single language | ❌ Fail — see [F5](#f5--low--subject-and-body-are-hard-coded-romanian-regardless-of-the-recipients-locale) | Subject `"Confirmă-ți contul Smart Restaurant"` and the entire body are hard-coded Romanian ([AuthController.cs:232-258](backend/RestaurantApi/Controllers/AuthController.cs#L232-L258)). Single-language is not documented in the segment's source files — the customer SPA's i18n composable would normally hint at EN/RO parity. |
| Retry/outbox for transient SMTP failures, or is one missed email gone forever (↳ roadmap follow-up) | ❌ Fail — see [F3](#f3--medium--no-retry-or-outbox-a-transient-smtp-failure-loses-the-email-permanently) | Single attempt; exception caught and swallowed; no persisted outbox table; `ResendConfirmation` ([AuthController.cs:118-127](backend/RestaurantApi/Controllers/AuthController.cs#L118-L127)) exists but is manually triggered by the user — they have to know to retry. |

---

## F1 — HIGH — SMTP transport defaults to plaintext; no startup guard flags unsafe TLS outside Development 🔥

[SmtpOptions.cs:7-14](backend/RestaurantApi/Services/Email/SmtpOptions.cs#L7-L14):

```csharp
public string Host { get; set; } = "localhost";
public int Port { get; set; } = 1025;
public string? Username { get; set; }
public string? Password { get; set; }
public bool UseStartTls { get; set; } = false;
public bool UseSsl { get; set; } = false;
public string FromAddress { get; set; } = "no-reply@smart-restaurant.local";
public string FromName { get; set; } = "Smart Restaurant";
```

[SmtpEmailSender.cs:29-38](backend/RestaurantApi/Services/Email/SmtpEmailSender.cs#L29-L38):

```csharp
var socketOptions = _opts.UseSsl
    ? SecureSocketOptions.SslOnConnect
    : _opts.UseStartTls
        ? SecureSocketOptions.StartTls
        : SecureSocketOptions.None;

await client.ConnectAsync(_opts.Host, _opts.Port, socketOptions, ct);

if (!string.IsNullOrWhiteSpace(_opts.Username))
    await client.AuthenticateAsync(_opts.Username, _opts.Password ?? string.Empty, ct);
```

The "no TLS configured" branch yields `SecureSocketOptions.None`. MailKit will then `AUTH PLAIN` (or `AUTH LOGIN`) over a plaintext TCP socket — both the SMTP `Username`/`Password` and the entire body of every confirmation/reset email travel as cleartext between the API container and the relay. Anything sniffing the path (a misconfigured ingress, a sidecar log forwarder, a misrouted egress) sees the bearer-equivalent token that lets the holder confirm someone else's account.

The roadmap calls for an explicit guard:

> In production, `Smtp__UseStartTls` is `true`. In dev (Mailpit), it's correctly `false` — confirm a check exists that flags `false` outside dev.

There is no such check. Concretely:

- `Program.cs` calls `Configure<SmtpOptions>(...)` ([line 83](backend/RestaurantApi/Program.cs#L83)) but does not chain `.ValidateDataAnnotations()`, `.Validate(o => ...)`, or `.ValidateOnStart()`.
- There is no `IValidateOptions<SmtpOptions>` implementation (grep across [backend/RestaurantApi/](backend/RestaurantApi/) returns nothing).
- The repo contains only [appsettings.Development.json](backend/RestaurantApi/appsettings.Development.json) — no `appsettings.json` to flag prod-shaped defaults — so a prod deploy that copies the compose env-var names (which explicitly set `Smtp__UseStartTls: "false"`, [docker-compose.yml:39-40](docker-compose.yml#L39-L40)) inherits the plaintext setting.
- There is no `appsettings.Production.json` either; the only override path is environment variables.

The two-line fix shape (out of scope for this review) is either a `.Validate(o => env.IsDevelopment() || o.UseStartTls || o.UseSsl, "Smtp must use TLS outside Development").ValidateOnStart()` on the options registration, or an explicit `if (!builder.Environment.IsDevelopment() && !options.UseStartTls && !options.UseSsl) throw ...` near the SMTP wiring in `Program.cs`.

---

## F2 — HIGH — `Register` / `ResendConfirmation` always respond `confirmation_sent` even when SMTP throws

[AuthController.cs:222-265](backend/RestaurantApi/Controllers/AuthController.cs#L222-L265):

```csharp
private async Task SendConfirmationEmailAsync(AppUser user)
{
    var raw = await _users.GenerateEmailConfirmationTokenAsync(user);
    // ...
    try
    {
        await _email.SendEmailAsync(user.Email!, "Confirmă-ți contul Smart Restaurant", html);
    }
    catch (Exception ex)
    {
        // Don't leak SMTP failures to the client, but log loudly so dev can diagnose.
        _logger.LogError(ex, "Failed to send confirmation email to {Email}", user.Email);
    }
}
```

And the callers — [`Register` at line 76-84](backend/RestaurantApi/Controllers/AuthController.cs#L76-L84), the resend branch at [line 54-60](backend/RestaurantApi/Controllers/AuthController.cs#L54-L60), [`ResendConfirmation` at line 118-127](backend/RestaurantApi/Controllers/AuthController.cs#L118-L127) — all unconditionally return:

```csharp
return Ok(new RegisterResponse
{
    Status = "confirmation_sent",
    Email = user.Email ?? request.Email,
    Message = "Verifică-ți emailul pentru a finaliza crearea contului.",
});
```

Three combined effects:

- **The API is lying.** `Status = "confirmation_sent"` is documented as the post-send status; in the catch path nothing was sent, but the response is identical to the success case. The frontend ([ConfirmEmailView](frontend-admin/src/views/ConfirmEmailView.vue) and the post-signup flow added by `21c59f8`) routes on this status — there is no "we failed to send, please try again" branch.
- **The user is silently locked out.** `Login` rejects unconfirmed accounts ([AuthController.cs:136-144](backend/RestaurantApi/Controllers/AuthController.cs#L136-L144)) with `email_not_confirmed`. The only escape is `POST /api/auth/resend-confirmation`. That endpoint is itself behind the same swallow-and-lie pattern, so a relay outage during signup *and* the immediate retry both look successful to the SPA while no email is being delivered.
- **The exception filter is too wide.** `catch (Exception ex)` swallows `OperationCanceledException` from the request's `HttpContext.RequestAborted` propagating into the SMTP call. That's a separate, smaller issue — the right shape is `catch (Exception ex) when (ex is not OperationCanceledException)` — but the headline is the silent failure mode.

A minimum-effort fix is to bubble the exception out of `SendConfirmationEmailAsync` and let the caller decide. The right fix is [F3](#f3--medium--no-retry-or-outbox-a-transient-smtp-failure-loses-the-email-permanently).

---

## F3 — MEDIUM — No retry or outbox; a transient SMTP failure loses the email permanently

`SmtpEmailSender.SendEmailAsync` makes one attempt. On failure the controller catches and logs ([F2](#f2--high--register--resendconfirmation-always-respond-confirmation_sent-even-when-smtp-throws)), the request returns, and the failed delivery exists only in the API's log stream. There is no:

- `outbox`/`email_queue` table backing the send (grep `Outbox` and `EmailQueue` across [backend/RestaurantApi/Models/](backend/RestaurantApi/Models/) and [Migrations/](backend/RestaurantApi/Migrations/) returns nothing).
- Polly retry policy on the sender (`AddPolicyHandler` only exists for `HttpClient` wiring elsewhere; `MailKit` is direct-instantiated, [SmtpEmailSender.cs:27](backend/RestaurantApi/Services/Email/SmtpEmailSender.cs#L27)).
- Background drainer hosted service. [OrderFinalizationService](backend/RestaurantApi/Services/OrderFinalizationService.cs) is the only `IHostedService` registered ([Program.cs:133](backend/RestaurantApi/Program.cs#L133)).

The user-visible recovery is `POST /api/auth/resend-confirmation` ([AuthController.cs:118-127](backend/RestaurantApi/Controllers/AuthController.cs#L118-L127)), which means a user has to:
1. Notice that the email never arrived (a quiet failure mode — they may attribute it to spam folders, slow delivery, etc.).
2. Find and use the resend control in the admin SPA.
3. Hope the transient failure has cleared.

For a development build talking to Mailpit on the same Docker network this is invisible because Mailpit doesn't fail. In production, transient SMTP failures (rate limiting, throttling, brief outages) are common enough that one of "outbox + background drainer" or "Polly retry with jitter + 502 to the client" is the table-stakes answer; the codebase has neither.

The roadmap's `↳` is explicit: "Is there a retry/outbox pattern for transient SMTP failures, or is one missed email gone forever?" Today: gone forever, and the user is told it went out.

---

## F4 — MEDIUM — Invitation flow creates the row but never emails the invitee

[OrganizationsController.cs:148-172](backend/RestaurantApi/Controllers/OrganizationsController.cs#L148-L172):

```csharp
[HttpPost("{orgId:guid}/invitations")]
public async Task<IActionResult> InviteCoOwner(Guid orgId, [FromBody] CreateInvitationRequest request)
{
    // ...
    var invite = new Invitation
    {
        Email = request.Email.Trim().ToLowerInvariant(),
        OrganizationId = orgId,
        RestaurantId = request.RestaurantId,
        Role = MembershipRole.RestaurantAdmin,
        InvitedByUserId = UserId,
    };
    _db.Invitations.Add(invite);
    await _db.SaveChangesAsync();
    _logger.LogInformation("Org {OrgId}: invited {Email} to restaurant {RestaurantId}",
        orgId, invite.Email, request.RestaurantId);

    return CreatedAtAction(nameof(ListInvitations), new { orgId }, ToResponse(invite));
}
```

`InviteCoOwner` injects no `IEmailSender`; grep confirms the file has zero references to it. The response — `InvitationResponse` — exposes the token only to the org owner who created the row:

[OrganizationsController.cs:205-215](backend/RestaurantApi/Controllers/OrganizationsController.cs#L205-L215):

```csharp
private static InvitationResponse ToResponse(Invitation i) => new()
{
    // ...
    Token = i.Token,
    ExpiresAt = i.ExpiresAt,
    AcceptPath = $"/accept-invite?token={i.Token}",
};
```

[Invitation.cs:6-7](backend/RestaurantApi/Models/Invitation.cs#L6-L7) documents the design:

```csharp
/// <summary>
/// A pending invite for someone to join a restaurant as a co-owner/admin.
/// Accepted via <see cref="Token"/>; no email infrastructure required (copyable link).
/// </summary>
```

That comment landed before [`IEmailSender`](backend/RestaurantApi/Services/Email/IEmailSender.cs) (commit `21c59f8`, `feat: email verification flow for account creation`). Now that the SMTP path exists, the manual-link workflow is the standing decision — and it should either be replaced by an email send or kept as a deliberate choice with a stale-comment update. Two operational problems if it stays:

- The invitee must already know the org owner out-of-band (Slack, in person) to receive the link. Onboarding a co-owner is mediated by a copy-paste step that defeats the audit-trail benefit of having an Invitation table at all.
- The token is a `Guid` ([Invitation.cs:24](backend/RestaurantApi/Models/Invitation.cs#L24)). Guids are URL-safe and adequately entropic for the use case, but the link will be copied through chat tools, screenshots, and OS clipboards — a side channel that an email-mediated flow at least centralises.

If the design intent stands, [Invitation.cs:6-7](backend/RestaurantApi/Models/Invitation.cs#L6-L7) is now misleading and should reflect "intentionally copy-link, not emailed."

---

## F5 — LOW — Subject and body are hard-coded Romanian regardless of the recipient's locale

[AuthController.cs:232-258](backend/RestaurantApi/Controllers/AuthController.cs#L232-L258) emits a Romanian-only email. The subject is `"Confirmă-ți contul Smart Restaurant"` ([line 258](backend/RestaurantApi/Controllers/AuthController.cs#L258)); body strings include `"Bună"`, `"Mulțumim că te-ai înregistrat..."`, `"Confirmă emailul"`, `"Sau copiază linkul în browser:"`, `"Dacă nu tu ai cerut acest cont, ignoră acest email."`.

The product is bilingual elsewhere — the customer SPA ships an [`useLocale` composable](frontend-customer/src/composables/useLocale.ts) and the segment-20 roadmap entry calls for RO/EN parity in every dictionary. The admin SPA's `LoginView` (touched by the same commit `21c59f8`) presumably has both languages too. The email path bypasses all of this.

Three knock-on effects: (1) an EN-preference user receives Romanian copy on their first interaction with the product; (2) `AppUser` has no locale field today — there is no place to store the preference even when the controller is upgraded; (3) the subject line is the part shown in spam-folder previews and inbox lists, so cross-locale clarity matters most there.

If single-language is the decision, [Services/Email/](backend/RestaurantApi/Services/Email/) or [AuthController.cs](backend/RestaurantApi/Controllers/AuthController.cs) should carry a comment that names that decision — the roadmap explicitly asks for the language to be documented if not localised.

---

## F6 — LOW — HTML-only message, no plain-text alternative

[SmtpEmailSender.cs:25](backend/RestaurantApi/Services/Email/SmtpEmailSender.cs#L25):

```csharp
message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();
```

`BodyBuilder.TextBody` is never set. Two consequences:

- Mainstream spam filters (Gmail's content classifier, Microsoft IP-reputation pipeline, SpamAssassin) score HTML-only messages higher than `multipart/alternative` ones. For a one-recipient transactional flow at low volume the impact is small; for a deployment that grows past Mailpit it's a known foot-gun.
- Screen readers and text-mode mail clients (mutt, terminal previews in CI alerts) render `text/html` as raw markup. The "or copy this link" paragraph is the only fallback, and it's still inside `<p>` tags.

The fix is mechanical — `BodyBuilder { HtmlBody = ..., TextBody = "Confirmă-ți contul: " + link }` — but the caller would need to surface a text variant too, which is the larger lift.

---

## F7 — LOW — Mailpit's SMTP port `1025` is published to the host, not only the UI port `8025`

[docker-compose.yml:51-55](docker-compose.yml#L51-L55):

```yaml
mailpit:
  image: axllent/mailpit:latest
  ports:
    - "1025:1025"   # SMTP
    - "8025:8025"   # Web UI — open http://localhost:8025 to inspect emails
```

The roadmap's Segment 01 checklist item states: "Mailpit only exposes port 8025 to the host network (8025 is the UI — confirm it's not reachable from prod)." The current file publishes both. In a developer sandbox this is harmless. In any shared dev/staging environment, anyone on the host network can speak SMTP to that listener and relay mail through what the API treats as its outbound channel — Mailpit will happily accept it. The fix is to keep `1025` internal to the compose network (omit the port mapping for `1025`; the `backend-api` service reaches Mailpit by DNS name `mailpit:1025` regardless).

This is cross-listed under Segment 01 as the canonical home, but it falls out of any Segment 11 audit too.

---

## What's solid

| Item | Why it's good |
|---|---|
| Confirmation link base URL is configuration-driven ([AuthController.cs:229-230](backend/RestaurantApi/Controllers/AuthController.cs#L229-L230)) | No host-header injection surface. |
| User-controlled HTML field is escaped ([AuthController.cs:236](backend/RestaurantApi/Controllers/AuthController.cs#L236)) | `System.Net.WebUtility.HtmlEncode` on `user.FullName`; only sink in the body. |
| Token round-trips correctly through Base64URL ([line 227](backend/RestaurantApi/Controllers/AuthController.cs#L227) / [line 103](backend/RestaurantApi/Controllers/AuthController.cs#L103)) | Identity tokens contain characters that don't survive a query-string round-trip raw; Base64URL is the right transformation. |
| `MailKit.SmtpClient` correctly disposed and `QUIT`-ed ([SmtpEmailSender.cs:27, 41](backend/RestaurantApi/Services/Email/SmtpEmailSender.cs#L27-L41)) | No connection leak on the happy path. |
| `CancellationToken` threaded through every MailKit call ([SmtpEmailSender.cs:35-41](backend/RestaurantApi/Services/Email/SmtpEmailSender.cs#L35-L41)) | Shutdown and request-cancellation both interrupt SMTP without hanging the process. |
| Sign-up does not leak account existence ([AuthController.cs:50-60](backend/RestaurantApi/Controllers/AuthController.cs#L50-L60), [line 119-126](backend/RestaurantApi/Controllers/AuthController.cs#L119-L126)) | Both `Register` (when the user already exists but is unconfirmed) and `ResendConfirmation` (always) return the same shape regardless of account state — the comment "Never disclose whether the email exists" at [line 122](backend/RestaurantApi/Controllers/AuthController.cs#L122) is enforced. |

---

## Cross-segment follow-ups raised here

- **[Segment 01](CODE_REVIEW_ROADMAP.md#segment-01--compose--service-topology):** Mailpit SMTP port 1025 is published to the host alongside the UI port (see [F7](#f7--low--mailpits-smtp-port-1025-is-published-to-the-host-not-only-the-ui-port-8025)). The canonical fix lives in `docker-compose.yml`.
- **[Segment 03](CODE_REVIEW_ROADMAP.md#segment-03--api-startup-pipeline-programcs):** Options validation pattern is absent across the board — `SmtpOptions`, `AppUrlOptions`, and `JwtOptions` (the ≥32-byte key check from Segment 03's `↳` follow-up) would all benefit from `.ValidateOnStart()` registrations. [F1](#f1--high--smtp-transport-defaults-to-plaintext-no-startup-guard-flags-unsafe-tls-outside-development-) is the email-shaped instance.
- **[Segment 05](CODE_REVIEW_ROADMAP.md#segment-05--authentication--token-lifecycle):** [F2](#f2--high--register--resendconfirmation-always-respond-confirmation_sent-even-when-smtp-throws) intersects the auth review: silent SMTP failure leaves users in `EmailConfirmed = false`, blocked from `Login` ([AuthController.cs:136-144](backend/RestaurantApi/Controllers/AuthController.cs#L136-L144)). The auth checklist's "Password reset tokens" item is not yet exercised — no `/forgot-password` endpoint exists today (grep confirms), so the segment-11 reset-link path is purely hypothetical.
- **[Segment 08](CODE_REVIEW_ROADMAP.md#segment-08--controllers--admin-surface):** [F4](#f4--medium--invitation-flow-creates-the-row-but-never-emails-the-invitee) — `OrganizationsController.InviteCoOwner` is in segment 08's scope; the email gap is documented here but the controller-side change lives there.
- **[Segment 25](CODE_REVIEW_ROADMAP.md#segment-25--end-to-end-security-review):** the OWASP "Cryptographic failures" item lists TLS for SMTP in prod — see [F1](#f1--high--smtp-transport-defaults-to-plaintext-no-startup-guard-flags-unsafe-tls-outside-development-).
