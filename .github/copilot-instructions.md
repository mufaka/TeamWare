# Copilot Instructions

## Project Guidelines
- TeamWare is an ASP.NET Core MVC application. It should not use Razor Pages. Use Controllers and Views instead.
- One type per file (MAINT-01).
- Tests accompany every feature. No phase is complete without its test cases.
- Follow the implementation plan in `TeamWare.Web/Specifications/ImplementationPlan.md`, `TeamWare.Web/Specifications/SocialFeaturesImplementationPlan.md`, `TeamWare.Web/Specifications/ProjectLoungeImplementationPlan.md`, `TeamWare.Web/Specifications/OllamaIntegrationImplementationPlan.md`, `TeamWare.Web/Specifications/McpServerImplementationPlan.md`, `TeamWare.Web/Specifications/AgentUsersImplementationPlan.md`, and `TeamWare.Web/Specifications/CopilotAgentImplementationPlan.md`.
- In `_DashboardProjects.cshtml`, ensure the link to project details is correctly implemented using `asp-controller="Project" asp-action="Details" asp-route-id="@project.Id"`.

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

> **Phases 0-25 (Complete):** All issue mappings for Phases 0-21 have been archived. These phases are fully merged to `master`. For historical reference, see the closed issues in GitHub or the individual implementation plans:
> - Phases 0-9: `TeamWare.Web/Specifications/ImplementationPlan.md`
> - Phases 10-14: `TeamWare.Web/Specifications/SocialFeaturesImplementationPlan.md`
> - Phases 15-21: `TeamWare.Web/Specifications/ProjectLoungeImplementationPlan.md`
> - Phases 22-25: `TeamWare.Web/Specifications/OllamaIntegrationImplementationPlan.md`

### Phase 26: MCP Foundation (label: `Phase 26: MCP Foundation`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|------------------|
| 26.1 PersonalAccessToken Entity and Migration | #160 | — |
| 26.2 PersonalAccessTokenService | #163 | — |
| 26.3 PAT Authentication Handler | #161 | — |
| 26.4 GlobalConfiguration Seeding and MCP Endpoint | #162 | — |
| 26.5 PAT Management UI | #164 | — |
| 26.6 Admin PAT Management | #165 | — |

### Phase 27: Read-Only MCP Tools (label: `Phase 27: MCP Read Tools`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|------------------|
| 27.1 ProjectTools | #167 | — |
| 27.2 TaskTools (Read) | #169 | — |
| 27.3 InboxTools (Read) | #166 | — |
| 27.4 ActivityTools | #168 | — |
| 27.5 Cross-Cutting Read Tool Tests | #170 | — |

### Phase 28: Write MCP Tools (label: `Phase 28: MCP Write Tools`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|------------------|
| 28.1 TaskTools (Write) | #171 | — |
| 28.2 InboxTools (Write) | #173 | — |
| 28.3 Cross-Cutting Write Tool Tests | #172 | — |

### Phase 29: MCP Prompts and Resources (label: `Phase 29: MCP Prompts Resources`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|------------------|
| 29.1 Project Context Prompt | #178 | — |
| 29.2 Task Breakdown Prompt | #177 | — |
| 29.3 Standup Prompt | #174 | — |
| 29.4 Dashboard Resource | #179 | — |
| 29.5 Project Summary Resource | #175 | — |
| 29.6 Cross-Cutting Prompt and Resource Tests | #176 | — |

### Phase 30: Lounge MCP Tools (label: `Phase 30: MCP Lounge Tools`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|------------------|
| 30.1 LoungeTools | #186 | — |
| 30.2 Cross-Cutting Lounge Tests | #185 | — |

### Phase 31: MCP Polish and Hardening (label: `Phase 31: MCP Polish`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|------------------|
| 31.1 Error Handling and Resilience | #180 | — |
| 31.2 Security Review | #184 | — |
| 31.3 JSON Response Consistency | #181 | — |
| 31.4 UI/UX Consistency | #183 | — |
| 31.5 Documentation | #182 | — |

### Phase 32: Agent Data Model and Authentication (label: `Phase 32: Agent Data Model`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|------------------|
| 32.1 ApplicationUser Entity Changes | #196 | — |
| 32.2 PAT Authentication Handler Changes | #195 | — |
| 32.3 Admin Service Agent Methods | #197 | — |

### Phase 33: Agent Management UI (label: `Phase 33: Agent Management UI`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|------------------|
| 33.1 Agent List Page | #198 | — |
| 33.2 Agent Creation Flow | #199 | — |
| 33.3 Agent Edit and Detail Pages | #200 | — |
| 33.4 Agent Pause/Resume and Deletion | #201 | — |

### Phase 34: Agent MCP Tools and Bot Badge (label: `Phase 34: Agent MCP and Bot Badge`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|------------------|
| 34.1 ProfileTools MCP Tool | #202 | — |
| 34.2 my_assignments Agent Filtering | #203 | — |
| 34.3 Bot Badge Partial View | #204 | — |
| 34.4 Bot Badge Integration Across Views | #205 | — |

### Phase 35: Agent Polish and Hardening (label: `Phase 35: Agent Polish`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|------------------|
| 35.1 Security Hardening | #209 | — |
| 35.2 Edge Cases and Regression Testing | #208 | — |
| 35.3 UI Consistency Review | #207 | — |
| 35.4 Documentation | #206 | — |

### Phase 36: TeamWare Prerequisites (label: `Phase 36: Agent Prerequisites`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|------------------|
| 36.1 TaskItemStatus Enum Changes | #216 | — |
| 36.2 Task Views and Filtering | #215 | — |
| 36.3 Service and MCP Tool Updates | #217 | — |
| 36.4 Progress Tracking Updates | #214 | — |

### Phase 37: Agent Project Scaffold and Configuration (label: `Phase 37: Agent Scaffold`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|------------------|
| 37.1 Project Creation | #220 | — |
| 37.2 Configuration Model | #218 | — |
| 37.3 Agent Hosted Service | #221 | — |
| 37.4 Test Project Creation | #219 | — |

### Phase 38: Polling Loop and Task Discovery (label: `Phase 38: Agent Polling`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|------------------|
| 38.1 Agent Polling Loop | #223 | — |
| 38.2 Infrastructure Error Handling | #222 | — |
| 38.3 MCP Client Integration Tests | #224 | — |

### Phase 39: Task Processing Pipeline (label: `Phase 39: Agent Pipeline`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|------------------|
| 39.1 Copilot SDK Integration | #225 | — |
| 39.2 Default System Prompt | #228 | — |
| 39.3 Pipeline Integration | #226 | — |
| 39.4 Copilot CLI Error Handling | #227 | — |

### Phase 40: Status Transitions and Reporting (label: `Phase 40: Agent Status Transitions`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|------------------|
| 40.1 Status Transition Handler | #229 | — |
| 40.2 Lounge Message Formatting | #230 | — |
| 40.3 Pipeline Integration | #231 | — |

### Phase 41: Safety Guardrails and Dry Run (label: `Phase 41: Agent Guardrails`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|------------------|
| 41.1 Dry Run Mode | #234 | — |
| 41.2 Custom Permission Handler | #232 | — |
| 41.3 Action Restriction Verification | #233 | — |

### Phase 42: Repository Management and Lounge Integration (label: `Phase 42: Agent Repository`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|------------------|
| 42.1 Repository Manager | #237 | — |
| 42.2 End-to-End Lounge Integration Tests | #235 | — |
| 42.3 Multiple Identity Integration Tests | #236 | — |

### Phase 43: Agent Polish and Hardening (label: `Phase 43: Agent Polish`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|------------------|
| 43.1 Security Hardening | #238 | — |
| 43.2 Edge Cases and Regression Testing | #239 | — |
| 43.3 Logging Review | #240 | — |
| 43.4 Documentation | #241 | — |