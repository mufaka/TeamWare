# Copilot Instructions

## Project Guidelines
- TeamWare is an ASP.NET Core MVC application. It should not use Razor Pages. Use Controllers and Views instead.
- One type per file (MAINT-01).
- Tests accompany every feature. No phase is complete without its test cases.
- Follow the implementation plan in `TeamWare.Web/Specifications/ImplementationPlan.md`, `TeamWare.Web/Specifications/SocialFeaturesImplementationPlan.md`, `TeamWare.Web/Specifications/ProjectLoungeImplementationPlan.md`, `TeamWare.Web/Specifications/OllamaIntegrationImplementationPlan.md`, `TeamWare.Web/Specifications/McpServerImplementationPlan.md`, `TeamWare.Web/Specifications/AgentUsersImplementationPlan.md`, `TeamWare.Web/Specifications/CopilotAgentImplementationPlan.md`, `TeamWare.Web/Specifications/ServerSideAgentConfigImplementationPlan.md`, `TeamWare.Web/Specifications/RealtimeAgentActivityImplementationPlan.md`, and `TeamWare.Web/Specifications/EditMcpAndRepositoryImplementationPlan.md`.
- In `_DashboardProjects.cshtml`, ensure the link to project details is correctly implemented using `asp-controller="Project" asp-action="Details" asp-route-id="@project.Id"`.

## Idea Documents
- Idea documents (e.g., files named *Idea.md in TeamWare.Web/Specifications/) are collaborative, human-in-the-loop documents. They present multiple approaches neutrally, pose open questions for the human to answer, and do not make decisions or declare a preferred direction. Decisions belong in specification documents, not idea documents.

---

## Developer Workflow

### Branch Strategy

Each phase in the implementation plan gets its own branch created from `master`. Branch naming convention:

| Phase | Branch Name |
|-------|------------|
| Phase 0 | `phase-0/foundation` |
| Phase 1 | `phase-1/project-management` |
| Phase 2 | `phase-2/task-management` |
| Phase 3 | `phase-3/inbox-gtd` |
| Phase 4 | `phase-4/progress-tracking` |
| Phase 5 | `phase-5/comments` |
| Phase 6 | `phase-6/notifications` |
| Phase 7 | `phase-7/review-workflow` |
| Phase 8 | `phase-8/user-profile-dashboard` |
| Phase 9 | `phase-9/polish-hardening` |
| Phase 10 | `phase-10/system-administration` |
| Phase 11 | `phase-11/user-directory` |
| Phase 12 | `phase-12/activity-presence` |
| Phase 13 | `phase-13/invitation-improvements` |
| Phase 14 | `phase-14/social-polish` |
| Phase 15 | `phase-15/lounge-data-layer` |
| Phase 16 | `phase-16/lounge-service-layer` |
| Phase 17 | `phase-17/lounge-signalr-hub` |
| Phase 18 | `phase-18/lounge-controllers-views` |
| Phase 19 | `phase-19/lounge-notifications-mentions` |
| Phase 20 | `phase-20/lounge-background-jobs` |
| Phase 21 | `phase-21/lounge-polish-hardening` |
| Phase 22 | `phase-22/ai-foundation` |
| Phase 23 | `phase-23/ai-content-rewriting` |
| Phase 24 | `phase-24/ai-summary-generation` |
| Phase 25 | `phase-25/ai-polish-hardening` |
| Phase 26 | `phase-26/mcp-foundation` |
| Phase 27 | `phase-27/mcp-read-tools` |
| Phase 28 | `phase-28/mcp-write-tools` |
| Phase 29 | `phase-29/mcp-prompts-resources` |
| Phase 30 | `phase-30/mcp-lounge-tools` |
| Phase 31 | `phase-31/mcp-polish-hardening` |
| Phase 32 | `phase-32/agent-data-model` |
| Phase 33 | `phase-33/agent-management-ui` |
| Phase 34 | `phase-34/agent-mcp-bot-badge` |
| Phase 35 | `phase-35/agent-polish-hardening` |
| Phase 36 | `phase-36/agent-prerequisites` |
| Phase 37 | `phase-37/agent-scaffold` |
| Phase 38 | `phase-38/agent-polling` |
| Phase 39 | `phase-39/agent-pipeline` |
| Phase 40 | `phase-40/agent-status-transitions` |
| Phase 41 | `phase-41/agent-guardrails` |
| Phase 42 | `phase-42/agent-repository` |
| Phase 43 | `phase-43/agent-polish` |
| Phase 44 | `phase-44/server-config-data-model` |
| Phase 45 | `phase-45/server-config-admin-ui` |
| Phase 46 | `phase-46/server-config-mcp-response` |
| Phase 47 | `phase-47/server-config-agent-merge` |
| Phase 48 | `phase-48/server-config-polish` |
| Phase 49 | `phase-49/realtime-task-hub` |
| Phase 50 | `phase-50/realtime-task-client` |
| Phase 51 | `phase-51/edit-repo-mcp-inline` |

When starting a phase:
1. Create the phase branch from `master` in GitHub.
2. All work items within that phase are committed to the phase branch.
3. When the phase is complete, create a pull request to merge the phase branch into `master`.

### Issue Tracking

Each work item in the implementation plan has a corresponding GitHub issue. Before starting any work item:
1. **Verify** that a matching GitHub issue exists and its description matches the current plan.
2. **Create** a new issue if one does not exist, using the format: `Phase X.Y: <Work Item Title>` with the appropriate phase label.
3. Close duplicate issues if they exist (see Duplicate Issues section below).

### Commit Messages

All commits must reference the GitHub issue being worked on. Format:

```
<short description of change>

Refs #<issue-number>
```

When a commit completes all work items in an issue, use `Closes` instead:

```
<short description of change>

Closes #<issue-number>
```

Examples:
```
Create ApplicationDbContext and ApplicationUser entity

Refs #2
```

```
Add integration tests for DbContext creation and migration

Closes #2
```

---

## GitHub Issue Map

The following maps implementation plan work items to their canonical GitHub issues. Use the **Canonical Issue** when committing code.

> **Phases 0-43 (Complete):** All issue mappings for Phases 0-43 have been archived. These phases are fully merged to `master`. For historical reference, see the closed issues in GitHub or the individual implementation plans:
> - Phases 0-9: `TeamWare.Web/Specifications/ImplementationPlan.md`
> - Phases 10-14: `TeamWare.Web/Specifications/SocialFeaturesImplementationPlan.md`
> - Phases 15-21: `TeamWare.Web/Specifications/ProjectLoungeImplementationPlan.md`
> - Phases 22-25: `TeamWare.Web/Specifications/OllamaIntegrationImplementationPlan.md`
> - Phases 26-31: `TeamWare.Web/Specifications/McpServerImplementationPlan.md`
> - Phases 32-35: `TeamWare.Web/Specifications/AgentUsersImplementationPlan.md`
> - Phases 36-43: `TeamWare.Web/Specifications/CopilotAgentImplementationPlan.md`
> - Phases 44-48: `TeamWare.Web/Specifications/ServerSideAgentConfigImplementationPlan.md`
> - Phases 49-50: `TeamWare.Web/Specifications/RealtimeAgentActivityImplementationPlan.md`

### Phase 44

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|------------------|
| 44.1 Entity Creation and Migration | #253 | — |
| 44.2 Encryption Helper Service | #254 | — |
| 44.3 Agent Configuration Service | #255 | — |

### Phase 45: Agent Configuration Admin UI (label: `Phase 45: Server-Side Agent Config Admin UI`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|------------------|
| 45.1 Edit Agent — Configuration Section | #256 | — |
| 45.2 Edit Agent — Repositories Section | #257 | — |
| 45.3 Edit Agent — MCP Servers Section | #258 | — |
| 45.4 Agent Detail Page Updates | #259 | — |

### Phase 46: MCP Profile Configuration Response (label: `Phase 46: Server-Side Agent Config MCP Response`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|------------------|
| 46.1 ProfileTools Update | #260 | — |
| 46.2 Agent-Side Profile Model Update | #261 | — |

### Phase 47: Agent-Side Configuration Merge (label: `Phase 47: Server-Side Agent Config Merge`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|------------------|
| 47.1 Configuration Merge Logic | #262 | — |
| 47.2 Polling Loop Integration | #263 | — |
| 47.3 Documentation Updates | #264 | — |

### Phase 48: Server-Side Config Polish and Hardening (label: `Phase 48: Server-Side Agent Config Polish`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|------------------|
| 48.1 Security Hardening | #265 | — |
| 48.2 Edge Cases and Regression Testing | #266 | — |
| 48.3 UI Consistency Review | #267 | — |
| 48.4 Documentation | #268 | — |

### Phase 49: Task SignalR Hub and Server Infrastructure (label: `Phase 49: Real-Time Task Hub`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|------------------|
| 49.1 TaskHub Creation | #287 | — |
| 49.2 Partial View Extraction | #288 | — |
| 49.3 Partial Endpoints | #289 | — |
| 49.4 Broadcast Integration | #290 | — |

### Phase 50: Client Integration and Polish (label: `Phase 50: Real-Time Task Client`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|------------------|
| 50.1 Client-Side SignalR Script | #291 | — |
| 50.2 Details.cshtml Integration | #292 | — |
| 50.3 Toast Notifications | #293 | — |
| 50.4 End-to-End Testing and Polish | #294 | — |

### Phase 51: Edit Agent Repositories and MCP Servers (label: `Phase 51: Edit Agent Repositories and MCP Servers`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|------------------|
| 51.1 Service Layer: Keep-Current Secret Logic | TBD | — |
| 51.2 Repository Edit UI | TBD | — |
| 51.3 MCP Server Edit UI | TBD | — |