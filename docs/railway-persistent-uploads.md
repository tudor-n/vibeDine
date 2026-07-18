# Persistent uploads on Railway (branding / avatars / menu-item photos)

## The problem

Uploaded images (branding logo, welcome photo, menu background, user avatars,
AI-imported dish photos) are written to local disk under the uploads root and
served back at `/uploads/*`. Railway containers get a **fresh filesystem on every
deploy**, so without a volume every redeploy silently deletes all uploads while
the database keeps pointing at the now-dead URLs — which is why branding had to
be re-uploaded after each deploy.

The uploads root defaults to `ContentRoot/wwwroot/uploads` (dev/docker-compose,
which bind-mounts it) and is overridable with the `Uploads__Root` env var
(`Storage/UploadPaths.cs`, wired in `Program.cs`).

## One-time Railway setup (backend service)

1. Backend service → right-click / **Attach Volume**. Mount path: `/data`.
   (Railway allows **one volume per service** — if the service already has one,
   skip this step and reuse its mount path below.)
2. Add the env var on the backend service:

   ```
   Uploads__Root=/data/uploads
   ```

3. Redeploy. The app creates `/data/uploads/{branding,avatars,menu-items}` on
   startup and serves them at `/uploads/*` — URLs stored in the DB don't change.
4. **Re-upload branding assets once per restaurant** (and any avatars that
   matter). Files lost to earlier redeploys are gone; from now on they persist.

Optional, same volume: `MenuImport__StorageRoot=/data/menu-imports` if PDF-import
staging files should also survive restarts mid-job.

## Caveats

- Railway volumes attach to a **single instance** — this setup does not support
  scaling the backend to multiple replicas. If that ever happens, move uploads
  to object storage (S3/R2) instead.
- Dev is unaffected: with `Uploads__Root` unset, everything stays under
  `wwwroot/uploads` exactly as before (docker-compose bind mount included).
