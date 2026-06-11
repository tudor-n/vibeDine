# Code Review — Segment 02: CI workflows & build hygiene

**Date:** 2026-05-21
**Reviewer:** Claude (Opus 4.7, read-only)
**Scope:** GitHub Actions workflows under `.github/workflows/` plus the `package.json` / `.csproj` scripts they invoke.

## Files reviewed

| Role | Path |
|---|---|
| Backend CI | [.github/workflows/backend.yml](.github/workflows/backend.yml) |
| Frontend CI | [.github/workflows/frontend.yml](.github/workflows/frontend.yml) |
| Contracts CI | [.github/workflows/contracts.yml](.github/workflows/contracts.yml) |
| Admin package | [frontend-admin/package.json](frontend-admin/package.json) |
| Customer package | [frontend-customer/package.json](frontend-customer/package.json) |
| Test project | [backend/RestaurantApi.Tests/RestaurantApi.Tests.csproj](backend/RestaurantApi.Tests/RestaurantApi.Tests.csproj) |

No `CLAUDE.md` files in the repo — checklist-only.

---

## Checklist outcomes

| # | Item | Result |
|---|---|---|
| 1 | Each workflow runs on `pull_request` against `main` (not just `push`) | **PASS** — see verification |
| 2 | Backend workflow: `dotnet restore` → `build` → `test` with a real Postgres (Testcontainers + Docker) | **PASS** — see verification |
| 3 | Frontend workflow: `npm ci`, type-check, lint, tests for **both** frontends | **FAIL** — see Finding 1 |
| 4 | Contracts workflow validates shared types compile against both consumers | **FAIL** — see Finding 2 |
| 5 | No `continue-on-error: true` where failure would matter | **PASS** — zero matches in workflows |
| 6 | Secrets via GitHub secrets, not committed env files | **PASS** — no real secrets used yet (deploy jobs are placeholders, see Finding 5) |
| 7 | Caching: `~/.nuget/packages`, `~/.npm`, pip caches restored across runs | **PARTIAL** — see Finding 3 |
| 8 | Status check rule on `main` requiring all three workflows | **OPEN** — see Finding 6 |

### Verification

**(1) Triggers:** [backend.yml:3-10](.github/workflows/backend.yml#L3-L10) and [frontend.yml:3-14](.github/workflows/frontend.yml#L3-L14) both trigger on `push: branches: [main, develop]` and on `pull_request` (no branch filter = all PRs). The wider trigger is fine — PRs from any branch get checked. [contracts.yml:3-6](.github/workflows/contracts.yml#L3-L6) triggers on `pull_request` only.

**(2) Backend test step:** [backend.yml:24-26](.github/workflows/backend.yml#L24-L26) runs `dotnet restore` → `build --no-restore` → `test --no-build`. [RestaurantApi.Tests.csproj:14](backend/RestaurantApi.Tests/RestaurantApi.Tests.csproj#L14) pins `Testcontainers.PostgreSql 3.10.0`, and ubuntu-latest runners come with Docker pre-installed. So when `dotnet test` runs, the test factory spins up a real Postgres in a container — exactly the segment-13 requirement.

---

## Findings

### Finding 1 — Frontend CI has no lint step and `frontend-customer` tests are a fake echo (HIGH)

[frontend.yml:30-33](.github/workflows/frontend.yml#L30-L33) for `build-customer`:

```yaml
- run: npm ci
- run: npm run type-check
- run: npm run build
- run: npm run test
```

[frontend-customer/package.json:12-15](frontend-customer/package.json#L12-L15):

```json
"test": "echo \"no tests yet\" && exit 0",
"lint": "run-s lint:*",
"lint:oxlint": "oxlint . --fix",
"lint:eslint": "eslint . --fix --cache",
```

So:
- `npm run test` for `frontend-customer` runs `echo` and exits 0. The "test" step **always passes** without running anything. Vitest, MSW, the segment-19 follow-ups — none of it is exercised in CI.
- `npm run lint` exists in `frontend-customer/package.json` (oxlint + eslint with `--fix`!) — but the workflow does not invoke it.
- The admin's `package.json` ([line 6-11](frontend-admin/package.json#L6-L11)) has no `lint` script at all, so even if the workflow added `npm run lint`, the admin would fail to find it.

Two consequences:
- Customer SPA bugs that vitest would catch (composables, stores, pure functions) ship to main with no signal.
- The lint scripts on customer use `--fix` flags. If a future maintainer wires them into CI, `--fix` mutates the working tree mid-CI and either masks an error or fails the diff-check later. `--fix` should not be in the CI invocation; it's a developer-side ergonomic.

The roadmap checklist explicitly requires "lint, and tests for **both** `frontend-customer` and `frontend-admin`."

**Recommendation:**
1. Replace the customer `test` script with `vitest run` (matches the admin's shape) and wire MSW tests as planned.
2. Add a `lint` script to `frontend-admin/package.json` (mirroring customer minus `--fix`).
3. Strip `--fix` from a CI-callable script. Define `lint:check` that runs eslint/oxlint without mutation; keep `lint` (with `--fix`) for human use.
4. Add `npm run lint:check` to both `build-customer` and `build-admin` jobs.

---

### Finding 2 — Contracts workflow only checks JSON syntax; the actual contract drift is unguarded (HIGH)

[contracts.yml:13-18](.github/workflows/contracts.yml#L13-L18):

```yaml
- name: Validate JSON files
  run: |
    for file in shared/contracts/*.json; do
      echo "Validating $file"
      python3 -m json.tool "$file" > /dev/null && echo "✅ $file is valid"
    done
```

What this does: confirms every `*.json` file in `shared/contracts/` is parseable. Nothing else.

What this does **not** do:
- Does not detect schema changes vs. the previous main.
- Does not validate that the JSON matches the actual API.
- Does not validate the TypeScript types under `shared/types/**` against either frontend's consumer code.
- The trigger paths (`shared/contracts/**`) exclude `shared/types/**` — changes to `shared/types/models.ts` don't fire this workflow.

The hard truth from [Segment 24 Findings 1, 2, 4](CODE_REVIEW_SEGMENT_24_SHARED_CONTRACTS.md): `DailyMetrics` is missing `flashDealRevenue`; admin order endpoints are typed `any[]`; `api-contract-ai.json` describes an API that no longer exists. None of those drifts would be caught by this workflow.

**Recommendation:**
1. Either delete `api-contract-ai.json` (Segment 24 F4) or regenerate from the live OpenAPI on each backend release.
2. Add a step to this workflow that runs `vue-tsc --noEmit` in both frontends after copying `shared/` into each — exercises the shared types from the consumer's perspective.
3. Expand the trigger to `shared/**` so `shared/types/` changes fire the workflow too.
4. Add an integration step that diffs the C# DTO surface (via Swagger JSON, gated by the dev-only Swagger endpoint, or via a `dotnet run --generate-contracts` target) against `shared/types/api-responses.ts`. Drift = fail.

---

### Finding 3 — NuGet packages not cached across runs (LOW)

[backend.yml:19-26](.github/workflows/backend.yml#L19-L26) uses `actions/setup-dotnet@v4` without `cache: true`, and no `actions/cache@v4` step keys `~/.nuget/packages`. Every CI run does a clean restore, adding ~30-60 s per run.

By contrast:
- The Python job uses `setup-python@v5` with `cache: 'pip'` and the right `cache-dependency-path` — correct.
- The Node jobs use `setup-node@v4` with `cache: 'npm'` — correct.

**Recommendation:**

```yaml
- uses: actions/setup-dotnet@v4
  with:
    dotnet-version: '8.0.x'
    cache: true
    cache-dependency-path: backend/RestaurantApi/packages.lock.json
```

(or use `actions/cache@v4` keyed on a hash of `*.csproj`.) Pure perf win, no correctness impact.

---

### Finding 4 — No vulnerability audit step in any workflow (MEDIUM — same as Segment 25 F6)

Carry-over from [Segment 25 Finding 6](CODE_REVIEW_SEGMENT_25_SECURITY.md). None of the three workflows runs:

- `dotnet list package --vulnerable --include-transitive`
- `npm audit --production` (or `npm audit --audit-level=high`)
- `pip-audit` (or `safety check`)

So a CVE-pinned transitive dependency lands without any check. The segment-25 fix-order item is the same one-time addition.

---

### Finding 5 — Deploy jobs are echo placeholders but still gate on `environment: production` (LOW)

[backend.yml:44-50](.github/workflows/backend.yml#L44-L50):

```yaml
deploy:
  needs: [dotnet, python-ai]
  runs-on: ubuntu-latest
  environment: production
  steps:
    - name: Deploy
      run: echo "Deploy step — replace with your actual deploy command"
```

Same shape in [frontend.yml:52-58](.github/workflows/frontend.yml#L52-L58).

Today this is harmless — the `echo` always exits 0, so the job is green. But two concerns:
- `environment: production` requires the `production` environment to exist on the GitHub repo settings. If it does and has approval gates, every merge to `main` queues a manual approval that does nothing.
- If a real deploy command lands here later without re-reviewing the surrounding job (no `if: github.ref == 'refs/heads/main'` guard, no concurrency control), deploys will run on every PR's merge AND on every push to `develop`. The trigger today is `push: branches: [main, develop]` ([backend.yml:5](.github/workflows/backend.yml#L5)), so this job runs on both branches.

**Recommendation:** either fully delete the `deploy` jobs until a real deploy exists, or guard them with `if: github.ref == 'refs/heads/main'` and add `concurrency: { group: deploy-backend, cancel-in-progress: false }` so two pushes don't race.

---

### Finding 6 — Branch protection / required status checks not verifiable (OPEN — roadmap follow-up)

The roadmap follow-up: *"Is there a status check rule on `main` requiring all three workflows to pass before merge?"*

This is configured in GitHub repository settings, not in the workflow files. Cannot verify from a read-only file pass.

**Recommendation:** confirm in the repo's *Branch protection rules* that:
- `main` requires PR review + status checks `Backend CI`, `Frontend CI`, and `Contract Validation` to pass before merge.
- `Require branches to be up to date before merging` is on.
- Direct pushes to `main` are blocked.

If branch protection is off, then this whole CI suite is advisory and any merge can land regardless.

---

### Finding 7 — `pytest` runs without coverage or junit reporting; no minimum test count assertion (LOW)

[backend.yml:42-43](.github/workflows/backend.yml#L42-L43):

```yaml
- run: pip install pytest
- run: pytest --tb=short
```

If `pytest` finds zero tests, the command exits 0 (pytest 7+ exits 5 — "no tests collected"). Need to verify which behavior is in effect by pinning a pytest version. With `pip install pytest` (unpinned), the contract is whatever the latest is at run-time.

Same lack of test-output capture in the dotnet step ([line 26](.github/workflows/backend.yml#L26)): no `--logger trx` or `--results-directory` flag, no `actions/upload-artifact` for the results. PR failures are visible in raw logs only.

**Recommendation:**
- Pin pytest in `requirements-dev.txt` (or in `requirements.txt`) and use `pytest --strict-markers --junit-xml=junit.xml`.
- Add `dotnet test --logger "trx;LogFileName=test_results.trx" --results-directory ./TestResults` and upload as an artifact for failed runs.
- Add `dorny/test-reporter@v1` (or equivalent) to surface failed test names directly in the PR check.

---

### Finding 8 — Frontend workflow doesn't run on `shared/contracts/**` changes (LOW NIT)

[frontend.yml:11-14](.github/workflows/frontend.yml#L11-L14):

```yaml
paths:
  - 'frontend-customer/**'
  - 'frontend-admin/**'
  - 'shared/types/**'
```

`shared/contracts/**` is not listed. If someone changes `api-contract-ai.json`, the contracts workflow fires (Finding 2: only JSON-syntax check) but neither frontend re-type-checks. Since neither frontend currently imports anything from `shared/contracts/` (per Segment 24), this is a latent issue, not an active one. Worth fixing pre-emptively.

---

### Finding 9 — Backend workflow runs `dotnet test` in Release configuration without `--blame-crash` (LOW)

[backend.yml:26](.github/workflows/backend.yml#L26):

```yaml
- run: dotnet test --no-build --configuration Release --verbosity normal
```

Tests run in Release. That's faster and matches the build configuration. But Testcontainers occasionally hangs on slow CI runners; without `--blame` / `--blame-crash` / `--blame-hang-timeout 10m`, a hung test means the whole job times out at 6 hours (GitHub default) with no useful output.

**Recommendation:**

```yaml
- run: dotnet test --no-build --configuration Release --verbosity normal --blame-hang-timeout 5m --blame-crash
```

Plus a job-level `timeout-minutes: 30` to bound runaway runs.

---

## Summary

**Top three risks**
1. **F1** — frontend tests don't run (customer side is an `echo`) and lint doesn't run anywhere. The most-edited surface of the project has the weakest CI signal.
2. **F2** — contract validation is a JSON-parseability check, not a contract check. Drift documented in Segment 24 lives in the codebase today and CI didn't catch it.
3. **F4** — no vulnerability scanning. A CVE'd dependency merges silently.

**Top three strengths**
- Backend job uses Testcontainers + real Postgres in CI, not in-memory EF or SQLite — matches prod behaviour. This is the right shape and most projects get it wrong.
- npm and pip caches are wired correctly via `setup-node`/`setup-python`. Cuts cold-start time meaningfully.
- Workflows are path-filtered, so a docs-only change doesn't burn five jobs. Sensible default.

**Recommended fix order**
1. F1.1 — replace customer `test` script with `vitest run` (matches admin shape).
2. F1.2 — add `lint:check` (no `--fix`) to both frontends, invoke from CI.
3. F4 — three one-line CI steps (`dotnet list package --vulnerable`, `npm audit`, `pip-audit`).
4. F2 — expand contracts workflow to type-check shared types against both consumers; gate on `shared/**` instead of `shared/contracts/**`.
5. F6 — verify and document branch protection rules.
6. F5, F7, F9 — polish (deploy gating, test reporting, blame-hang timeout).
7. F3, F8 — perf / nits.

No code modified during this review.

---

## Roadmap completion

This is the last segment in the 25-segment roadmap. With Segment 02 done, all 25 reports exist:

| # | Title | Report |
|---|---|---|
| 01 | Compose & service topology | [CODE_REVIEW_SEGMENT_01_COMPOSE_TOPOLOGY.md](CODE_REVIEW_SEGMENT_01_COMPOSE_TOPOLOGY.md) |
| 02 | CI workflows & build hygiene | *this report* |
| 03 | API startup pipeline | [CODE_REVIEW_SEGMENT_03_STARTUP.md](CODE_REVIEW_SEGMENT_03_STARTUP.md) |
| 04 | Multi-tenancy enforcement | [CODE_REVIEW_SEGMENT_04_MULTI_TENANCY.md](CODE_REVIEW_SEGMENT_04_MULTI_TENANCY.md) |
| 05 | Auth & token lifecycle | [CODE_REVIEW_SEGMENT_05_AUTH.md](CODE_REVIEW_SEGMENT_05_AUTH.md) |
| 06 | Data layer & migrations | [CODE_REVIEW_SEGMENT_06_DATA_MIGRATIONS.md](CODE_REVIEW_SEGMENT_06_DATA_MIGRATIONS.md) |
| 07 | Domain models & invariants | [CODE_REVIEW_SEGMENT_07_DOMAIN_MODELS.md](CODE_REVIEW_SEGMENT_07_DOMAIN_MODELS.md) |
| 08 | Controllers — admin | [CODE_REVIEW_SEGMENT_08_ADMIN_CONTROLLERS.md](CODE_REVIEW_SEGMENT_08_ADMIN_CONTROLLERS.md) |
| 09 | Controllers — customer | [CODE_REVIEW_SEGMENT_09_CUSTOMER_CONTROLLERS.md](CODE_REVIEW_SEGMENT_09_CUSTOMER_CONTROLLERS.md) |
| 10 | Background & domain services | [CODE_REVIEW_SEGMENT_10_BACKGROUND_SERVICES.md](CODE_REVIEW_SEGMENT_10_BACKGROUND_SERVICES.md) |
| 11 | Email subsystem | [CODE_REVIEW_SEGMENT_11_EMAIL.md](CODE_REVIEW_SEGMENT_11_EMAIL.md) |
| 12 | SignalR hubs & realtime | [CODE_REVIEW_SEGMENT_12_SIGNALR.md](CODE_REVIEW_SEGMENT_12_SIGNALR.md) |
| 13 | Backend tests | [CODE_REVIEW_SEGMENT_13_TESTS.md](CODE_REVIEW_SEGMENT_13_TESTS.md) |
| 14 | AI service — bootstrap & routers | [CODE_REVIEW_SEGMENT_14_AI_BOOTSTRAP.md](CODE_REVIEW_SEGMENT_14_AI_BOOTSTRAP.md) |
| 15 | AI service — vision extraction | [CODE_REVIEW_SEGMENT_15_VISION.md](CODE_REVIEW_SEGMENT_15_VISION.md) |
| 16 | AI service — engines | [CODE_REVIEW_SEGMENT_16_AI_ENGINES.md](CODE_REVIEW_SEGMENT_16_AI_ENGINES.md) |
| 17 | AI service tests | [CODE_REVIEW_SEGMENT_17_AI_TESTS.md](CODE_REVIEW_SEGMENT_17_AI_TESTS.md) |
| 18 | Customer SPA — shell & state | [CODE_REVIEW_SEGMENT_18_CUSTOMER_SHELL.md](CODE_REVIEW_SEGMENT_18_CUSTOMER_SHELL.md) |
| 19 | Customer SPA — realtime | [CODE_REVIEW_SEGMENT_19_CUSTOMER_REALTIME.md](CODE_REVIEW_SEGMENT_19_CUSTOMER_REALTIME.md) |
| 20 | Customer SPA — i18n | [CODE_REVIEW_SEGMENT_20_CUSTOMER_I18N.md](CODE_REVIEW_SEGMENT_20_CUSTOMER_I18N.md) |
| 21 | Admin SPA — auth store & guards | [CODE_REVIEW_SEGMENT_21_ADMIN_AUTH.md](CODE_REVIEW_SEGMENT_21_ADMIN_AUTH.md) |
| 22 | Admin SPA — menu management & import | [CODE_REVIEW_SEGMENT_22_MENU_MANAGEMENT.md](CODE_REVIEW_SEGMENT_22_MENU_MANAGEMENT.md) |
| 23 | Admin SPA — orders, metrics, AI config | [CODE_REVIEW_SEGMENT_23_ADMIN_ORDERS_METRICS_AI.md](CODE_REVIEW_SEGMENT_23_ADMIN_ORDERS_METRICS_AI.md) |
| 24 | Shared contracts | [CODE_REVIEW_SEGMENT_24_SHARED_CONTRACTS.md](CODE_REVIEW_SEGMENT_24_SHARED_CONTRACTS.md) |
| 25 | End-to-end security review | [CODE_REVIEW_SEGMENT_25_SECURITY.md](CODE_REVIEW_SEGMENT_25_SECURITY.md) |
