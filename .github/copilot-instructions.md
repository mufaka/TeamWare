# Copilot Instructions

## Project Guidelines
- TeamWare is an ASP.NET Core MVC application. It should not use Razor Pages. Use Controllers and Views instead.
- One type per file (MAINT-01).
- Tests accompany every feature. No phase is complete without its test cases.
- Follow the implementation plan in `TeamWare.Web/Specifications/ImplementationPlan.md`, `TeamWare.Web/Specifications/SocialFeaturesImplementationPlan.md`, `TeamWare.Web/Specifications/ProjectLoungeImplementationPlan.md`, `TeamWare.Web/Specifications/OllamaIntegrationImplementationPlan.md`, `TeamWare.Web/Specifications/McpServerImplementationPlan.md`, and `TeamWare.Web/Specifications/AgentUsersImplementationPlan.md`.
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

> **Phases 0-21 (Complete):** All issue mappings for Phases 0-21 have been archived. These phases are fully merged to `master`. For historical reference, see the closed issues in GitHub or the individual implementation plans:
> - Phases 0-9: `TeamWare.Web/Specifications/ImplementationPlan.md`
> - Phases 10-14: `TeamWare.Web/Specifications/SocialFeaturesImplementationPlan.md`
> - Phases 15-21: `TeamWare.Web/Specifications/ProjectLoungeImplementationPlan.md`

### Phase 22: AI Foundation and Configuration (label: `Phase 22: AI Foundation`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|------------------|
| 22.1 GlobalConfiguration Seeding | #136 | — |
| 22.2 OllamaService | #138 | — |
| 22.3 AiAssistantService | #137 | — |
| 22.4 AiController Skeleton | #139 | — |

### Phase 23: Content Rewriting (label: `Phase 23: Content Rewriting`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|------------------|
| 23.1 Rewrite Controller Actions | #140 | — |
| 23.2 Rewrite UI - Project Edit Form | #141 | — |
| 23.3 Rewrite UI - Task Edit Form | #145 | — |
| 23.4 Rewrite UI - Comment Form | #143 | — |
| 23.5 Rewrite UI - Inbox Clarify Form | #142 | — |
| 23.6 Shared AI JavaScript | #144 | — |

### Phase 24: Summary Generation (label: `Phase 24: Summary Generation`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|------------------|
| 24.1 Summary Data Gathering | #146 | — |
| 24.2 Summary Controller Actions | #147 | — |
| 24.3 Summary UI - Project Dashboard | #148 | — |
| 24.4 Summary UI - Personal Dashboard | #149 | — |
| 24.5 Summary UI - GTD Review Page | #150 | — |
| 24.6 Shared AI Summary JavaScript | #151 | — |

### Phase 25: AI Polish and Hardening (label: `Phase 25: AI Polish`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|------------------|
| 25.1 Error Handling and Resilience | #152 | — |
| 25.2 UI/UX Consistency | #153 | — |
| 25.3 Security Review | #154 | — |
| 25.4 Documentation | #155 | — |

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