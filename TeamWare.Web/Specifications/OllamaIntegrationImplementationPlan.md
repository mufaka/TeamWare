# TeamWare - Ollama AI Integration Implementation Plan

This document defines the phased implementation plan for the TeamWare Ollama AI Integration based on the [Ollama Integration Specification](OllamaIntegrationSpecification.md). Each phase builds on the previous one and is broken into work items suitable for GitHub Issues. Check off items as they are completed to track progress.

---

## Progress Summary

| Phase | Description | Status |
|-------|------------|--------|
| 22 | AI Foundation and Configuration | Not Started |
| 23 | Content Rewriting | Not Started |
| 24 | Summary Generation | Not Started |
| 25 | AI Polish and Hardening | Not Started |

---

## Current State

All original phases (0-9), social feature phases (10-14), and Project Lounge phases (15-21) are complete. The workspace is an ASP.NET Core MVC project (.NET 10) with:

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
- Security hardening, performance optimization, and UI polish

The Ollama AI Integration builds on top of this foundation. It uses the existing `GlobalConfiguration` infrastructure (from Phase 10) for settings and the existing `ServiceResult<T>` pattern for service layer results.

---

## Guiding Principles

All guiding principles from the [original implementation plan](ImplementationPlan.md), [social features implementation plan](SocialFeaturesImplementationPlan.md), and [Project Lounge implementation plan](ProjectLoungeImplementationPlan.md) continue to apply:

1. **Vertical slices** — Each phase delivers end-to-end working functionality (service, controller, view, tests).
2. **Tests accompany every feature** — No phase is complete without its test cases.
3. **One type per file** — Enforced throughout (MAINT-01).
4. **MVC only** — Controllers and Views, no Razor Pages (project guideline).

Additionally:

5. **Progressive enhancement** — All AI features are optional. The application is fully functional without Ollama. UI elements are rendered only when `OLLAMA_URL` is configured.
6. **User-in-the-loop** — AI output is always a suggestion. The user explicitly accepts, edits, or discards every result. No content is saved automatically by AI features.
7. **Fail-safe** — AI errors never prevent primary actions. Errors are surfaced only when the user interacts with an AI button, not on page load.

---

## Phase 22: AI Foundation and Configuration

Establish the Ollama service layer, configuration seeding, and the `AiController` skeleton. This phase delivers no user-facing AI features but provides the infrastructure for Phases 23 and 24.

### 22.1 GlobalConfiguration Seeding

- [ ] Seed `OLLAMA_URL` key with empty value and description: "Base URL of the Ollama instance (e.g., http://ollama-host:11434). Leave empty to disable AI features." (AI-01, AI-02, AI-05)
- [ ] Seed `OLLAMA_MODEL` key with empty value and description: "Ollama model name for AI completions. Defaults to llama3.1 when empty." (AI-01, AI-03, AI-05)
- [ ] Seed `OLLAMA_TIMEOUT` key with empty value and description: "AI request timeout in seconds. Defaults to 60 when empty." (AI-01, AI-04, AI-05)
- [ ] Write tests verifying the three keys are seeded on first run (AI-TEST-07)

### 22.2 OllamaService

- [ ] Create `IOllamaService` interface with methods: `GenerateCompletion(string systemPrompt, string userPrompt)`, `IsConfigured()` (Section 4.1)
- [ ] Create `OllamaService` implementation:
  - [ ] Read `OLLAMA_URL`, `OLLAMA_MODEL`, and `OLLAMA_TIMEOUT` from `IGlobalConfigurationService` with in-memory caching (60-second expiry) (AI-06, AI-07, AI-NF-03)
  - [ ] `IsConfigured` returns `true` when `OLLAMA_URL` has a non-empty cached value (AI-02)
  - [ ] `GenerateCompletion` short-circuits with failure result when not configured
  - [ ] `GenerateCompletion` calls Ollama `/api/chat` endpoint with system and user messages (AI-14)
  - [ ] Apply `OLLAMA_TIMEOUT` as the `HttpClient` timeout per request, defaulting to 60 seconds (AI-04, AI-NF-02)
  - [ ] Default model to `llama3.1` when `OLLAMA_MODEL` is empty (AI-03)
  - [ ] Return `ServiceResult<string>.Failure(...)` for: not configured, HTTP error, timeout, empty/unparseable response (AI-50, AI-51, AI-52)
- [ ] Register `IOllamaService` / `OllamaService` in DI (Section 4.3)
- [ ] Write unit tests: configured success, not configured, timeout, HTTP error, empty response (AI-TEST-01)

### 22.3 AiAssistantService

- [ ] Create `SummaryPeriod` enum: `Today`, `ThisWeek`, `ThisMonth` (Section 4.2)
- [ ] Create `IAiAssistantService` interface with methods: `RewriteProjectDescription`, `RewriteTaskDescription`, `PolishComment`, `ExpandInboxItem`, `GenerateProjectSummary`, `GeneratePersonalDigest`, `GenerateReviewPreparation`, `IsAvailable` (Section 4.2)
- [ ] Create `AiAssistantService` implementation:
  - [ ] `IsAvailable` delegates to `IOllamaService.IsConfigured` (Section 4.2)
  - [ ] Rewrite methods construct system prompt + user prompt and delegate to `IOllamaService.GenerateCompletion` (AI-20, AI-21, AI-22, AI-23)
  - [ ] Summary methods are stubbed (implemented in Phase 24)
  - [ ] All methods propagate failure results from `IOllamaService` (Section 4.2)
- [ ] Register `IAiAssistantService` / `AiAssistantService` in DI (Section 4.3)
- [ ] Write unit tests for rewrite methods verifying prompt construction and result propagation (AI-TEST-02)

### 22.4 AiController Skeleton

- [ ] Create `AiController` with `[Authorize]` attribute (AI-60)
- [ ] Add `IsAvailable` GET action returning JSON `{ available: bool }` (Section 5.1)
- [ ] Add empty POST action stubs for all endpoints (populated in Phases 23 and 24) (Section 5.1)
- [ ] Ensure all POST actions require anti-forgery tokens (SEC-03)
- [ ] Write integration tests verifying authentication is required on all endpoints (AI-TEST-04)
- [ ] Write integration tests verifying `IsAvailable` returns `false` when `OLLAMA_URL` is empty (AI-TEST-05)

---

## Phase 23: Content Rewriting

Deliver the AI rewrite buttons for project descriptions, task descriptions, comments, and inbox items.

### 23.1 Rewrite Controller Actions

- [ ] Implement `RewriteProjectDescription` POST action:
  - [ ] Accept `int projectId, string description` (Section 5.1)
  - [ ] Validate project membership (AI-61)
  - [ ] Validate input length (max 4000 characters) (AI-NF-05, SEC-04)
  - [ ] Call `IAiAssistantService.RewriteProjectDescription` and return JSON result
- [ ] Implement `RewriteTaskDescription` POST action:
  - [ ] Accept `int taskId, string description` (Section 5.1)
  - [ ] Validate project membership for the task's project (AI-62)
  - [ ] Validate input length (max 4000 characters) (AI-NF-05, SEC-04)
  - [ ] Call `IAiAssistantService.RewriteTaskDescription` and return JSON result
- [ ] Implement `PolishComment` POST action:
  - [ ] Accept `int taskId, string comment` (Section 5.1)
  - [ ] Validate the user can post comments on the task (AI-63)
  - [ ] Validate input length (max 4000 characters) (AI-NF-05, SEC-04)
  - [ ] Call `IAiAssistantService.PolishComment` and return JSON result
- [ ] Implement `ExpandInboxItem` POST action:
  - [ ] Accept `int inboxItemId, string title, string? description` (Section 5.1)
  - [ ] Validate the user owns the inbox item (AI-64)
  - [ ] Call `IAiAssistantService.ExpandInboxItem` and return JSON result
- [ ] Write integration tests for authorization enforcement on all rewrite endpoints (AI-TEST-04)
- [ ] Write integration tests verifying behavior when Ollama is not configured (AI-TEST-05)

### 23.2 Rewrite UI - Project Edit Form

- [ ] Add "AI Rewrite" button next to the description field on the project edit view (AI-10)
- [ ] Button is rendered only when Ollama is configured (AI-NF-04, AI-54)
- [ ] Button is hidden when the description field is empty (AI-18)
- [ ] Clicking the button sends the description to `/Ai/RewriteProjectDescription` via HTMX or JavaScript (AI-14)
- [ ] Display loading spinner while request is in progress (AI-15, AI-UI-02, AI-NF-01)
- [ ] Display the AI suggestion alongside the original text with "Accept" and "Discard" buttons (AI-16, AI-17, AI-UI-03)
- [ ] Handle error responses with toast notifications (AI-50, AI-51, AI-52, AI-UI-06)
- [ ] Style buttons and comparison view with Tailwind CSS, supporting light/dark theme (AI-UI-01, AI-UI-07, AI-NF-06)
- [ ] Write UI rendering tests verifying button visibility based on configuration (AI-TEST-06)

### 23.3 Rewrite UI - Task Edit Form

- [ ] Add "AI Rewrite" button next to the description field on the task edit view (AI-11)
- [ ] Same behavior pattern as project edit form: conditional rendering, empty-field hiding, loading state, comparison view, accept/discard, error handling (AI-14 through AI-18, AI-50 through AI-52)
- [ ] Style consistently with project edit form (AI-UI-01, AI-UI-07, AI-NF-06)

### 23.4 Rewrite UI - Comment Form

- [ ] Add "Polish" button on the comment form (AI-12)
- [ ] Same behavior pattern: conditional rendering, empty-field hiding, loading state, comparison view, accept/discard, error handling
- [ ] Style consistently (AI-UI-01, AI-UI-07, AI-NF-06)

### 23.5 Rewrite UI - Inbox Clarify Form

- [ ] Add "Expand" button next to the description field on the inbox item clarify form (AI-13)
- [ ] Same behavior pattern: conditional rendering, empty-field hiding, loading state, comparison view, accept/discard, error handling
- [ ] Style consistently (AI-UI-01, AI-UI-07, AI-NF-06)

### 23.6 Shared AI JavaScript

- [ ] Create `ai-rewrite.js` (or extend an existing JS module) to handle:
  - [ ] Sending rewrite requests to the `AiController` endpoints
  - [ ] Displaying loading state on the triggering button
  - [ ] Rendering the comparison view (original vs. suggestion) with Accept/Discard controls
  - [ ] Handling Accept (replace field content) and Discard (close comparison view)
  - [ ] Displaying toast notifications for errors
- [ ] Ensure all interactions are asynchronous and non-blocking (AI-NF-01)
- [ ] Ensure responsive behavior across breakpoints (AI-UI-08)

---

## Phase 24: Summary Generation

Deliver the AI summary buttons for the project dashboard, personal dashboard, and GTD review page.

### 24.1 Summary Data Gathering

- [ ] Implement `AiAssistantService.GenerateProjectSummary`:
  - [ ] Gather data from `IActivityLogService.GetActivityForProject` for the specified period (AI-33)
  - [ ] Gather data from `IProgressService.GetProjectStatistics`, `GetOverdueTasks`, and `GetUpcomingDeadlines` (AI-33, AI-34)
  - [ ] Format gathered data as structured text for the user prompt (AI-39)
  - [ ] Construct system prompt for project summary (AI-44)
  - [ ] Call `IOllamaService.GenerateCompletion` and return result
- [ ] Implement `AiAssistantService.GeneratePersonalDigest`:
  - [ ] Gather the user's activity in the last 24 hours from `IActivityLogService` (AI-35)
  - [ ] Gather the user's tasks from `ITaskService` to identify completed and in-progress work (AI-36)
  - [ ] Format gathered data as structured text for the user prompt (AI-39)
  - [ ] Construct system prompt for personal digest (AI-45)
  - [ ] Call `IOllamaService.GenerateCompletion` and return result
- [ ] Implement `AiAssistantService.GenerateReviewPreparation`:
  - [ ] Gather unprocessed inbox items from `IInboxService.GetUnprocessedItems` (AI-37, AI-38)
  - [ ] Gather Someday/Maybe items from `ITaskService.GetSomedayMaybe` (AI-38)
  - [ ] Gather upcoming deadlines from `IProgressService.GetUpcomingDeadlines` across user's projects (AI-38)
  - [ ] Identify stale tasks (no activity in 14+ days) from `IActivityLogService` and `ITaskService` (AI-38)
  - [ ] Format gathered data as structured text for the user prompt (AI-39)
  - [ ] Construct system prompt for review preparation (AI-46)
  - [ ] Call `IOllamaService.GenerateCompletion` and return result
- [ ] Write unit tests for each summary method verifying data gathering and prompt construction (AI-TEST-03)

### 24.2 Summary Controller Actions

- [ ] Implement `ProjectSummary` POST action:
  - [ ] Accept `int projectId, string period` (Section 5.1)
  - [ ] Parse `period` to `SummaryPeriod` enum (AI-43)
  - [ ] Validate project membership (AI-65)
  - [ ] Call `IAiAssistantService.GenerateProjectSummary` and return JSON result
- [ ] Implement `PersonalDigest` POST action:
  - [ ] Scope to authenticated user (AI-66)
  - [ ] Call `IAiAssistantService.GeneratePersonalDigest` and return JSON result
- [ ] Implement `ReviewPreparation` POST action:
  - [ ] Scope to authenticated user (AI-67)
  - [ ] Call `IAiAssistantService.GenerateReviewPreparation` and return JSON result
- [ ] Write integration tests for authorization enforcement on all summary endpoints (AI-TEST-04)
- [ ] Write integration tests verifying behavior when Ollama is not configured (AI-TEST-05)

### 24.3 Summary UI - Project Dashboard

- [ ] Add "Generate Summary" button on the project dashboard view (AI-30)
- [ ] Add period selector (Today, This Week, This Month) as a button group or dropdown (AI-43, AI-UI-09)
- [ ] Button is rendered only when Ollama is configured (AI-NF-04, AI-54)
- [ ] Clicking the button sends `projectId` and `period` to `/Ai/ProjectSummary` (AI-39)
- [ ] Display loading spinner while request is in progress (AI-40, AI-UI-02, AI-NF-01)
- [ ] Display the summary in a dismissible panel below the button (AI-41, AI-UI-04)
- [ ] Include a "Copy to Clipboard" button on the summary panel (AI-42, AI-UI-05)
- [ ] Handle error responses with toast notifications (AI-50, AI-51, AI-52, AI-UI-06)
- [ ] Style with Tailwind CSS, supporting light/dark theme (AI-UI-07, AI-NF-06)

### 24.4 Summary UI - Personal Dashboard

- [ ] Add "Generate Summary" button on the user's personal dashboard (Home/Index) (AI-31)
- [ ] Button is rendered only when Ollama is configured (AI-NF-04, AI-54)
- [ ] Clicking the button sends request to `/Ai/PersonalDigest` (AI-35)
- [ ] Same display pattern: loading state, dismissible summary panel, copy to clipboard, error handling
- [ ] Style consistently with project dashboard summary (AI-UI-07, AI-NF-06)

### 24.5 Summary UI - GTD Review Page

- [ ] Add "Prepare Review Summary" button on the GTD review page (AI-32)
- [ ] Button is rendered only when Ollama is configured (AI-NF-04, AI-54)
- [ ] Clicking the button sends request to `/Ai/ReviewPreparation` (AI-37)
- [ ] Same display pattern: loading state, dismissible summary panel, copy to clipboard, error handling
- [ ] Style consistently (AI-UI-07, AI-NF-06)

### 24.6 Shared AI Summary JavaScript

- [ ] Create `ai-summary.js` (or extend existing AI JS module) to handle:
  - [ ] Sending summary requests to the `AiController` endpoints
  - [ ] Displaying loading state on the triggering button
  - [ ] Rendering the dismissible summary panel with the generated text
  - [ ] Handling the "Copy to Clipboard" action
  - [ ] Dismissing the summary panel
  - [ ] Displaying toast notifications for errors
- [ ] Ensure all interactions are asynchronous and non-blocking (AI-NF-01)
- [ ] Ensure responsive behavior across breakpoints (AI-UI-08)

---

## Phase 25: AI Polish and Hardening

Final pass on cross-cutting concerns, accessibility, error handling robustness, and documentation.

### 25.1 Error Handling and Resilience

- [ ] Verify all AI error paths render user-friendly toast messages (AI-50, AI-51, AI-52)
- [ ] Verify AI errors never block primary actions (saving projects, tasks, comments, inbox items, reviews) (AI-53)
- [ ] Verify AI buttons are rendered when Ollama is configured but unreachable, and errors surface only on click (AI-54)
- [ ] Test behavior when Ollama returns slowly (near-timeout) to ensure UI remains responsive (AI-NF-01)
- [ ] Test behavior when `OLLAMA_URL` is changed from empty to a valid URL (feature appears within 60 seconds) (AI-07)
- [ ] Test behavior when `OLLAMA_URL` is cleared (feature disappears within 60 seconds) (AI-07)

### 25.2 UI/UX Consistency

- [ ] Verify AI button styling is consistent across all four rewrite locations and three summary locations (AI-UI-01)
- [ ] Verify light/dark theme support on all AI UI elements (AI-UI-07, AI-NF-06)
- [ ] Verify no emoticons or emojis in AI chrome or labels (AI-NF-07)
- [ ] Verify responsive behavior across breakpoints (AI-UI-08)
- [ ] Verify comparison view (rewrite) and summary panel are usable on mobile viewports

### 25.3 Security Review

- [ ] Audit all AI endpoints for authentication enforcement (AI-60)
- [ ] Audit all AI endpoints for resource-level authorization (AI-61 through AI-67)
- [ ] Verify anti-forgery tokens on all POST endpoints (SEC-03)
- [ ] Verify input length validation on all rewrite endpoints (AI-NF-05, SEC-04)
- [ ] Verify `GlobalConfiguration` Ollama keys are editable only by site admins (AI-68)

### 25.4 Documentation

- [ ] Update `README.md` with Ollama integration setup instructions (how to configure `OLLAMA_URL` via admin dashboard)
- [ ] Update `copilot-instructions.md` with Phase 22-25 branch names and issue map
- [ ] Update the [Ollama Integration Idea document](OllamaIntegrationIdea.md) next steps to mark the implementation plan as complete

---

## Branch Strategy

| Phase | Branch Name |
|-------|------------|
| Phase 22 | `phase-22/ai-foundation` |
| Phase 23 | `phase-23/ai-content-rewriting` |
| Phase 24 | `phase-24/ai-summary-generation` |
| Phase 25 | `phase-25/ai-polish-hardening` |

---

## References

- [OllamaIntegrationSpecification.md](OllamaIntegrationSpecification.md) — Formal specification for this feature
- [OllamaIntegrationIdea.md](OllamaIntegrationIdea.md) — Original idea document and design exploration
- [ImplementationPlan.md](ImplementationPlan.md) — Main TeamWare implementation plan (Phases 0-9)
- [SocialFeaturesImplementationPlan.md](SocialFeaturesImplementationPlan.md) — Social Features implementation plan (Phases 10-14)
- [ProjectLoungeImplementationPlan.md](ProjectLoungeImplementationPlan.md) — Project Lounge implementation plan (Phases 15-21)
