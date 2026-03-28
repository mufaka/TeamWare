# TeamWare - Copilot Agent Implementation Plan

This document defines the phased implementation plan for the TeamWare Copilot Agent based on the [Copilot Agent Specification](CopilotAgentSpecification.md). Each phase builds on the previous one and is broken into work items suitable for GitHub Issues. Check off items as they are completed to track progress.

---

## Progress Summary

| Phase | Description | Status |
|-------|------------|--------|
| 36 | TeamWare Prerequisites (Blocked/Error Statuses) | ✅ Complete |
| 37 | Agent Project Scaffold and Configuration | Not Started |
| 38 | Polling Loop and Task Discovery | Not Started |
| 39 | Task Processing Pipeline | Not Started |
| 40 | Status Transitions and Reporting | Not Started |
| 41 | Safety Guardrails and Dry Run | Not Started |
| 42 | Repository Management and Lounge Integration | Not Started |
| 43 | Agent Polish and Hardening | Not Started |

---

## Current State

All original phases (0-9), social feature phases (10-14), Project Lounge phases (15-21), Ollama AI Integration phases (22-25), MCP Server Integration phases (26-31), and Agent Users phases (32-35) are complete. The workspace is an ASP.NET Core MVC project (.NET 10) with:

- Full project and task management (CRUD, assignment, filtering, GTD workflow)
- Inbox capture/clarify workflow with Someday/Maybe list
- Progress tracking with activity log and deadline visibility
- Task commenting with notifications
- In-app notification system
- GTD review workflow
- User profile management and personal dashboard
- Site-wide admin role and admin dashboard with `GlobalConfiguration` management
- User directory with profile pages
- Real-time online/offline presence via SignalR
- Project invitation accept/decline workflow
- Project Lounge with real-time messaging, reactions, pinning, and message retention
- Ollama AI Integration with content rewriting and summary generation
- MCP Server with PAT authentication, read/write tools, prompts, resources, and lounge integration
- Agent Users with data model, admin management UI, `get_my_profile` MCP tool, `my_assignments` filtering, and bot badge rendering
- Security hardening, performance optimization, and UI polish

The Copilot Agent is a **separate .NET console application** (`TeamWare.Agent`) that lives alongside the existing `TeamWare.Web` project. Phase 36 handles prerequisite changes to `TeamWare.Web` (new task statuses). Phases 37-43 build the agent process itself.

---

## Guiding Principles

All guiding principles from previous implementation plans continue to apply for TeamWare.Web changes:

1. **Vertical slices** — Each phase delivers end-to-end working functionality (model, service, controller/tools, view, tests).
2. **Tests accompany every feature** — No phase is complete without its test cases.
3. **One type per file** — Enforced throughout (MAINT-01).
4. **MVC only** — Controllers and Views, no Razor Pages (project guideline).
5. **Reuse, do not duplicate** — All existing services work without modification.

Additionally, for the `TeamWare.Agent` project:

6. **Separate process** — The agent is a standalone .NET 10 console application. It does not share code, database, or process space with TeamWare.Web. All interaction is via the MCP endpoint.
7. **System prompt is the primary control** — Agent behavior is governed by the system prompt, not by SDK-level tool restrictions.
8. **Dry run first** — Initial development and testing use dry run mode exclusively. Write capabilities are enabled only after dry run validation.
9. **One task at a time** — Each agent identity processes one task to completion (or deferral) before picking up the next.
10. **Fail safely** — Errors move tasks to Error status. Unclear tasks move to Blocked status. The agent never retries.

---

## Phase 36: TeamWare Prerequisites (Blocked/Error Statuses)

Add the `Blocked` and `Error` task statuses to TeamWare.Web. These are required by the Copilot Agent for its status transition workflow but are also useful for human users. This phase modifies only TeamWare.Web — the agent project does not exist yet.

### 36.1 TaskItemStatus Enum Changes

- [x] Add `Blocked = 4` to `TaskItemStatus` enum in `TeamWare.Web/Models/TaskItemStatus.cs` (Spec CA-63, Section 9.1)
- [x] Add `Error = 5` to `TaskItemStatus` enum in `TeamWare.Web/Models/TaskItemStatus.cs` (Spec CA-64, Section 9.1)
- [x] Create EF Core migration if needed (enum values stored as integers; verify no schema change is required since EF stores the int value)
- [x] Write unit tests verifying the enum values: `Blocked = 4`, `Error = 5`

### 36.2 Task Views and Filtering

- [x] Update task list view (`Views/Task/Index.cshtml`) to display `Blocked` and `Error` statuses with appropriate styling:
  - [x] `Blocked` — yellow/amber badge (e.g., `bg-amber-100 text-amber-700 dark:bg-amber-900 dark:text-amber-300`)
  - [x] `Error` — red badge (e.g., `bg-red-100 text-red-700 dark:bg-red-900 dark:text-red-300`)
- [x] Update task detail view (`Views/Task/Details.cshtml`) to display `Blocked` and `Error` statuses
- [x] Update status filter dropdowns to include `Blocked` and `Error` options
- [x] Update any status change controls (dropdowns, buttons) in the task detail view to include `Blocked` and `Error` as valid target statuses
- [x] Update the project dashboard view to include `Blocked` and `Error` in task count summaries
- [x] Write tests verifying new statuses render correctly in all views

### 36.3 Service and MCP Tool Updates

- [x] Verify `ITaskService.ChangeStatus` handles transitions to and from `Blocked` and `Error` (the service likely uses the enum directly, so no code change may be needed — verify and add tests)
- [x] Verify MCP `update_task_status` tool correctly parses `"Blocked"` and `"Error"` string values to the enum (add tests if the parsing uses `Enum.TryParse`, which should handle new values automatically)
- [x] Verify MCP `list_tasks` status filter correctly handles `"Blocked"` and `"Error"` filter values
- [x] Verify MCP `get_task` returns the new status values in its JSON response
- [x] Verify `my_assignments` for agent users (which filters to `ToDo` and `InProgress`) correctly excludes `Blocked` and `Error` tasks
- [x] Update activity log formatting to display `Blocked` and `Error` status names in activity entries
- [x] Write integration tests verifying end-to-end status transitions involving `Blocked` and `Error` through both the web UI and MCP tools

### 36.4 Progress Tracking Updates

- [x] Update `IProgressService.GetProjectStatistics` to include `Blocked` and `Error` counts in the project statistics (or verify it already handles unknown statuses gracefully)
- [x] Update the project dashboard progress display (if it shows per-status breakdowns) to include `Blocked` and `Error`
- [x] Write tests verifying statistics correctly count tasks in `Blocked` and `Error` statuses

---

## Phase 37: Agent Project Scaffold and Configuration

Create the `TeamWare.Agent` console application project with the configuration model, host builder, and basic project structure. This phase delivers a running process that loads configuration and logs startup — but does not yet connect to TeamWare or the Copilot SDK.

### 37.1 Project Creation

- [ ] Create `TeamWare.Agent` .NET 10 console application project
- [ ] Add project to the TeamWare solution (`TeamWare.sln`)
- [ ] Add NuGet package references:
  - [ ] `GitHub.Copilot.SDK` (Copilot SDK)
  - [ ] `Microsoft.Extensions.Hosting` (Generic Host for IHostedService)
  - [ ] `Microsoft.Extensions.Configuration.Json` (appsettings.json)
  - [ ] `Microsoft.Extensions.Configuration.EnvironmentVariables`
  - [ ] `Microsoft.Extensions.Logging.Console` (structured logging)
  - [ ] `Serilog.Extensions.Hosting` (optional, if Serilog is preferred — match TeamWare.Web's logging approach)
- [ ] Create `Program.cs` with `Host.CreateDefaultBuilder` pattern:
  - [ ] Configure logging (structured, console output)
  - [ ] Load configuration from `appsettings.json` and environment variables (CA-14)
  - [ ] Register hosted service (placeholder for `AgentHostedService`)
  - [ ] Build and run the host
- [ ] Create `appsettings.json` with the `Agents` array configuration structure (Spec Section 7.1)
- [ ] Verify the project builds and runs (exits cleanly with no agents configured)

### 37.2 Configuration Model

- [ ] Create `TeamWare.Agent/Configuration/AgentIdentityOptions.cs` — strongly-typed options class (Spec Section 7.2):
  - [ ] `Name` (string, required)
  - [ ] `WorkingDirectory` (string, required)
  - [ ] `RepositoryUrl` (string?, optional)
  - [ ] `RepositoryBranch` (string?, optional)
  - [ ] `RepositoryAccessToken` (string?, optional)
  - [ ] `PersonalAccessToken` (string, required)
  - [ ] `PollingIntervalSeconds` (int, default 60)
  - [ ] `Model` (string?, optional)
  - [ ] `AutoApproveTools` (bool, default true)
  - [ ] `DryRun` (bool, default false)
  - [ ] `SystemPrompt` (string?, optional)
  - [ ] `McpServers` (list of `McpServerOptions`, required)
- [ ] Create `TeamWare.Agent/Configuration/McpServerOptions.cs` — strongly-typed options class (Spec Section 7.3):
  - [ ] `Name` (string, required)
  - [ ] `Type` (string, required — `http` or `stdio`)
  - [ ] `Url` (string?, required for http)
  - [ ] `AuthHeader` (string?, optional)
- [ ] Bind configuration in `Program.cs` using `IOptions<List<AgentIdentityOptions>>` or similar pattern
- [ ] Write unit tests verifying:
  - [ ] Configuration loads correctly from JSON
  - [ ] Required field validation (Name, WorkingDirectory, PersonalAccessToken, McpServers)
  - [ ] Default values applied (PollingIntervalSeconds = 60, AutoApproveTools = true, DryRun = false)
  - [ ] Multiple agent identities load correctly

### 37.3 Agent Hosted Service

- [ ] Create `TeamWare.Agent/AgentHostedService.cs` implementing `IHostedService` (CA-01):
  - [ ] In `StartAsync`: read the `Agents` configuration array, log the number of configured identities, start a polling loop per identity (as `Task.Run` with `CancellationToken`)
  - [ ] In `StopAsync`: signal cancellation to all polling loops, await completion (CA-02)
  - [ ] Log startup and shutdown events (CA-NF-02)
- [ ] Register `AgentHostedService` in DI in `Program.cs`
- [ ] For this phase, the polling loop is a placeholder that logs "Polling cycle for {Name}" and waits `PollingIntervalSeconds`
- [ ] Write unit tests verifying:
  - [ ] Service starts and stops cleanly
  - [ ] One loop is started per configured identity (CA-20)
  - [ ] Graceful shutdown cancels all loops (CA-02)
  - [ ] No agents configured results in a clean startup with a warning log

### 37.4 Test Project Creation

- [ ] Create `TeamWare.Agent.Tests` xUnit test project
- [ ] Add project to the TeamWare solution
- [ ] Add project reference to `TeamWare.Agent`
- [ ] Add NuGet package references: `xUnit`, `Moq` (or NSubstitute — match TeamWare.Web.Tests conventions), `FluentAssertions` (if used in existing tests), `Microsoft.Extensions.Configuration.Json`
- [ ] Verify all tests from 37.2 and 37.3 pass

---

## Phase 38: Polling Loop and Task Discovery

Connect the polling loop to TeamWare's MCP endpoint. The agent authenticates, checks its active status via `get_my_profile`, and discovers assigned tasks via `my_assignments`. This phase does not yet process tasks — it discovers them and logs what it finds.

### 38.1 Agent Polling Loop

- [ ] Create `TeamWare.Agent/Pipeline/AgentPollingLoop.cs` (CA-30 through CA-36):
  - [ ] Accept an `AgentIdentityOptions` instance and `CancellationToken`
  - [ ] Run a loop: each cycle calls `get_my_profile`, checks active status, calls `my_assignments` if active, then waits `PollingIntervalSeconds`
  - [ ] If `get_my_profile` returns `isAgentActive = false`, log a message and skip to the next cycle (CA-31)
  - [ ] Filter `my_assignments` results to tasks with status `ToDo` only (CA-33)
  - [ ] Log discovered tasks: count, IDs, titles
  - [ ] For this phase, do not process tasks — just log "Would process task #{id}: {title}"
  - [ ] Constant polling interval — no backoff or acceleration (CA-36)
- [ ] Define an interface or abstraction for MCP tool invocation (to allow mocking):
  - [ ] `ITeamWareMcpClient` (or similar) with methods: `GetMyProfileAsync()`, `GetMyAssignmentsAsync()`, `GetTaskAsync(int taskId)`, `UpdateTaskStatusAsync(int taskId, string status)`, `AddCommentAsync(int taskId, string content)`, `PostLoungeMessageAsync(int? projectId, string content)`
  - [ ] HTTP-based implementation that calls the TeamWare MCP endpoint using the PAT from configuration
- [ ] Update `AgentHostedService` to create an `AgentPollingLoop` per identity instead of the placeholder loop
- [ ] Write unit tests verifying (CA-TEST-01, CA-TEST-02):
  - [ ] Polling loop calls `get_my_profile` at the start of each cycle
  - [ ] Loop skips processing when `isAgentActive = false`
  - [ ] Loop calls `my_assignments` when active
  - [ ] Loop filters for `ToDo` tasks only
  - [ ] Loop waits the correct interval between cycles
  - [ ] Loop handles cancellation gracefully

### 38.2 Infrastructure Error Handling

- [ ] Implement error handling in `AgentPollingLoop` for MCP connectivity issues (CA-150):
  - [ ] If `get_my_profile` or `my_assignments` throws a network/HTTP error, log the error and wait for the next cycle
  - [ ] Do not crash the polling loop on transient errors
  - [ ] Do not crash other agent identities if one identity's loop fails
- [ ] Write unit tests verifying (CA-TEST-09):
  - [ ] Network errors during profile check are logged and the loop continues
  - [ ] Network errors during task discovery are logged and the loop continues
  - [ ] One identity's error does not affect other identities

### 38.3 MCP Client Integration Tests

- [ ] Write integration tests verifying end-to-end connectivity (CA-TEST-20):
  - [ ] Agent identity authenticates via PAT to a running TeamWare instance
  - [ ] `get_my_profile` returns the correct agent identity
  - [ ] `my_assignments` returns tasks assigned to the agent
  - [ ] Invalid PAT is rejected
  - [ ] Paused agent (`IsActive = false`) is rejected at the MCP level (CA-TEST-22)

---

## Phase 39: Task Processing Pipeline

Integrate the GitHub Copilot SDK to create LLM sessions for each task. The agent picks up a task, creates a session with the task context, and lets the LLM reason and act using all available tools. This is the core intelligence layer.

### 39.1 Copilot SDK Integration

- [ ] Create `TeamWare.Agent/Pipeline/TaskProcessor.cs` (CA-40 through CA-45):
  - [ ] Accept an `AgentIdentityOptions`, a task object (from `my_assignments`), and `CancellationToken`
  - [ ] Create a `CopilotClient` with `Cwd` set to the identity's `WorkingDirectory` (CA-44)
  - [ ] Create a session via `client.CreateSessionAsync()` with:
    - [ ] Model from identity config (CA-41)
    - [ ] System prompt appended via `SystemMessageMode.Append` (CA-41)
    - [ ] MCP servers from identity config (CA-41)
    - [ ] Permission handler: `PermissionHandler.ApproveAll` if `AutoApproveTools` is true, custom handler otherwise (CA-130, CA-131)
  - [ ] Construct the task prompt with: task ID, title, description, priority, status, project name, existing comments (CA-42)
  - [ ] Call `session.SendAndWaitAsync()` with the task prompt (CA-42)
  - [ ] Dispose of the session and client after processing
- [ ] Write unit tests verifying (CA-TEST-03, CA-TEST-04):
  - [ ] Session is created with correct model selection
  - [ ] Session is created with correct system prompt
  - [ ] Session is created with correct MCP server configuration
  - [ ] Task prompt includes all required fields
  - [ ] Session and client are properly disposed

### 39.2 Default System Prompt

- [ ] Create a constant or resource file containing the default system prompt (Spec Section 3.9, CA-82, CA-83):
  - [ ] Include all 8 steps (read, assess scope, explore, change, build/test, commit, comment, update status)
  - [ ] Include all rules (no Done, no create/delete, no reassign, no delete comments, comment-before-status, feature branch naming, Blocked for unclear/too-large)
- [ ] If the identity's `SystemPrompt` config is null or empty, use the default
- [ ] If the identity's `SystemPrompt` config is provided, use it instead
- [ ] Write unit tests verifying default prompt is used when config is empty, and custom prompt is used when provided (CA-81)

### 39.3 Pipeline Integration

- [ ] Update `AgentPollingLoop` to call `TaskProcessor` for each discovered `ToDo` task instead of logging a placeholder
- [ ] Process tasks one at a time in order (CA-21, CA-34)
- [ ] Wrap `TaskProcessor` calls in try/catch — errors are handled in Phase 40
- [ ] Write unit tests verifying:
  - [ ] Tasks are processed sequentially, one at a time
  - [ ] Processing one task completes before the next begins
  - [ ] An exception in one task does not prevent processing subsequent tasks

### 39.4 Copilot CLI Error Handling

- [ ] Implement error handling for Copilot SDK failures (CA-151, CA-152):
  - [ ] If `CopilotClient` fails to create, log the error and skip to the next polling cycle
  - [ ] If `CreateSessionAsync` fails, log the error and treat as a task-level error
  - [ ] If `SendAndWaitAsync` fails (LLM timeout, rate limit, etc.), log the error and treat as a task-level error
- [ ] Write unit tests verifying:
  - [ ] Client creation failure is logged and does not crash the loop
  - [ ] Session creation failure is logged and the task is skipped
  - [ ] LLM provider errors are handled as task-level errors

---

## Phase 40: Status Transitions and Reporting

Implement the complete status transition lifecycle: ToDo → InProgress → InReview/Error/Blocked. Add comment posting before every status change and lounge notifications for Error and Blocked transitions.

### 40.1 Status Transition Handler

- [ ] Create `TeamWare.Agent/Pipeline/StatusTransitionHandler.cs` (CA-60 through CA-66):
  - [ ] `PickUpTask(int taskId)` — post a comment ("Starting work on this task"), change status to `InProgress` (CA-60, CA-65)
  - [ ] `CompleteTask(int taskId, string summary)` — post a summary comment, change status to `InReview` (CA-61, CA-65, CA-70)
  - [ ] `BlockTask(int taskId, string reason, string projectName)` — post a comment explaining the block, change status to `Blocked`, post lounge message (CA-63, CA-65, CA-71, CA-73)
  - [ ] `ErrorTask(int taskId, string errorDetails, string projectName)` — post an error comment, change status to `Error`, post lounge message (CA-64, CA-65, CA-72, CA-74)
  - [ ] All methods use the `ITeamWareMcpClient` abstraction from Phase 38
- [ ] Write unit tests verifying (CA-TEST-05, CA-TEST-06, CA-TEST-07):
  - [ ] A comment is posted before every status change
  - [ ] Correct status transitions: ToDo → InProgress, InProgress → InReview, InProgress → Error, InProgress → Blocked
  - [ ] Lounge messages are posted only for Blocked and Error, targeting the project lounge
  - [ ] Lounge messages use the correct format (CA-176, CA-177)
  - [ ] No lounge message is posted for InReview transitions (CA-77)

### 40.2 Lounge Message Formatting

- [ ] Implement lounge message formatting per the specification (CA-175 through CA-178):
  - [ ] Blocked: `"I need help with Task #{id} — {title}. I've posted a comment explaining what information I need. Can someone take a look?"`
  - [ ] Error: `"I ran into a problem on Task #{id} — {title}. I've posted a comment with the error details. Someone will need to triage this."`
  - [ ] Plain text only — no icons, emoticons, or decorative formatting (CA-175)
  - [ ] Target the project lounge, not the global lounge (CA-178)
- [ ] Write unit tests verifying message format for both Blocked and Error cases

### 40.3 Pipeline Integration

- [ ] Update `AgentPollingLoop` to call `StatusTransitionHandler.PickUpTask` before passing the task to `TaskProcessor`
- [ ] Update `TaskProcessor` to call `StatusTransitionHandler.CompleteTask` after successful session completion
- [ ] Add try/catch around `TaskProcessor` execution:
  - [ ] On exception, call `StatusTransitionHandler.ErrorTask` with the error details (CA-140, CA-141, CA-142)
- [ ] Implement read-before-write: call `get_task` before any status change to verify current state (CA-100)
- [ ] Implement idempotency: skip tasks not in `ToDo` status (CA-NF-06, CA-TEST-10)
- [ ] Write unit tests verifying:
  - [ ] Task pickup posts comment and changes status before processing
  - [ ] Successful processing posts summary and changes to InReview
  - [ ] Processing exceptions result in Error status with error comment and lounge message
  - [ ] Tasks not in ToDo status are skipped

---

## Phase 41: Safety Guardrails and Dry Run

Implement dry run mode, the custom permission handler, and verify all safety guardrails are enforced.

### 41.1 Dry Run Mode

- [ ] Create `TeamWare.Agent/Logging/DryRunLogger.cs` (CA-120 through CA-123):
  - [ ] Intercept write tool calls and log them instead of executing
  - [ ] Log tool name, parameters, and the LLM's reasoning
  - [ ] Allow read tools to execute normally
- [ ] Implement dry run mode in `TaskProcessor`:
  - [ ] When `DryRun = true`, configure the session or permission handler to block all write operations (CA-121)
  - [ ] Log all would-be tool calls via `DryRunLogger` (CA-122)
  - [ ] The full pipeline still runs: polling, task discovery, session creation, LLM reasoning — only writes are blocked
- [ ] Write unit tests verifying (CA-TEST-08):
  - [ ] Dry run mode prevents all write tool invocations
  - [ ] Read/reasoning pipeline still executes
  - [ ] Tool calls are logged with name, parameters, and reasoning
  - [ ] Dry run mode is configurable per identity

### 41.2 Custom Permission Handler

- [ ] Create `TeamWare.Agent/Permissions/AgentPermissionHandler.cs` (CA-131):
  - [ ] Implement the SDK's permission handler callback interface
  - [ ] Inspect each tool call before approving
  - [ ] Optionally block dangerous operations (e.g., `rm -rf`, `git push --force`, commits to main/master) (CA-SEC-06)
  - [ ] Log approved and denied tool calls
- [ ] Wire up the permission handler: use `PermissionHandler.ApproveAll` when `AutoApproveTools = true`, use `AgentPermissionHandler` when `false` (CA-130)
- [ ] Write unit tests verifying:
  - [ ] `AutoApproveTools = true` uses `PermissionHandler.ApproveAll`
  - [ ] `AutoApproveTools = false` uses the custom handler
  - [ ] Custom handler logs decisions
  - [ ] Permission handler is independent of dry run mode (CA-132)

### 41.3 Action Restriction Verification

- [ ] Verify all action restrictions from the specification are enforced by the system prompt and/or the MCP tool layer (CA-90 through CA-94):
  - [ ] Agent cannot create tasks (system prompt rule, not enforced at SDK level)
  - [ ] Agent cannot delete tasks (no delete tool exists)
  - [ ] Agent cannot delete comments (no delete tool exists)
  - [ ] Agent cannot reassign tasks (system prompt rule)
  - [ ] Agent cannot set status to Done (system prompt rule)
- [ ] Write integration tests verifying the system prompt is correctly included in the session
- [ ] Document that action restrictions beyond the system prompt (e.g., hard-blocking `create_task` calls) are the responsibility of the `OnPermissionRequest` handler when `AutoApproveTools = false`

---

## Phase 42: Repository Management and Lounge Integration

Implement automatic repository clone/pull before task processing and verify end-to-end lounge integration.

### 42.1 Repository Manager

- [ ] Create `TeamWare.Agent/Repository/RepositoryManager.cs` (CA-50 through CA-54):
  - [ ] `EnsureRepository(AgentIdentityOptions options)` — called before each task:
    - [ ] If `RepositoryUrl` is null, do nothing (CA-53)
    - [ ] If `WorkingDirectory` does not contain a `.git` directory, clone the repository (CA-50)
    - [ ] If `WorkingDirectory` contains a `.git` directory, pull latest from `RepositoryBranch` (CA-51)
    - [ ] If `RepositoryAccessToken` is configured, use it for authentication (CA-52)
  - [ ] Use `git` CLI commands via `Process.Start` (not the Copilot CLI — this runs before the session is created)
  - [ ] Log clone/pull operations and any errors
- [ ] Update `AgentPollingLoop` to call `RepositoryManager.EnsureRepository` before each task (after pickup, before session creation)
- [ ] Write unit tests verifying:
  - [ ] No-op when `RepositoryUrl` is null
  - [ ] Clone is performed when directory has no `.git`
  - [ ] Pull is performed when directory has `.git`
  - [ ] Access token is used when configured
  - [ ] Clone/pull errors are logged and treated as task-level errors

### 42.2 End-to-End Lounge Integration Tests

- [ ] Write integration tests verifying the complete lounge notification workflow:
  - [ ] Agent moves task to Blocked → comment posted, lounge message posted to project lounge
  - [ ] Agent moves task to Error → comment posted, lounge message posted to project lounge
  - [ ] Agent moves task to InReview → comment posted, no lounge message
  - [ ] Lounge messages are plain text with no formatting (CA-175)
  - [ ] Lounge messages target the project lounge, not global (CA-178)

### 42.3 Multiple Identity Integration Tests

- [ ] Write integration tests verifying concurrent identity execution (CA-TEST-23):
  - [ ] Two agent identities configured in the same process
  - [ ] Each polls independently and processes tasks from different projects
  - [ ] Identities do not share state (CA-22)
  - [ ] One identity's error does not affect the other

---

## Phase 43: Agent Polish and Hardening

Final review, edge case handling, security hardening, and documentation. Ensure the agent operates correctly across all supported configurations and failure modes.

### 43.1 Security Hardening

- [ ] Verify PATs are never logged, committed to source control, or included in error messages (CA-SEC-02):
  - [ ] Audit all logging statements for token leakage
  - [ ] Ensure configuration loading does not log sensitive values
  - [ ] Write tests verifying PATs are redacted in logs
- [ ] Verify `RepositoryAccessToken` follows the same security practices (CA-SEC-03)
- [ ] Verify the kill switch works at both levels (CA-SEC-07):
  - [ ] Client-side: `get_my_profile` check causes identity to skip the cycle
  - [ ] Server-side: `PatAuthenticationHandler` rejects paused agents
  - [ ] Write integration test: agent is active, processes a task, is paused mid-cycle, next poll is skipped
- [ ] Verify the agent is subject to project membership authorization (CA-SEC-05):
  - [ ] Write integration test: agent attempts to access a project it is not a member of via MCP tools

### 43.2 Edge Cases and Regression Testing

- [ ] Verify idempotency: re-running the agent against the same task list produces no side effects (CA-NF-06):
  - [ ] Task already in InProgress — skipped
  - [ ] Task already in InReview — skipped
  - [ ] Task already in Blocked — skipped
  - [ ] Task already in Error — skipped
  - [ ] Task already in Done — skipped
- [ ] Verify graceful shutdown during task processing (CA-02):
  - [ ] SIGTERM during a session — agent finishes current task or posts Error comment and exits
  - [ ] SIGTERM during polling wait — agent exits immediately
- [ ] Verify zero-task polling cycles:
  - [ ] No tasks available — agent logs and waits, no errors
  - [ ] All tasks in non-ToDo statuses — agent logs and waits, no errors
- [ ] Verify configuration edge cases:
  - [ ] Zero agents configured — process starts and logs a warning
  - [ ] Agent with invalid PAT — authentication fails, logged, polling continues
  - [ ] Agent with unreachable MCP endpoint — network error logged, polling continues
  - [ ] Agent with nonexistent WorkingDirectory — error logged at startup
- [ ] Verify multiple agent identities do not interfere:
  - [ ] Different working directories
  - [ ] Different PATs
  - [ ] Different polling intervals
  - [ ] One identity fails, others continue

### 43.3 Logging Review

- [ ] Verify structured logging is used throughout (CA-NF-02):
  - [ ] Polling cycle start/end with identity name
  - [ ] Task pickup with task ID and title
  - [ ] Status transitions with task ID, old status, new status
  - [ ] Tool invocations (in dry run mode, all calls; in normal mode, significant events)
  - [ ] Errors with full exception details
  - [ ] Startup and shutdown events
- [ ] Verify log levels are appropriate:
  - [ ] `Information` for normal operations (polling, task pickup, status changes)
  - [ ] `Warning` for recoverable issues (network errors, paused agent)
  - [ ] `Error` for failures (task errors, CLI crashes)
  - [ ] `Debug` for detailed tool call logging

### 43.4 Documentation

- [ ] Update the [copilot-instructions.md](../../.github/copilot-instructions.md) with:
  - [ ] Phase 36-43 branch names in the Branch Strategy table
  - [ ] Phase 36-43 GitHub issue mappings in the GitHub Issue Map section (create issues as work begins)
- [ ] Review and finalize the [Copilot Agent Idea document](CopilotAgentIdea.md) — mark all next steps as complete
- [ ] Review and finalize the [Copilot Agent Specification](CopilotAgentSpecification.md) — verify all requirements are implemented
- [ ] Create a `TeamWare.Agent/README.md` with:
  - [ ] Overview and purpose
  - [ ] Prerequisites (GitHub Copilot subscription or BYOK, git, .NET 10)
  - [ ] Configuration reference (all fields with descriptions and defaults)
  - [ ] Quick start guide (create agent user in TeamWare, configure PAT, configure appsettings.json, run)
  - [ ] Dry run mode instructions
  - [ ] Deployment options (terminal, systemd, Docker)
  - [ ] Troubleshooting common issues (authentication failures, network errors, Copilot CLI not found)
- [ ] Create a sample `appsettings.example.json` in `TeamWare.Agent/` with commented configuration showing all options
