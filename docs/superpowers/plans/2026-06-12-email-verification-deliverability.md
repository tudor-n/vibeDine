# Email Verification Deliverability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **GIT IS USER-MANAGED:** Do NOT run `git add`/`git commit`/`git push`. Where a step says "Commit", treat it as a checkpoint: stop and let the user commit themselves.

**Goal:** Make the already-built email-verification flow deliver confirmation emails reliably to Gmail inboxes via Resend SMTP on a custom domain, and surface a soft warning to the user when a send fails.

**Architecture:** The verification flow (`register` → `confirm-email` → unconfirmed-login block) is unchanged. Production mail is routed through Resend's SMTP endpoint (the existing `SmtpEmailSender` already speaks SMTP) from a DKIM-signed custom-domain `From`, configured entirely via Railway environment variables. The only behavioral code change is making confirmation-send failures visible to the caller instead of silently swallowed.

**Tech Stack:** ASP.NET Core 8, ASP.NET Identity, MailKit SMTP, Resend (provider), xUnit + FluentAssertions + Testcontainers (Postgres) for integration tests.

---

## Spec

Source spec: [docs/superpowers/specs/2026-06-12-email-verification-deliverability-design.md](../specs/2026-06-12-email-verification-deliverability-design.md)

## File Structure

**Code (this plan):**
- Modify `backend/RestaurantApi/Controllers/AuthController.cs` — `SendConfirmationEmailAsync` returns `bool`; `register` and its resend-branch surface `confirmation_failed`; `resend-confirmation` logs failures but stays generic.
- Modify `backend/RestaurantApi/appsettings.json` — add documented `Smtp` and `App` sections (dev-safe defaults; production overridden by Railway env).
- Modify `backend/RestaurantApi/Program.cs` — optional production startup warning when SMTP is still on dev defaults.

**Tests (this plan):**
- Create `backend/RestaurantApi.Tests/Helpers/RecordingEmailSender.cs` — `IEmailSender` test double that records sends and succeeds.
- Create `backend/RestaurantApi.Tests/Helpers/FailingEmailSender.cs` — `IEmailSender` test double that throws.
- Create `backend/RestaurantApi.Tests/Helpers/AuthTestFactory.cs` — `WebApplicationFactory` that swaps in a chosen `IEmailSender`.
- Create `backend/RestaurantApi.Tests/AuthControllerTests.cs` — integration tests for the success/failure register responses.

**Docs (this plan):**
- Create `docs/email-deliverability-setup.md` — the manual Resend + DNS + Railway runbook for the user.

**Manual / out of code (user, not in this plan):** register a domain, create the Resend account, add DKIM/SPF/DMARC DNS records, set Railway env vars. Tracked in the runbook doc, gates end-to-end Gmail verification.

---

### Task 1: Surface send-failure as `confirmation_failed`

**Files:**
- Create: `backend/RestaurantApi.Tests/Helpers/RecordingEmailSender.cs`
- Create: `backend/RestaurantApi.Tests/Helpers/FailingEmailSender.cs`
- Create: `backend/RestaurantApi.Tests/Helpers/AuthTestFactory.cs`
- Create: `backend/RestaurantApi.Tests/AuthControllerTests.cs`
- Modify: `backend/RestaurantApi/Controllers/AuthController.cs`

- [ ] **Step 1: Create the recording (success) email test double**

`backend/RestaurantApi.Tests/Helpers/RecordingEmailSender.cs`:

```csharp
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using RestaurantApi.Services.Email;

namespace RestaurantApi.Tests.Helpers;

/// <summary>IEmailSender double that records every send and always succeeds.</summary>
public class RecordingEmailSender : IEmailSender
{
    public record SentEmail(string To, string Subject, string HtmlBody);

    public ConcurrentBag<SentEmail> Sent { get; } = new();

    public Task SendEmailAsync(string toAddress, string subject, string htmlBody, CancellationToken ct = default)
    {
        Sent.Add(new SentEmail(toAddress, subject, htmlBody));
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Create the failing email test double**

`backend/RestaurantApi.Tests/Helpers/FailingEmailSender.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using RestaurantApi.Services.Email;

namespace RestaurantApi.Tests.Helpers;

/// <summary>IEmailSender double that simulates an SMTP outage.</summary>
public class FailingEmailSender : IEmailSender
{
    public Task SendEmailAsync(string toAddress, string subject, string htmlBody, CancellationToken ct = default)
        => throw new InvalidOperationException("simulated SMTP failure");
}
```

- [ ] **Step 3: Create the auth test factory that swaps IEmailSender**

`backend/RestaurantApi.Tests/Helpers/AuthTestFactory.cs`:

```csharp
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using RestaurantApi.Services;
using RestaurantApi.Services.Email;

namespace RestaurantApi.Tests.Helpers;

/// <summary>
/// Test host that replaces the registered IEmailSender with a supplied double and
/// drops the background workers (which the base factory leaves running). Set
/// <see cref="EmailSender"/> before the first CreateClient() call.
/// </summary>
public class AuthTestFactory : TestWebApplicationFactory
{
    public IEmailSender EmailSender { get; set; } = new RecordingEmailSender();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            // Swap the SMTP sender for the supplied double.
            var emailDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IEmailSender));
            if (emailDescriptor != null) services.Remove(emailDescriptor);
            services.AddSingleton(EmailSender);

            // The base factory (unlike MenuImportTestFactory) leaves hosted services
            // registered; drop them so they don't poll/race the test database.
            RemoveHostedService<MenuImportBackgroundService>(services);
            RemoveHostedService<OrderFinalizationService>(services);
        });
    }

    private static void RemoveHostedService<T>(IServiceCollection services) where T : class
    {
        foreach (var d in services.Where(d => d.ImplementationType == typeof(T)).ToList())
            services.Remove(d);
    }
}
```

- [ ] **Step 4: Write the integration tests (RED)**

`backend/RestaurantApi.Tests/AuthControllerTests.cs`:

```csharp
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantApi.Data;
using RestaurantApi.DTOs.Responses;
using RestaurantApi.Services.Email;
using RestaurantApi.Tests.Helpers;
using Testcontainers.PostgreSql;
using Xunit;

namespace RestaurantApi.Tests;

public class AuthControllerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private AuthTestFactory _factory = null!;

    public async Task InitializeAsync() => await _pg.StartAsync();

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        await _pg.DisposeAsync();
    }

    private async Task<HttpClient> ClientWithSenderAsync(IEmailSender sender)
    {
        _factory = new AuthTestFactory
        {
            PostgresConnectionString = _pg.GetConnectionString(),
            EmailSender = sender,
        };
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
        }
        return _factory.CreateClient();
    }

    [Fact]
    public async Task Register_returns_confirmation_sent_when_email_succeeds()
    {
        var recorder = new RecordingEmailSender();
        var client = await ClientWithSenderAsync(recorder);

        var resp = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = "alice@example.com",
            password = "Sup3rSecret!",
            fullName = "Alice",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<RegisterResponse>();
        body!.Status.Should().Be("confirmation_sent");
        recorder.Sent.Should().ContainSingle(e => e.To == "alice@example.com");
    }

    [Fact]
    public async Task Register_returns_confirmation_failed_when_email_send_throws()
    {
        var client = await ClientWithSenderAsync(new FailingEmailSender());

        var resp = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = "bob@example.com",
            password = "Sup3rSecret!",
            fullName = "Bob",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<RegisterResponse>();
        body!.Status.Should().Be("confirmation_failed");
    }
}
```

- [ ] **Step 5: Run the tests to verify they fail**

Run: `dotnet test backend/RestaurantApi.Tests --filter FullyQualifiedName~AuthControllerTests`
Expected: `Register_returns_confirmation_failed_when_email_send_throws` FAILS — current code returns `confirmation_sent` regardless of send outcome (the success test may already pass). Requires Docker running for Testcontainers.

- [ ] **Step 6: Make `SendConfirmationEmailAsync` return a bool**

In `backend/RestaurantApi/Controllers/AuthController.cs`, change the method signature and the `try`/`catch` to report outcome. Replace:

```csharp
    private async Task SendConfirmationEmailAsync(AppUser user)
    {
```
with:
```csharp
    private async Task<bool> SendConfirmationEmailAsync(AppUser user)
    {
```

And replace the trailing try/catch:

```csharp
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
with:
```csharp
        try
        {
            await _email.SendEmailAsync(user.Email!, "Confirmă-ți contul Smart Restaurant", html);
            return true;
        }
        catch (Exception ex)
        {
            // Don't leak SMTP failures to the client, but log loudly so dev can diagnose.
            _logger.LogError(ex, "Failed to send confirmation email to {Email}", user.Email);
            return false;
        }
    }
```

- [ ] **Step 7: Branch the new-user register path on the send result**

In `Register`, replace:

```csharp
        await SendConfirmationEmailAsync(user);

        _logger.LogInformation("Registered user {UserId} ({Email}) — confirmation email sent", user.Id, user.Email);
        return Ok(new RegisterResponse
        {
            Status = "confirmation_sent",
            Email = user.Email ?? request.Email,
            Message = "Verifică-ți emailul pentru a finaliza crearea contului.",
        });
```
with:
```csharp
        var sent = await SendConfirmationEmailAsync(user);

        _logger.LogInformation("Registered user {UserId} ({Email}) — confirmation email sent: {Sent}", user.Id, user.Email, sent);
        return Ok(new RegisterResponse
        {
            Status = sent ? "confirmation_sent" : "confirmation_failed",
            Email = user.Email ?? request.Email,
            Message = sent
                ? "Verifică-ți emailul pentru a finaliza crearea contului."
                : "Contul a fost creat, dar nu am putut trimite emailul de confirmare. Încearcă din nou în câteva momente.",
        });
```

- [ ] **Step 8: Branch the existing-unconfirmed resend path on the send result**

In `Register`, replace the early resend branch:

```csharp
            if (!existing.EmailConfirmed)
            {
                await SendConfirmationEmailAsync(existing);
                return Ok(new RegisterResponse
                {
                    Status = "confirmation_sent",
                    Email = existing.Email ?? request.Email,
                    Message = "Ți-am trimis un nou link de confirmare pe email.",
                });
            }
```
with:
```csharp
            if (!existing.EmailConfirmed)
            {
                var resent = await SendConfirmationEmailAsync(existing);
                return Ok(new RegisterResponse
                {
                    Status = resent ? "confirmation_sent" : "confirmation_failed",
                    Email = existing.Email ?? request.Email,
                    Message = resent
                        ? "Ți-am trimis un nou link de confirmare pe email."
                        : "Nu am putut trimite emailul de confirmare. Încearcă din nou în câteva momente.",
                });
            }
```

- [ ] **Step 9: Log (but don't disclose) failures in resend-confirmation**

In `ResendConfirmation`, replace:

```csharp
        var user = await _users.FindByEmailAsync(request.Email);
        // Never disclose whether the email exists.
        if (user != null && !user.EmailConfirmed)
            await SendConfirmationEmailAsync(user);
```
with:
```csharp
        var user = await _users.FindByEmailAsync(request.Email);
        // Never disclose whether the email exists.
        if (user != null && !user.EmailConfirmed)
        {
            var sent = await SendConfirmationEmailAsync(user);
            if (!sent)
                _logger.LogWarning("Resend confirmation email failed for {Email}", user.Email);
        }
```

(The public response message is intentionally left unchanged to preserve anti-enumeration behavior.)

- [ ] **Step 10: Run the tests to verify they pass (GREEN)**

Run: `dotnet test backend/RestaurantApi.Tests --filter FullyQualifiedName~AuthControllerTests`
Expected: both tests PASS.

- [ ] **Step 11: Run the full backend suite to confirm no regressions**

Run: `dotnet test backend/RestaurantApi.Tests`
Expected: all tests PASS.

- [ ] **Step 12: Commit** (user-managed git — checkpoint; let the user commit)

Suggested message: `feat(auth): surface confirmation-email send failures as confirmation_failed`

---

### Task 2: Document the SMTP/App config keys

**Files:**
- Modify: `backend/RestaurantApi/appsettings.json`

- [ ] **Step 1: Add `Smtp` and `App` sections with dev-safe defaults**

In `backend/RestaurantApi/appsettings.json`, add two top-level sections (production values come from Railway env vars `Smtp__*` / `App__*`, which override these). Replace:

```json
  "MenuImport": {
    "MaxConcurrentJobs": 3,
    "PollIntervalMs": 2000,
    "MaxFileBytes": 110000000,
    "CallbackBaseUrl": "http://localhost:5005",
    "DefaultProvider": "gemini",
    "StaleAfterMinutes": 5
  }
}
```
with:
```json
  "MenuImport": {
    "MaxConcurrentJobs": 3,
    "PollIntervalMs": 2000,
    "MaxFileBytes": 110000000,
    "CallbackBaseUrl": "http://localhost:5005",
    "DefaultProvider": "gemini",
    "StaleAfterMinutes": 5
  },
  "Smtp": {
    "Host": "localhost",
    "Port": 1025,
    "UseStartTls": false,
    "UseSsl": false,
    "FromAddress": "no-reply@smart-restaurant.local",
    "FromName": "Smart Restaurant"
  },
  "App": {
    "AdminBaseUrl": "http://localhost:3001"
  }
}
```

- [ ] **Step 2: Verify the app still builds and binds config**

Run: `dotnet build backend/RestaurantApi`
Expected: build succeeds. (These values match the existing code defaults, so behavior in dev is unchanged.)

- [ ] **Step 3: Commit** (user-managed git — checkpoint)

Suggested message: `chore(config): document Smtp and App config sections`

---

### Task 3 (optional): Production startup warning for dev SMTP defaults

Prevents silently shipping a broken mail path again. Skip if you prefer to keep `Program.cs` untouched.

**Files:**
- Modify: `backend/RestaurantApi/Program.cs`

- [ ] **Step 1: Add a production-only warning near the Email registration**

In `backend/RestaurantApi/Program.cs`, immediately after the Email section (the three `Smtp`/`AppUrl`/`IEmailSender` lines around line 98-100), add:

```csharp
// Warn loudly if Production is about to send mail through the dev defaults —
// a misconfigured SMTP host/From silently breaks confirmation emails.
if (builder.Environment.IsProduction())
{
    var smtpCheck = builder.Configuration.GetSection(SmtpOptions.SectionName).Get<SmtpOptions>() ?? new SmtpOptions();
    if (smtpCheck.Host is "localhost" or "127.0.0.1"
        || smtpCheck.FromAddress.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine(
            "[WARN] SMTP is using development defaults in Production — confirmation emails will NOT be delivered. " +
            "Set Smtp__Host and Smtp__FromAddress (see docs/email-deliverability-setup.md).");
    }
}
```

If `System` is not already imported in `Program.cs`, add `using System;` at the top (for `StringComparison`).

- [ ] **Step 2: Verify build**

Run: `dotnet build backend/RestaurantApi`
Expected: build succeeds.

- [ ] **Step 3: Commit** (user-managed git — checkpoint)

Suggested message: `chore(auth): warn when Production SMTP uses dev defaults`

---

### Task 4: Write the Resend + DNS + Railway runbook

The manual, non-code setup the user performs. End-to-end Gmail delivery cannot be verified until this is done.

**Files:**
- Create: `docs/email-deliverability-setup.md`

- [ ] **Step 1: Create the runbook**

`docs/email-deliverability-setup.md`:

```markdown
# Email Deliverability Setup (Resend + custom domain)

The email-verification flow is fully implemented in code. To make confirmation
emails reach Gmail inboxes in production, complete the steps below. None of this
is code — it is provider, DNS, and Railway configuration.

## 1. Domain
Register/own a domain with DNS access (e.g. Cloudflare, Namecheap).
Example used below: `smartrestaurant.app`.

## 2. Resend account + domain verification
1. Create an account at resend.com.
2. Add your domain in Resend → it generates **DKIM** (CNAME) and **SPF** (TXT) records.
3. Add those records to your domain's DNS, then click **Verify** in Resend
   (propagation: minutes to ~2h).
4. Add a **DMARC** TXT record to improve Gmail trust:
   - Name: `_dmarc`
   - Value: `v=DMARC1; p=none; rua=mailto:postmaster@smartrestaurant.app`
5. Create a Resend **API key**.

## 3. Railway env vars (backend service)
Set these on the backend Railway service (NOT committed — the password is a secret):

| Key | Value |
|-----|-------|
| `Smtp__Host` | `smtp.resend.com` |
| `Smtp__Port` | `587` |
| `Smtp__Username` | `resend` |
| `Smtp__Password` | `<RESEND_API_KEY>` |
| `Smtp__UseStartTls` | `true` |
| `Smtp__FromAddress` | `no-reply@smartrestaurant.app` |
| `Smtp__FromName` | `Smart Restaurant` |
| `App__AdminBaseUrl` | `https://<existing-admin-railway-url>` |

(`__` maps to nested config keys. `App__AdminBaseUrl` is the click-through link in
the email; keeping the Railway admin URL is fine — Gmail authenticates the `From`
domain, not the link.)

## 4. Verify delivery
1. Register a new account using a real Gmail address.
2. Confirm the email lands in the **inbox** (not spam).
3. In Gmail, open the message → **Show original** → confirm:
   - `DKIM: PASS`
   - `SPF: PASS`
   - `DMARC: PASS`
4. Click the confirmation link → account confirmed → login succeeds.
5. Test `resend-confirmation` and confirm that an unconfirmed login is blocked.

## Later (optional)
Move the confirmation link to a custom subdomain by pointing the admin frontend at
the domain via Railway custom domains and updating `App__AdminBaseUrl`. No code change.
```

- [ ] **Step 2: Commit** (user-managed git — checkpoint)

Suggested message: `docs: add email deliverability (Resend + DNS) runbook`

---

## Manual end-to-end verification (after the user completes Task 4 setup)

These cannot be automated (require the live domain + Resend):

- [ ] Register with a real Gmail address → email arrives in **inbox**.
- [ ] Gmail "Show original" shows `DKIM/SPF/DMARC: PASS`.
- [ ] Confirmation link confirms the account and login succeeds.
- [ ] Unconfirmed login is blocked with `email_not_confirmed`.
- [ ] `resend-confirmation` delivers a fresh link.
```
