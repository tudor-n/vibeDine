# Handoff — Menu Digitalization Feature

You (the new agent) are picking up an in-progress feature called **menu digitalization**: an admin wizard that turns photos of a paper menu into digital `Products` + `Categories` via a vision-LLM. The previous session wrote the spec, wrote a 19-task implementation plan, and finished the first two tasks. This document tells you exactly what's on disk, what constraints to respect, and what to do next.

Working directory: `d:\smart-restaurant`. Branch: `feature/digitalizing-menu`.

---

## 1. Read these first (in order)

1. **`docs/superpowers/specs/2026-05-19-menu-digitalization-design.md`** — the design spec. Read sections §1 (goal), §2 (scope), §3 (architecture), §4 (data contracts), §5 (prompt), §6 (frontend wizard), §7 (error handling), §10 (acceptance criteria). About 10 minutes; do not skim.
2. **`docs/superpowers/plans/2026-05-19-menu-digitalization.md`** — the implementation plan. 19 tasks, each a TDD cycle. Tasks 1 and 2 are done; you're starting from Task 3.
3. **`C:\Users\Tudor\.claude\projects\d--smart-restaurant\memory\MEMORY.md`** — the project memory (auto-loaded on session start). Contains the no-git rule below in canonical form.

---

## 2. Hard constraints (do not violate)

### 2.1 No git operations — ever

The user manages git manually. This is a durable preference recorded in memory at `feedback_git_manual.md`.

- **Never** run `git add`, `git commit`, `git push`, `git pull`, `git branch`, `git merge`, `git reset`, `git checkout` of branches, `git stash`, or any other git command that mutates state. Read-only inspection (`git status`, `git diff`, `git log`, `git show`) is fine.
- The plan's per-task "Checkpoint" steps are **informational only** — they describe what's ready and suggest a commit message. **Do not execute them.**
- When you dispatch implementer subagents, make this constraint explicit in their prompt. Example wording: *"CRITICAL CONSTRAINT — NO GIT COMMANDS. User manages git manually. Do NOT run git add/commit/push or any state-changing git command. Skip the 'Checkpoint' step entirely."*
- This overrides the default behavior of skills like `superpowers:subagent-driven-development` and `superpowers:test-driven-development` that try to commit after each green test.

### 2.2 Subagent-driven execution

The user chose subagent-driven execution. Use the **`superpowers:subagent-driven-development`** skill. For each task:

1. Dispatch a **general-purpose** Agent (Task tool) with the full plan task text inlined into the prompt — don't make the subagent read the plan file.
2. Wait for the implementer's report (status DONE / DONE_WITH_CONCERNS / BLOCKED / NEEDS_CONTEXT).
3. Dispatch a spec-compliance reviewer (reads the actual files, compares to the spec verbatim).
4. If spec ✅, dispatch a code-quality reviewer.
5. Only mark the task complete in TaskCreate/TaskUpdate after both reviews pass.

Model selection:
- **Haiku** for mechanical file creation tasks with verbatim code from the plan (Tasks 1, 2, 7, 9, 13, 15, 16, 18).
- **Sonnet** (default) for tasks that integrate multiple files, modify existing files, or involve nontrivial logic (Tasks 3, 4, 5, 6, 8, 10, 11, 12, 14, 17).
- Task 19 is manual verification — done together with the user, not dispatched.

### 2.3 Don't expand scope

The spec deliberately defers description/allergens/hierarchical categories/multi-language to later iterations. v1 = `name + price + category_hint`. If a reviewer suggests adding tests for fields v1 doesn't use, decline. If you find yourself wanting to "improve" the spec while implementing, stop and surface it to the user instead of expanding.

---

## 3. Current state on disk

### 3.1 Files created (uncommitted, will show up in `git status`)

```
docs/superpowers/specs/2026-05-19-menu-digitalization-design.md      # the spec
docs/superpowers/plans/2026-05-19-menu-digitalization.md             # the plan
docs/superpowers/HANDOFF-menu-digitalization.md                      # this file

# Task 1 (DONE)
shared/types/menu-validation.ts
shared/types/menu-import.ts

# Task 2 (DONE)
backend/ai-scripts/models/menu_extraction.py
backend/ai-scripts/tests/test_menu_extraction_parsing.py
backend/ai-scripts/requirements.txt   # MODIFIED — added `pytest` (was missing; needed to run tests)
```

### 3.2 Files unchanged from before this feature

Everything else, including the existing modified files in `git status` (the previous owner's in-flight changes in `frontend-admin/index.html`, `frontend-customer/`, `smart restaurant (3)/design_handoff_clarifi/*`) — those are **NOT** part of this feature. Leave them alone.

### 3.3 Memory

`C:\Users\Tudor\.claude\projects\d--smart-restaurant\memory\` contains:
- `MEMORY.md` — the index
- `feedback_git_manual.md` — the no-git rule (full text)

---

## 4. What to do next

### 4.1 Resume from Task 3

Task 3 of the plan: **VisionExtractor protocol + fake-for-tests**. Files involved:
- Create: `backend/ai-scripts/services/vision_extractor.py`
- Modify: `backend/ai-scripts/tests/test_menu_extraction_parsing.py` (extend with 2 fake-extractor tests)
- Possibly modify: `backend/ai-scripts/requirements.txt` and `backend/ai-scripts/pytest.ini` if `pytest-asyncio` isn't configured

The full task text (including the test code and the implementation) is in the plan at `docs/superpowers/plans/2026-05-19-menu-digitalization.md` under `## Task 3`. **Copy that text verbatim into your implementer's prompt** — don't paraphrase, don't make the subagent re-read the plan, don't make architectural changes.

### 4.2 Recommended first action

1. Announce: *"I'm resuming the menu-digitalization plan from Task 3 via subagent-driven-development."*
2. Read the spec sections listed in §1 above.
3. Clean up the TaskList — delete any stale entries from prior sessions, create one task per remaining plan task (Tasks 3–19) so progress is visible.
4. Mark Task 3 in_progress, dispatch the implementer subagent with the Task 3 text verbatim and the no-git constraint baked into the prompt.

### 4.3 After each task

- Update TaskList: mark current task complete only when both spec and code-quality reviewers approved.
- If a reviewer reports issues that **block correctness**, re-dispatch the implementer to fix; loop until approved.
- If a reviewer reports only **minor style/test-coverage suggestions**, surface them to the user as a choice ("skip, fix, or stop") rather than acting unilaterally — that's the policy the user set on Task 2.
- Continue to the next task without pausing for "should I continue?" prompts. The user has already authorized execution of the whole plan.

### 4.4 At the end (after Task 18 is green)

Task 19 is a manual end-to-end verification, walked through interactively with the user. Once Tasks 3–18 are complete, hand back to the user with a summary of:
- All files created / modified.
- Test results across Python, C#, and frontend.
- A reminder that nothing has been committed and the user should review the work tree before deciding what to commit.

---

## 5. Known nits, deferred from Task 2

The Task 2 code-quality reviewer flagged these; the user chose to skip them at the time. Do not block on them, but track them so they aren't lost:

1. Boundary tests missing in `backend/ai-scripts/tests/test_menu_extraction_parsing.py`:
   - Name length 121 → reject.
   - Price = 0 and price > 99,999,999.99 → reject.
   - Confidence < 0 or > 1 → reject.
   - `source_page` / `source_index` < 0 → reject.
2. `test_extracted_menu_item_accepts_future_optional_fields` uses `item.description.startswith("Bucățele")` — equality would be less fragile.
3. `MenuExtractionResponse.schema_version` lacks a docstring explaining the versioning intent.

If at the end of the run you have spare cycles, you can offer to clean these up in one targeted task. Don't sneak them into other tasks.

---

## 6. Things to watch out for

- **Pydantic v2** is in use (see `requirements.txt`). The plan's `Field(...)` syntax is v2-compatible; do not migrate or "modernize" further.
- **The C# project has no test infrastructure yet** — Task 7 sets it up (new `RestaurantApi.Tests` project). Several later tasks depend on it; don't skip Task 7 or attempt to add tests to an arbitrary location.
- **Vision-LLM provider** defaults to Claude (per Task 4). The `ANTHROPIC_API_KEY` env var is required to run the real provider, but **never required for tests** — tests inject the `FakeVisionExtractor` via FastAPI dependency override (Task 6).
- **Romanian-language UI** — keep all admin-facing strings in Romanian to match the rest of the admin app. The plan's Vue components already have the correct text; copy verbatim.
- **Existing design tokens** — the admin app uses `pill-btn`, `card-paper`, `card-white`, `eyebrow`, `display-title`, `field-input`, CSS custom properties like `var(--ink)`, `var(--lime)`. Reuse, don't reinvent. The plan's component code already does this correctly.
- **The `MenuManagementController` is shared real estate** — Task 8 refactors it (extracting `ProductValidator`) and Task 12 adds the bulk-create action to it. Do these in plan order, not concurrently, so the file's state is predictable.

---

That's it. Read the spec, read the plan, then go.
