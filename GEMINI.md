# Workspace Coding Rules: Karpathy Guidelines

Behavioral guidelines to reduce common LLM coding mistakes, derived from Andrej Karpathy's observations on LLM coding pitfalls.
These rules bias toward caution, simplicity, and surgical precision over speed.

---

## 1. Think Before Coding
**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State assumptions explicitly. If uncertain, ask for clarification.
- If multiple interpretations exist, present them rather than picking one silently.
- If a simpler approach exists, propose it and push back when warranted.
- If anything is unclear, stop immediately, identify what is confusing, and ask.

---

## 2. Simplicity First
**Minimum code that solves the problem. Nothing speculative.**

- Implement only what was directly requested—no unsolicited features.
- Avoid premature abstractions for single-use code.
- Avoid unnecessary "flexibility" or "configurability" that wasn't requested.
- Do not write defensive error handling for impossible scenarios.
- If 200 lines of code could be written cleanly in 50 lines, rewrite it to be simpler.
- Ask: *"Would a senior engineer consider this overcomplicated?"* If yes, simplify.

---

## 3. Surgical Changes
**Touch only what you must. Clean up only your own mess.**

When editing existing code:
- Do not modify adjacent code, comments, or formatting unless directly related to the task.
- Do not refactor parts of the codebase that are not broken.
- Match existing repository style and conventions, even if you prefer an alternative.
- If unrelated dead code or bugs are noticed, mention them to the user instead of silently altering or deleting them.

When changes create orphans:
- Clean up unused imports, variables, and helper functions introduced by your changes.
- Do not remove pre-existing unused code unless explicitly instructed.
- **Rule of thumb:** Every changed line must trace directly to the user's request.

---

## 4. Goal-Driven Execution
**Define success criteria. Loop until verified.**

Transform tasks into verifiable goals:
- *"Add validation"* → Write tests / checks for invalid inputs, then make them pass.
- *"Fix the bug"* → Reproduce with a test / check, apply fix, verify it passes.
- *"Refactor X"* → Ensure existing tests / behavior pass before and after.

For multi-step tasks, outline a concise plan:
1. `[Step 1]` → verify: `[check]`
2. `[Step 2]` → verify: `[check]`
3. `[Step 3]` → verify: `[check]`

---

## 5. Repository Map & Multi-Agent Hygiene
Always read and strictly obey `D:\NEXUS\AGENTS.md` (Sections 1–6) for the canonical directory layout, single-node MSBuild rules, and anti-sprawl workspace hygiene. Never create new `.md` reports, logs, backup folders, or test dumps in `D:\NEXUS` or `D:\NEXUS\Nexus-CSharp` root.
- **Windows-Only & Zero Engine/Model Duplication**: Target exclusively Windows (`net8.0-windows` / `win-x64`). NEVER copy or move `D:\NEXUS\Engine` or `D:\NEXUS\Models` into `bin\Debug`, `bin\Release`, or `obj\`. Always preserve `nexus_data\conversations` inside `Nexus.App\bin` so user chat history is never lost.

