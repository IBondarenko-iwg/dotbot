# Issue 123: Duplicate Function Consolidation Plan

## Challenge Assessment

- Issue: Consolidate duplicate PowerShell function definitions across modules where signature drift or ambiguous ownership creates bugs.
- Soundness: 90%
- Why this is sound:
  - The reported duplicates exist in the current tree.
  - At least one duplicate set already caused a real bug around `Write-Status` import behavior (PowerShell runs the most recently imported same-name command; importing the wrong module last silently replaces the intended implementation).
  - Several duplicates represent shared task-domain logic that has already drifted.
- Remaining design questions:
  - Whether task slugs must be canonical across MCP task references and runtime worktree naming.
  - Whether `Write-Status` should become a single cross-layer API or be renamed by layer to prevent shadowing.
  - Whether `Send-Whisper` should share an internal helper only, or stay fully separate because the two call paths have different targeting semantics.

## Context Sources

- GitHub issue 123: duplicate function audit and suggested approach.
- `CLAUDE.md`
- `scripts/Platform-Functions.psm1`
- `stacks/dotnet/hooks/dev/Common.ps1`
- `workflows/default/systems/runtime/modules/DotBotTheme.psm1`
- `workflows/default/systems/runtime/modules/WorktreeManager.psm1`
- `workflows/default/systems/mcp/modules/TaskMutation.psm1`
- `workflows/default/systems/mcp/modules/TaskStore.psm1`
- `workflows/default/systems/ui/modules/TaskAPI.psm1`
- `workflows/default/systems/ui/modules/StateBuilder.psm1`
- `workflows/default/systems/ui/modules/ControlAPI.psm1`
- `workflows/default/hooks/scripts/steering.ps1`
- `tests/Test-Helpers.psm1`
- `tests/Test-TaskActions.ps1`

## Classes Dependency Hierarchy

### Level 0: Shared Helper Ownership Decisions

- `TaskPathHelpers` or equivalent shared MCP helper module
  - Responsibility: Own task-directory resolution, roadmap dependency parsing, and todo-record lookup used by both MCP and UI layers.
  - Placement: `workflows/default/systems/mcp/modules/`.
  - Reason: The duplicated task helpers are task-domain concerns, and MCP already owns task mutation and storage behavior.
  - **Dependency direction note**: This placement means `systems/ui/` modules will depend on a module under `systems/mcp/`. This is a deliberate cross-system dependency that should be made explicit in any module manifest or import comments so future contributors do not reverse it.

- `dotbot-mcp-helpers.ps1` (already exists)
  - Responsibility: Own the canonical `Send-McpRequest` implementation for MCP tool tests. Currently contains JSON-RPC and date helpers; `Send-McpRequest` should be added here.
  - Placement: `workflows/default/systems/mcp/dotbot-mcp-helpers.ps1` — already dot-sourced by every tool test.ps1.
  - Reason: The current duplication is test-only and should be removed before touching higher-risk runtime code. Do NOT create a new module; the shared script already exists.

### Level 1: Shared Task-Domain Consumers

- `TaskMutation.psm1`
  - Responsibility: Continue to own task mutation workflows, but stop owning duplicated general-purpose task helper implementations.
  - Depends on: shared task helper module.

- `TaskStore.psm1`
  - Responsibility: Continue to own task state transitions and status directory access.
  - Depends on: shared task helper module for base task path resolution.

- `TaskAPI.psm1`
  - Responsibility: UI-facing task operations should consume the same task lookup and path-resolution helpers as MCP.
  - Depends on: shared task helper module.

- `StateBuilder.psm1`
  - Responsibility: Continue building UI state, but read roadmap dependency fallback data through the shared task-domain helper.
  - Depends on: shared task helper module.

### Level 2: Naming Clarification Consumers

- `Platform-Functions.psm1`
  - Responsibility: Keep install and script output helpers, but avoid exporting or defining a `Write-Status` name that can be mistaken for the runtime theme function.

- `Common.ps1`
  - Responsibility: Keep dotnet stack-local output behavior or adopt a shared output helper, but avoid a conflicting `Write-Status` symbol.

- `DotBotTheme.psm1`
  - Responsibility: Remain the canonical runtime/UI themed status writer if the repo chooses a single `Write-Status` owner.

- `steering.ps1`
  - Responsibility: Send a whisper to a specific session/process target.

- `ControlAPI.psm1`
  - Responsibility: Send a whisper to one or more running instances selected by type.

- `WorktreeManager.psm1`
  - Responsibility: Continue to own worktree-path-safe slug generation unless slug behavior is unified across the task system.
  - Note: The 50-char truncation in WorktreeManager is a **Windows MAX_PATH defensive measure** (worktree paths become subdirectories on disk), not a Git protocol requirement. Git itself imposes no branch name length limit; `git check-ref-format` enforces character rules only.

## Consolidation Categories

### Category A: Safe Quick Wins

- `Send-McpRequest`
  - Move into the already-existing `workflows/default/systems/mcp/dotbot-mcp-helpers.ps1`, which is already dot-sourced by all tool tests.
  - Remove the embedded copies from MCP tool `test.ps1` files.
  - **Parameter order must be unified**: `Test-Helpers.psm1` declares `($Process, $Request)`; tool test copies declare `($Request, $Process)`. All current call sites use named parameters so both work today, but the canonical version must pick one order and all callers audited for any positional use.

- `Get-TodoTaskRecord`
  - Consolidate into the shared task helper module.
  - Use the richer record shape as the canonical result.

- `Get-RoadmapOverviewDependencyMap`
  - Consolidate into the shared task helper module.
  - Preserve existing roadmap-overview parsing behavior.

### Category B: Shared Logic With Signature Alignment

- `Get-TasksBaseDir`
  - Consolidate into the shared task helper module.
  - Preserve support for explicit test overrides while still supporting normal bot-root resolution.

### Category C: Requires Explicit Naming Decision

- `Write-Status`
  - Either establish `DotBotTheme.psm1` as the single canonical owner for runtime/UI contexts and rename other layer-specific helpers, or rename all layer-specific variants so the shared name is no longer ambiguous.

- `Send-Whisper`
  - Keep separate public names because the session-targeted and instance-type-targeted behaviors are not the same operation.
  - Optionally share a low-level whisper-file append helper later.

### Category D: Requires Product Decision

- `Get-TaskSlug`
  - If task-reference aliases and worktree branch names must be canonical, adopt one slug algorithm and use it in both consumers.
  - If the two contexts have different requirements, rename the helpers to reflect those requirements and stop treating them as duplicates.

## Implementation Steps

### Step 1: Consolidate MCP tool test request helper

- Files to modify:
  - `workflows/default/systems/mcp/dotbot-mcp-helpers.ps1` — add `Send-McpRequest` here (this file is already dot-sourced by all tool tests)
  - `workflows/default/systems/mcp/tools/*/test.ps1` — remove local `Send-McpRequest` definitions; the shared version is already in scope via the existing dot-source
  - `tests/Test-Helpers.psm1` — align `Send-McpRequest` parameter order to match the canonical version
  - `workflows/default/systems/mcp/README-NEWTOOL.md` if it documents the old self-contained pattern
- Purpose:
  - Make one `Send-McpRequest` implementation authoritative.
  - Resolve the latent parameter order reversal (`($Process, $Request)` vs `($Request, $Process)`) as part of the same change.
  - Reduce low-risk duplicate surface area first.
- Note: Do NOT create a new helper module — `dotbot-mcp-helpers.ps1` already exists for this purpose.
- Serena: use `safe_delete_symbol` on each tool test's `Send-McpRequest` before removing it to confirm no other callers exist. Use `replace_content` (regex) to bulk-remove the function definition blocks across all 15 files.
- Expected risk: Low
- Checkpoint: All MCP tool tests consume the shared helper in `dotbot-mcp-helpers.ps1`. No local `Send-McpRequest` definitions remain in tool `test.ps1` files.

### Step 2: Introduce shared task helper ownership

- Files to create or modify:
  - `workflows/default/systems/mcp/modules/` shared helper module
  - `workflows/default/systems/mcp/modules/TaskMutation.psm1`
  - `workflows/default/systems/mcp/modules/TaskStore.psm1`
  - `workflows/default/systems/ui/modules/TaskAPI.psm1`
  - `workflows/default/systems/ui/modules/StateBuilder.psm1`
- Purpose:
  - Establish one owner for `Get-TasksBaseDir`, `Get-RoadmapOverviewDependencyMap`, and `Get-TodoTaskRecord`.
  - Prevent UI and MCP task behavior from drifting further.
- Serena: use `find_referencing_symbols` on each function before moving it to enumerate every consumer precisely. Use `replace_symbol_body` to replace each duplicate body with a delegation call once the shared module exists.
- Expected risk: Medium
- Checkpoint: One canonical owner exists for `Get-TasksBaseDir`, `Get-RoadmapOverviewDependencyMap`, and `Get-TodoTaskRecord`. All consumers import from the shared module.

### Step 3: Update task-domain tests and integration coverage

- Files to modify:
  - `tests/Test-TaskActions.ps1`
  - `tests/Test-Components.ps1`
  - any MCP or UI tests that assume the old helper ownership
- Purpose:
  - Verify shared helpers work through both MCP and UI code paths.
  - Preserve task lookup, ignore-state, and roadmap fallback behavior.
- Expected risk: Medium
- Checkpoint: MCP and UI task flows both pass against the shared helper implementation. Ignore-state and roadmap fallback behavior is unchanged.

### Step 4: Remove naming collisions in status-output helpers

- Files to modify:
  - `scripts/Platform-Functions.psm1`
  - `stacks/dotnet/hooks/dev/Common.ps1`
  - `workflows/default/systems/runtime/modules/DotBotTheme.psm1`
  - call sites in install scripts, stack hooks, and runtime/UI scripts as needed
- Purpose:
  - Eliminate ambiguous `Write-Status` symbol collisions.
  - Protect existing runtime/UI `-Type` call sites from importing the wrong function.
- Serena: use `rename_symbol` targeting `Write-Status` in `Platform-Functions.psm1` and `Common.ps1` separately for codebase-wide rename. Serena propagates all call-site updates automatically.
- Expected risk: Medium
- Checkpoint: No ambiguous `Write-Status` import path remains. Output helper names reflect layer ownership clearly enough to prevent shadowing bugs.

### Step 5: Clarify whisper operation ownership

- Files to modify:
  - `workflows/default/hooks/scripts/steering.ps1`
  - `workflows/default/systems/ui/modules/ControlAPI.psm1`
  - `workflows/default/systems/ui/server.ps1`
  - related tests if added or updated
- Purpose:
  - Rename the two whisper operations so their targeting model is explicit.
  - Avoid same-name functions with different semantics.
- Serena: use `rename_symbol` on each `Send-Whisper` definition independently (scoped to its file) to rename to the chosen target name. Codebase-wide reference updates are automatic.
- Expected risk: Low
- Checkpoint: Whisper operations are distinguishable by name and target model. UI and steering call sites are unambiguous.

### Step 6: Resolve slug policy and implement accordingly

- Files to modify:
  - `workflows/default/systems/mcp/modules/TaskMutation.psm1`
  - `workflows/default/systems/runtime/modules/WorktreeManager.psm1`
  - tests covering task creation and worktree naming
- Purpose:
  - Either unify slug generation or make the differing intent explicit by name.
- Expected risk: Medium because branch-name behavior and task-reference behavior may already be relied on indirectly.
- Note: The 50-char slug limit in WorktreeManager is a path-length guard for Windows (MAX_PATH = 260 chars), not a Git naming constraint. Any unified algorithm must preserve this guard to avoid worktree creation failures on Windows.
- Checkpoint: Slug behavior is explicitly defined by policy. Either one canonical algorithm is in use across both consumers, or the two algorithms have distinct names and documented responsibilities.

## Testing Requirements

### Functional Scenarios

- MCP tool tests can still start the MCP process, send requests, and parse responses after `Send-McpRequest` consolidation.
- UI and MCP task operations resolve the same base task directory in normal repo execution.
- Test paths that inject a temporary tasks base directory still work.
- Roadmap-overview dependency fallback remains identical for task mutation and UI state building.
- Todo task lookup returns the same task records expected by edit, delete, restore, and ignore flows.

### Regression Scenarios

- Importing output helper modules no longer changes behavior based on load order.
- Runtime/UI scripts that use `Write-Status -Type ...` continue to work with the intended implementation.
- Stack-local dev hooks still emit readable status output after any rename or consolidation.
- Session-targeted steering and instance-targeted UI whispers still write the expected whisper files.
- Long task names do not create mismatched task references or invalid worktree branch names after any slug decision.

### Test Execution

- Run install/update cycle from repo root:
  - `pwsh install.ps1`
- Run layers 1-3:
  - `pwsh tests/Run-Tests.ps1`
- If iteration is needed, run the smallest targeted test file first, then rerun the full suite at the end.

## Serena Tool Guidance

The Serena MCP server is available for this project and changes the risk profile and execution approach for several steps.

### Tool-to-step mapping

- `find_referencing_symbols` — use before every step to generate a precise LSP-backed call-site map. Replaces grep-based discovery and eliminates the risk of missing a call site.
- `safe_delete_symbol` — use in Step 1 before removing each embedded `Send-McpRequest` copy. Returns a reference list if any callers exist, preventing silent breakage.
- `replace_content` (regex mode) — use in Step 1 to bulk-remove the embedded function definitions from the 15 tool `test.ps1` files in a single targeted pass.
- `replace_symbol_body` — use in Step 2 to replace duplicate function bodies with delegation calls once the shared module is in place.
- `rename_symbol` — use in Steps 4 and 5. Performs a codebase-wide rename including all call sites. This is the primary reason those steps have been re-rated from high to medium risk.
- `get_symbols_overview` — use when exploring a module before editing to avoid reading the full file.

## Recommended Execution Approach

- Steps 4 and 5 (renames) can now run **before** the shared-module work in Step 2, not after, because `rename_symbol` removes the manual call-site hunting that made them high-risk. Suggested revised order:
  1. Step 1: `Send-McpRequest` consolidation into `dotbot-mcp-helpers.ps1`
  2. Step 4: `Write-Status` rename (use `rename_symbol`; no logic change)
  3. Step 5: `Send-Whisper` rename (use `rename_symbol`; no logic change)
  4. Step 2: Introduce shared task helper module
  5. Step 3: Update task-domain tests
  6. Step 6: Resolve slug policy
- Only Step 6 remains deferred because it still requires a product decision before code can be written.

## Open Decisions For Implementation

- Should slug generation be canonical across task lookup aliases and worktree branch names?
- Should `DotBotTheme.psm1` own the only cross-runtime `Write-Status`, with other layers renamed away from that symbol?
- Should the two whisper functions be renamed now, or should a lower-level shared helper be introduced first and the public rename happen in the same change?