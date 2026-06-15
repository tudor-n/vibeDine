# Email Verification Deliverability — Design

**Date:** 2026-06-12
**Status:** Approved (pending spec review)
**Author:** brainstorming session

## Problem

Smart Restaurant's signup flow already implements email verification end-to-end in
[`AuthController`](../../../backend/RestaurantApi/Controllers/AuthController.cs):
`register` generates an ASP.NET Identity confirmation token and emails a confirm
link, `confirm-email` validates it, `resend-confirmation` re-sends, and `login`
blocks unconfirmed users. The sender ([`SmtpEmailSender`](../../../backend/RestaurantApi/Services/Email/SmtpEmailSender.cs))
is a working MailKit SMTP client.

The flow does not work in production because the outbound mail path is unconfigured:
[`SmtpOptions`](../../../backend/RestaurantApi/Services/Email/SmtpOptions.cs) defaults
to a local dev catcher (`localhost:1025`) with a fake `no-reply@smart-restaurant.local`
From address. Gmail rejects or spam-files mail from an unauthenticated, non-existent
domain, so confirmation emails never reach users.

**Goal:** Confirmation emails reliably land in Gmail (and general) inboxes. This is
primarily a provider + DNS configuration task, plus one small UX code change. The
verification *logic* is not changing.

## Approach

Send through **Resend** over SMTP, from an address on a **custom domain** the user
controls, with SPF + DKIM (+ DMARC) configured so Gmail trusts the mail. Because
`SmtpEmailSender` already speaks standard SMTP, switching providers is configuration,
not code.

Chosen during brainstorming:
- **From address:** custom domain (DKIM-signed) — this is what Gmail authenticates.
- **Confirm-link base URL (`App__AdminBaseUrl`):** keep the existing Railway admin
  URL for now (the click-through link). Can move to a custom subdomain later with no
  rework.
- **Send-failure UX:** surface a soft warning to the user when the email fails to
  send, instead of the current silent swallow-and-log.

Rejected alternatives: Gmail SMTP relay (500/day cap, From forced to a Gmail address,
weak for non-Gmail recipients); Resend shared test domain (`onboarding@resend.dev`)
(fine as a stopgap but unbranded, not production-grade).

## Manual prerequisites (one-time, performed by the user)

These are outside the codebase and block end-to-end verification:

1. Register/own a domain (e.g. `smartrestaurant.app`) with DNS access.
2. Create a Resend account; add the domain in Resend.
3. Add the **DKIM** (CNAME) and **SPF** (TXT) records Resend generates to the domain's
   DNS; click verify (propagation: minutes to ~2h).
4. Add a **DMARC** TXT record: `v=DMARC1; p=none; rua=mailto:<your-address>` —
   recommended, improves Gmail trust.
5. Generate a Resend **API key**.

## Configuration (Railway backend service env vars)

```
Smtp__Host=smtp.resend.com
Smtp__Port=587
Smtp__Username=resend
Smtp__Password=<RESEND_API_KEY>
Smtp__UseStartTls=true
Smtp__FromAddress=no-reply@<your-domain>
Smtp__FromName=Smart Restaurant
App__AdminBaseUrl=https://<existing-admin-railway-url>
```

`SmtpOptions` binds from config section `Smtp`, `AppUrlOptions` from `App`
([`Program.cs`](../../../backend/RestaurantApi/Program.cs) lines 97–100). On Railway,
the `__` (double underscore) separator maps to nested config keys.

## Code changes

Deliberately minimal. The verification flow is untouched.

### 1. Soft-warning on send failure (the only behavioral change)

Today [`SendConfirmationEmailAsync`](../../../backend/RestaurantApi/Controllers/AuthController.cs#L239)
catches send exceptions, logs them, and returns void — so `register` always returns
`confirmation_sent` even if no email went out.

Change:
- `SendConfirmationEmailAsync` returns a `bool` (true = sent, false = send failed).
  It still catches/logs internally (no exception leaks to the client).
- `register`: if the send returns false, respond with a distinct status
  (e.g. `Status = "confirmation_failed"`) and a user-facing message inviting a retry,
  instead of `confirmation_sent`. The user account is still created and unconfirmed,
  so a later `resend-confirmation` works.
- `resend-confirmation`: keep the non-enumerating response (never disclose whether the
  email exists), but the internal send result can be logged. The public message stays
  generic to preserve the existing anti-enumeration behavior.

The `register` resend-branch (existing-but-unconfirmed user, line ~52) uses the same
return-value handling.

### 2. Config hygiene

- Document the real `Smtp__*` / `App__*` keys in the backend `appsettings` example /
  README so the production config is discoverable.
- **Optional startup guard:** in production, fail fast (or log a prominent warning) if
  `Smtp:FromAddress` still ends in `.local` or `Smtp:Host` is `localhost` — prevents
  silently re-shipping a broken mail path. Recommended but can be deferred.

## Data flow (unchanged)

```
register → UserManager.GenerateEmailConfirmationTokenAsync → Base64Url encode
        → SmtpEmailSender.SendEmailAsync (smtp.resend.com, STARTTLS, DKIM-signed)
        → Gmail inbox
user clicks link → admin SPA /confirm-email?userId=&token=
        → POST confirm-email → ConfirmEmailAsync → JWT issued
```

## Error handling

- SMTP send failure: caught and logged in `SmtpEmailSender`/`SendConfirmationEmailAsync`;
  surfaced to the user as a soft warning (status `confirmation_failed`); account remains
  created-and-unconfirmed so a resend recovers it.
- Invalid/expired confirm token: existing behavior (`400` with a localized message).
- Already-confirmed user hitting confirm-email: existing idempotent behavior (returns an
  auth response).

## Testing / verification

Manual (requires the live domain + Resend setup):
1. Register with a real Gmail address → email arrives in **inbox**, not spam.
2. In Gmail "Show original", confirm `DKIM: PASS`, `SPF: PASS`, `DMARC: PASS`.
3. Click the link → account confirmed → login succeeds.
4. Unconfirmed login is blocked with `email_not_confirmed`.
5. `resend-confirmation` sends a fresh link.

Automated (no live SMTP dependency):
- Unit test that `register` returns `confirmation_failed` when `IEmailSender` throws
  (inject a failing fake sender), and `confirmation_sent` when it succeeds.
- Existing backend test suite continues to pass.

## Out of scope (YAGNI)

- Resend HTTP API integration (SMTP is sufficient).
- Email templating engine / multi-language emails.
- Bounce/complaint webhook handling.
- Moving the confirm-link to a custom subdomain (deferred, no rework cost).

## Open items / dependencies

- Requires the user to complete the manual domain + Resend + DNS prerequisites before
  end-to-end Gmail delivery can be verified.
- Railway env vars must be set on the backend service (not in committed config) since
  they include the API key.
