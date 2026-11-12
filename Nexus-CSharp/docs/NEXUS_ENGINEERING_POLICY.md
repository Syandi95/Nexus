# Nexus Engineering Policy — Workflow V2.0

Approved by the project owner on 12 October 2026. This policy governs all coding
agents and engineering work in Nexus. Architecture: native Windows, WPF, C#/.NET 8.
The latest explicit owner instruction governs authorized scope; historical reports
remain immutable evidence, not silently rewritten acceptance definitions.

## 1. Acceptance before implementation

Use **UNDERSTAND → REPRODUCE/AUDIT → FIX → VERIFY → REPORT**.

Read the request and actual affected source. Identify contracts, known evidence,
material assumptions, ownership and verifiable outcomes before writing code.
Use proven root causes from earlier reports instead of restarting the same audit.
Audit and directly implement a clear minimal fix within authorized scope without
unnecessary approval loops. For nonmaterial details, use safe repository conventions
and record assumptions. STOP for ambiguity affecting safety, scope or requested result.

## 2. Simplicity and surgical changes

Choose the simplest correct existing Nexus solution. Do not add a framework for one
case, unjustified dependency/abstraction, speculative configuration, or a retry/fallback
that hides a defect. Every changed line must relate to the goal. Do not reformat,
refactor, delete pre-existing dead code, remove another agent's work, or reverse
approved UI/UX. Clean only orphan code introduced by the current change.

At most **two directed fix cycles for one root cause**, then review with evidence.
Do not retry until PASS, layer speculative patches, top up stochastic calls, or keep
auditing/testing after the relevant acceptance is proven without a new reason.

## 3. Enhanced Open Source first

Before a substantive component: inspect Nexus implementations, then mature Microsoft,
GitHub and ecosystem projects. Assess license, maintenance, known security concerns,
and native .NET 8/WPF compatibility. Record USE / ADAPT / ENHANCE / REJECT / NOT NEEDED
with rationale. Implement only necessary parts. Audited external development tooling
does not automatically become a Nexus runtime/product dependency. Keep native WPF/C#
architecture unless the owner explicitly approves a change.

## 4. User work, ownership and checkpoints

Before edits, capture Git status, affected-file SHA-256 and staged identity; distinguish
pre-existing work from this task. Reconcile differences against the latest valid
checkpoint. Use one writer per affected file; coordinate simultaneous agents, or use
an authorized isolated worktree. STOP if ownership/conflicts cannot be reconciled.
Do not reset, restore, clean, stash, commit, merge or destructively roll back automatically.
Preserve staged, unstaged and untracked user work; build outputs are generated evidence,
not permission to overwrite source. Preserve relevant pre-build generated identities
when builds change them. Do not overwrite existing checkpoints or consumed-run evidence.

Record changed paths, before/after hashes, test commands/results, source and tooling
identity, and limitations. A checkpoint is not a certification or execution approval.
New policy, acceptance, test or tooling changes require new candidate hashes and a
future source freeze; no consumed approval can be reused.

## 5. Features and UI/UX

Preserve all approved features and native WPF behavior, accessibility, Light/Dark/High
Contrast, visual fidelity, focus/DPI behavior and ResourceDictionary correctness.
Use existing tokens/semantic resources and feature architecture. Source and affected
tests may change together when the owner authorizes the feature, bug or visual contract.
Document before/after evidence for expected-value or golden-baseline changes.

## 6. Tests and evidence governance

Tests may change for approved feature/contract changes, correction of wrong behavior,
approved UI/UX baselines, proven isolation defects, or outdated golden baselines under
the new approved design. They may not change merely because assertions fail, actual
output differs, tests are slow, a stochastic model fails, or PASS is desired.

Record old contract, new contract, rationale and before/after evidence. Preserve strict
validators and original failure reporting. Never report a skipped/undiscovered test as
PASS, silently hide failures, or describe historical execution as fresh execution.
Run targeted tests first, then impacted regression; broaden/repeat only for a changed
input, new failure or unresolved concern. Full solution certification is a separate,
explicitly approved frozen-source operation.

## 7. Gate H Option B — narrowly approved exception

Read `GATE_H_ACCEPTANCE_OPTION_B.md` and `gate-h-acceptance-manifest.json`.
All deterministic safety and functional tests, genuine Gate F/G/H repair, WPF/OCR/ONNX/
runtime mandatory regression remain mandatory PASS. A genuine repair that does not
produce a valid plan is a functional failure.

Only `GateHP1NativeSelectionLiveTests.CandidateArrays_ProductionAdapterAndDecoder_ReadOnlyColdWarmChat`
is a probabilistic model reliability diagnostic. Preserve its scenario, schema,
token cap, strict success assertions, independent execution and visible failures.
OutputLimit is never a native tool-call success. Safe rejection may be a RELIABILITY
FAILURE; unauthorized mutation, invalid approval, malformed-output acceptance or a
deterministic host defect is always a mandatory blocker. No other flaky-test exemption.

## 8. Process ownership and deadlines

Use the verified Windows Job Object launcher for certification/diagnostic subprocesses:
direct suspended root assigned and verified before resume; descendants inherit kernel
membership; cleanup only via its anonymous Job. Names, PID history, ancestry and
enumeration do not establish cleanup ownership. Never terminate foreign processes.
Do not weaken the 120-second Agent deadline, token caps, approval independence,
SafeWrite/recovery, repair budgets, strict protocol or default-deny authority.

## 9. Required STOP conditions

STOP and request review for unresolved source ownership; unapproved safety/authority/
architecture changes; weakened approval/SafeWrite/recovery; a disabled mandatory test;
required model/tool runtime replacement; unexplained deterministic regression after
bounded investigation; possible foreign-process cleanup; inability to preserve the P1
diagnostic; or an acceptance change beyond Option B. A safely rejected read-only
stochastic OutputLimit alone does not justify a speculative production patch.

After completing authorized implementation and reporting, STOP. Full Gate H certification
requires a **new explicit source-freeze approval**. Gate I never starts automatically.
