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

> **Important — Railway blocks outbound SMTP.** Railway (like most PaaS) blocks
> outbound TCP on SMTP ports (25/465/587, and the 2xxx alternates), so the backend
> **cannot** send through `smtp.resend.com`. A connection attempt hangs for the full
> ~2-minute MailKit timeout and never delivers. Production therefore sends through
> Resend's **HTTP API** (port 443), selected with `Email__Provider=resend`. The
> `ResendEmailSender` reuses the `Smtp__*` values below, so no extra credentials are
> needed — `Smtp__Password` holds the Resend API key (it is the same key Resend uses
> as the SMTP password).

Set these on the backend Railway service (NOT committed — the API key is a secret):

| Key | Value | Notes |
|-----|-------|-------|
| `Email__Provider` | `resend` | **Required** — switches to the HTTP API transport |
| `Smtp__Password` | `<RESEND_API_KEY>` | the Resend API key (`re_…`) |
| `Smtp__FromAddress` | `no-reply@vibedine.ro` | must be on your verified domain |
| `Smtp__FromName` | `Smart Restaurant` | sender display name |
| `App__AdminBaseUrl` | `https://<admin-railway-url>` | click-through link in the email |

The `Smtp__Host`, `Smtp__Port`, `Smtp__Username`, `Smtp__UseStartTls` vars are only
used by the local-dev SMTP sender and are ignored when `Email__Provider=resend`; you
can leave or remove them.

(`__` maps to nested config keys. `App__AdminBaseUrl` is just the click-through link;
keeping the Railway admin URL is fine — Gmail authenticates the `From` domain, not the
link.)

After setting these, **redeploy the backend** so the new variables take effect.

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
