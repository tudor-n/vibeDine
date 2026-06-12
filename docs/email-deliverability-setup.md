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
