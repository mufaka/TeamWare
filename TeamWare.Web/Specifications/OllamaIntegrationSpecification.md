# TeamWare - Ollama AI Integration Specification

## 1. Introduction

### 1.1 Purpose

This document provides the formal specification for the Ollama AI Integration feature being added to TeamWare. It defines the functional requirements, configuration model, service layer design, and UI integration needed to support optional AI-assisted content rewriting and activity summary generation powered by a self-hosted Ollama instance. This specification is a companion to the [main TeamWare specification](Specification.md) and follows the same conventions.

### 1.2 Scope

The Ollama AI Integration introduces two AI-assisted features to TeamWare:

1. **Content Rewriting** — Users can request AI-polished rewrites of project descriptions, task descriptions, comments, and inbox items. The original text is sent to a local Ollama instance and a rewritten version is returned for the user to accept or discard.
2. **Summary Generation** — Users can generate human-readable summaries of project activity, personal daily accomplishments, and GTD review preparation data. The system gathers structured data from existing services and sends it to Ollama for natural-language summarization.

Both features are entirely optional. They are hidden from the UI when no Ollama URL is configured and never interfere with primary application functionality.

### 1.3 Definitions and Acronyms

| Term | Definition |
|------|-----------|
| Ollama | A local LLM runtime that exposes an HTTP API for inference. Runs open-weight models (Llama 3.1, Mistral, Gemma, etc.) without external dependencies. |
| Completion | A generated text response from an LLM given a prompt. |
| System Prompt | An instruction prepended to every request that defines the LLM's behavior and output format. |
| Rewrite | An AI-generated alternative version of user-provided text, intended to improve clarity, grammar, or structure. |
| Summary | An AI-generated natural-language description of structured activity data gathered from existing TeamWare services. |
| GlobalConfiguration | The existing key-value configuration table in TeamWare, managed via the admin dashboard. |

### 1.4 Design Principles

- **Optional and non-invasive** — The integration is a progressive enhancement. No feature depends on Ollama being available. The application is fully functional without it.
- **User-in-the-loop** — All AI output is presented as a suggestion. The user explicitly accepts, edits, or discards every result. No content is saved automatically.
- **Self-hosted** — No data leaves the local network. Ollama runs on the same network as TeamWare. No external API keys or cloud services are involved.
- **Fail-safe** — Ollama errors (unreachable, timeout, unusable response) are handled gracefully. Primary actions (saving a task, posting a comment) never fail because of an AI error.
- **Consistent with existing patterns** — Configuration uses `GlobalConfiguration`. Services follow the `ServiceResult<T>` pattern. UI uses HTMX for async operations, consistent with the rest of TeamWare.

---

## 2. Technology Additions

| Layer | Technology | Purpose |
|-------|-----------|---------|
| AI Inference | Ollama HTTP API (`/api/chat`) | Local LLM completion requests |
| HTTP Client | `System.Net.Http.HttpClient` | Communication with the Ollama API from `OllamaService` |
| Caching | `System.Runtime.Caching.MemoryCache` or `IMemoryCache` | Short-lived cache for `GlobalConfiguration` values to avoid per-request database reads |

All other technology choices remain unchanged from the [main specification](Specification.md).

---

## 3. Functional Requirements

### 3.1 Configuration

| ID | Requirement |
|----|------------|
| AI-01 | The system shall store Ollama configuration in the existing `GlobalConfiguration` table using the keys `OLLAMA_URL`, `OLLAMA_MODEL`, and `OLLAMA_TIMEOUT` |
| AI-02 | The `OLLAMA_URL` key shall hold the base URL of the Ollama instance (e.g., `http://ollama-host:11434`). When this value is empty or the key does not exist, all AI features shall be disabled and hidden from the UI |
| AI-03 | The `OLLAMA_MODEL` key shall hold the model name to use for completions. When empty, the system shall default to `llama3.1` |
| AI-04 | The `OLLAMA_TIMEOUT` key shall hold the request timeout in seconds. When empty or non-numeric, the system shall default to `60` |
| AI-05 | The `OLLAMA_URL`, `OLLAMA_MODEL`, and `OLLAMA_TIMEOUT` keys shall be seeded with empty values on first run so they appear in the admin configuration list |
| AI-06 | Configuration values shall be cached in memory for a short duration (60 seconds) to avoid a database round-trip on every AI-eligible page load |
| AI-07 | Changes to configuration values via the admin dashboard shall take effect within the cache duration (60 seconds) without requiring an application restart |

### 3.2 Content Rewriting

| ID | Requirement |
|----|------------|
| AI-10 | When Ollama is configured, an "AI Rewrite" button shall appear on the project edit form next to the description field |
| AI-11 | When Ollama is configured, an "AI Rewrite" button shall appear on the task edit form next to the description field |
| AI-12 | When Ollama is configured, a "Polish" button shall appear on the comment form before posting |
| AI-13 | When Ollama is configured, an "Expand" button shall appear on the inbox item clarify form next to the description field |
| AI-14 | Clicking an AI rewrite button shall send the current field content to the Ollama API with a context-appropriate system prompt |
| AI-15 | The system shall display a loading indicator while waiting for the Ollama response. The loading indicator shall not block interaction with other page elements |
| AI-16 | The system shall display the AI-generated rewrite alongside the original text so the user can compare the two |
| AI-17 | The user shall be able to accept the rewrite (replacing the field content) or discard it (keeping the original) |
| AI-18 | The AI rewrite button shall be hidden when the text field is empty |
| AI-19 | The rewrite request shall include only the content of the specific text field. No other page data shall be sent to Ollama for rewrite requests |
| AI-20 | The system prompt for project description rewrites shall instruct the model to produce clear, professional, well-structured text while preserving the original meaning |
| AI-21 | The system prompt for task description rewrites shall instruct the model to produce a clear requirement or work item description while preserving the original meaning |
| AI-22 | The system prompt for comment polishing shall instruct the model to improve clarity and tone while preserving the original intent and keeping the length similar |
| AI-23 | The system prompt for inbox item expansion shall instruct the model to expand a brief note into a fuller description suitable for a task, adding relevant detail while preserving the original intent |

### 3.3 Summary Generation

| ID | Requirement |
|----|------------|
| AI-30 | When Ollama is configured, a "Generate Summary" button shall appear on the project dashboard |
| AI-31 | When Ollama is configured, a "Generate Summary" button shall appear on the user's personal dashboard |
| AI-32 | When Ollama is configured, a "Prepare Review Summary" button shall appear on the GTD review page |
| AI-33 | The project activity summary shall gather data from `IActivityLogService`, `IProgressService`, and `ICommentService` for the specified project and time period |
| AI-34 | The project activity summary shall include: task counts by status, tasks completed in the period, tasks created in the period, overdue tasks, upcoming deadlines, and recent comment activity |
| AI-35 | The personal daily digest shall gather data from `IActivityLogService` and `ITaskService` for the current user's activity in the last 24 hours across all projects |
| AI-36 | The personal daily digest shall include: tasks completed, tasks worked on (status changes, comments), and projects the user was active in |
| AI-37 | The review preparation summary shall gather data from `IInboxService`, `ITaskService`, and `IProgressService` for the current user |
| AI-38 | The review preparation summary shall include: unprocessed inbox item count and titles, stale tasks (no activity in 14+ days), upcoming deadlines (next 7 days), and Someday/Maybe item count |
| AI-39 | The gathered data shall be formatted as structured text and sent to Ollama with a system prompt instructing the model to produce a concise, human-readable summary |
| AI-40 | The system shall display a loading indicator while waiting for the Ollama response |
| AI-41 | The generated summary shall be displayed in a dismissible panel on the page |
| AI-42 | The user shall be able to copy the summary text to the clipboard |
| AI-43 | The project activity summary shall support a configurable time period: "Today", "This Week" (last 7 days), and "This Month" (last 30 days) |
| AI-44 | The system prompt for project summaries shall instruct the model to produce a concise narrative summary highlighting key accomplishments, blockers, and upcoming work |
| AI-45 | The system prompt for personal digests shall instruct the model to produce a brief first-person summary of the user's day suitable for a status update |
| AI-46 | The system prompt for review preparation shall instruct the model to produce a prioritized list of items that need attention during the review |

### 3.4 Error Handling

| ID | Requirement |
|----|------------|
| AI-50 | If the Ollama endpoint is unreachable or returns a non-success HTTP status, the system shall display a toast notification stating "AI assistant unavailable" and hide the loading indicator |
| AI-51 | If the Ollama request times out (exceeds `OLLAMA_TIMEOUT`), the system shall display a toast notification stating "AI request timed out. Try again or write manually." and hide the loading indicator |
| AI-52 | If the Ollama response cannot be parsed or is empty, the system shall display a message stating "Could not generate a suggestion. Try again or write manually." |
| AI-53 | AI errors shall never prevent or interfere with primary actions (saving a project, saving a task, posting a comment, clarifying an inbox item, completing a review) |
| AI-54 | If Ollama is configured but unreachable, AI buttons shall still be rendered. The error is surfaced only when the user clicks the button, not on page load |

### 3.5 Authorization

| ID | Requirement |
|----|------------|
| AI-60 | AI rewrite and summary endpoints shall require authentication |
| AI-61 | AI rewrite for a project description shall require project membership (Owner, Admin, or Member) |
| AI-62 | AI rewrite for a task description shall require project membership for the task's project |
| AI-63 | AI rewrite for a comment shall require that the user is the comment author or has permission to post comments on the task |
| AI-64 | AI rewrite for an inbox item shall require that the user owns the inbox item |
| AI-65 | Project activity summary shall require project membership |
| AI-66 | Personal daily digest shall be scoped to the authenticated user's own activity |
| AI-67 | Review preparation summary shall be scoped to the authenticated user's own data |
| AI-68 | `GlobalConfiguration` keys for Ollama shall be editable only by site admins, consistent with existing admin-only configuration management |

---

## 4. Service Layer Design

### 4.1 IOllamaService

A thin wrapper around the Ollama HTTP API. Responsible for HTTP communication, timeout handling, and response deserialization.

| Method | Parameters | Returns | Description |
|--------|-----------|---------|-------------|
| `GenerateCompletion` | `string systemPrompt, string userPrompt` | `ServiceResult<string>` | Sends a chat completion request to Ollama and returns the generated text. Returns a failure result if Ollama is not configured, unreachable, times out, or returns an unusable response. |
| `IsConfigured` | _(none)_ | `Task<bool>` | Returns `true` if `OLLAMA_URL` has a non-empty value in `GlobalConfiguration`. Uses the cached configuration value. |

**Implementation notes:**
- Reads `OLLAMA_URL`, `OLLAMA_MODEL`, and `OLLAMA_TIMEOUT` from `IGlobalConfigurationService` with in-memory caching (60-second expiry).
- Uses `HttpClient` with the timeout set per-request from the cached `OLLAMA_TIMEOUT` value.
- Calls the Ollama `/api/chat` endpoint with a messages array containing the system prompt and user prompt.
- Returns `ServiceResult<string>.Failure(...)` for all error cases (not configured, HTTP error, timeout, empty response).

### 4.2 IAiAssistantService

A higher-level service that constructs domain-specific prompts, gathers context data, calls `IOllamaService`, and returns results.

| Method | Parameters | Returns | Description |
|--------|-----------|---------|-------------|
| `RewriteProjectDescription` | `string description` | `ServiceResult<string>` | Rewrites a project description for clarity and professionalism. |
| `RewriteTaskDescription` | `string description` | `ServiceResult<string>` | Rewrites a task description as a clear work item. |
| `PolishComment` | `string comment` | `ServiceResult<string>` | Polishes a comment for clarity and tone. |
| `ExpandInboxItem` | `string title, string? description` | `ServiceResult<string>` | Expands a brief inbox item into a fuller task description. |
| `GenerateProjectSummary` | `int projectId, string userId, SummaryPeriod period` | `ServiceResult<string>` | Gathers project activity data and generates a narrative summary. |
| `GeneratePersonalDigest` | `string userId` | `ServiceResult<string>` | Gathers the user's last 24 hours of activity and generates a daily digest. |
| `GenerateReviewPreparation` | `string userId` | `ServiceResult<string>` | Gathers inbox, stale task, and deadline data and generates a review preparation summary. |
| `IsAvailable` | _(none)_ | `Task<bool>` | Delegates to `IOllamaService.IsConfigured`. Convenience method for UI visibility checks. |

**Implementation notes:**
- Each method constructs a system prompt and a user prompt, then delegates to `IOllamaService.GenerateCompletion`.
- Summary methods inject `IActivityLogService`, `IProgressService`, `ITaskService`, `IInboxService`, and `ICommentService` to gather raw data.
- The `SummaryPeriod` enum defines `Today`, `ThisWeek` (7 days), and `ThisMonth` (30 days).
- All methods return `ServiceResult<string>.Failure(...)` if Ollama is not configured, propagating the result from `IOllamaService`.

### 4.3 Service Registration

Both services are registered in DI unconditionally during application startup. They handle the disabled state internally:
- `IOllamaService` checks the cached `OLLAMA_URL` value and short-circuits with a failure result if empty.
- `IAiAssistantService` delegates to `IOllamaService` and propagates the failure.

No conditional registration or no-op implementations are needed.

---

## 5. API Endpoints

AI operations are exposed as controller actions returning JSON results, called via HTMX or JavaScript from the UI.

### 5.1 AiController

| Action | HTTP Method | Route | Parameters | Returns |
|--------|------------|-------|-----------|---------|
| `RewriteProjectDescription` | POST | `/Ai/RewriteProjectDescription` | `int projectId, string description` | JSON: `{ succeeded, data, errors }` |
| `RewriteTaskDescription` | POST | `/Ai/RewriteTaskDescription` | `int taskId, string description` | JSON: `{ succeeded, data, errors }` |
| `PolishComment` | POST | `/Ai/PolishComment` | `int taskId, string comment` | JSON: `{ succeeded, data, errors }` |
| `ExpandInboxItem` | POST | `/Ai/ExpandInboxItem` | `int inboxItemId, string title, string? description` | JSON: `{ succeeded, data, errors }` |
| `ProjectSummary` | POST | `/Ai/ProjectSummary` | `int projectId, string period` | JSON: `{ succeeded, data, errors }` |
| `PersonalDigest` | POST | `/Ai/PersonalDigest` | _(none, uses authenticated user)_ | JSON: `{ succeeded, data, errors }` |
| `ReviewPreparation` | POST | `/Ai/ReviewPreparation` | _(none, uses authenticated user)_ | JSON: `{ succeeded, data, errors }` |
| `IsAvailable` | GET | `/Ai/IsAvailable` | _(none)_ | JSON: `{ available: bool }` |

All POST actions require anti-forgery tokens, consistent with existing TeamWare conventions. All actions require authentication. Actions that reference a project or task enforce membership authorization.

---

## 6. Data Model

### 6.1 New Entities

No new database entities are required. The Ollama integration is stateless; it reads existing data (activity logs, tasks, inbox items) and returns transient results that are not persisted unless the user explicitly saves them through existing save operations.

### 6.2 Modified Entities

#### GlobalConfiguration (Seeded Keys)

Three new keys are seeded into the `GlobalConfiguration` table on first run:

| Key | Default Value | Description |
|-----|---------------|-------------|
| `OLLAMA_URL` | _(empty)_ | Base URL of the Ollama instance. Empty means AI features are disabled. |
| `OLLAMA_MODEL` | _(empty)_ | Model name for completions. Empty defaults to `llama3.1` at runtime. |
| `OLLAMA_TIMEOUT` | _(empty)_ | Request timeout in seconds. Empty defaults to `60` at runtime. |

---

## 7. Changes to Existing Requirements

No existing functional requirements are modified. The Ollama integration adds new capabilities as progressive enhancements without changing existing behavior.

The following existing non-functional requirements apply with additional considerations:

| Requirement | Consideration |
|-------------|--------------|
| SEC-03 (Anti-forgery tokens) | All AI POST endpoints shall include anti-forgery token validation |
| SEC-04 (Input validation) | User-provided text sent to Ollama shall be validated for length (max 4000 characters for rewrites, reasonable limits for summary context) |
| SEC-05 (Authorization enforcement) | All AI endpoints enforce authentication and resource-level authorization |

---

## 8. Non-Functional Requirements

| ID | Requirement |
|----|------------|
| AI-NF-01 | AI operations shall be asynchronous and shall not block the UI thread or prevent interaction with other page elements |
| AI-NF-02 | The `OllamaService` HTTP timeout shall be configurable via the `OLLAMA_TIMEOUT` GlobalConfiguration key, defaulting to 60 seconds |
| AI-NF-03 | GlobalConfiguration values for Ollama shall be cached in memory with a 60-second expiry to minimize database reads |
| AI-NF-04 | AI buttons and UI elements shall not be rendered when Ollama is not configured, producing no additional page weight or HTTP requests |
| AI-NF-05 | The maximum input length for rewrite requests shall be 4000 characters |
| AI-NF-06 | The AI UI shall follow existing TeamWare styling conventions (Tailwind CSS 4, light/dark theme support) |
| AI-NF-07 | The AI UI shall not contain emoticons or emojis in chrome or labels (consistent with UI-07) |

---

## 9. UI/UX Requirements

| ID | Requirement |
|----|------------|
| AI-UI-01 | AI buttons shall be visually distinct but unobtrusive, using a secondary/muted style consistent with existing button conventions |
| AI-UI-02 | AI buttons shall display a loading spinner or pulsing indicator when a request is in progress |
| AI-UI-03 | The rewrite comparison view shall display the original text and the AI suggestion side by side or stacked, with "Accept" and "Discard" buttons |
| AI-UI-04 | The summary display shall appear in a dismissible panel below the "Generate Summary" button |
| AI-UI-05 | The summary panel shall include a "Copy to Clipboard" button |
| AI-UI-06 | Toast notifications for AI errors shall use the existing `_Notification.cshtml` partial or equivalent pattern |
| AI-UI-07 | All AI UI elements shall support both light and dark themes |
| AI-UI-08 | AI UI elements shall be responsive across all supported breakpoints |
| AI-UI-09 | The project summary shall include a period selector (Today, This Week, This Month) rendered as a button group or dropdown before triggering generation |

---

## 10. Testing Requirements

| ID | Requirement |
|----|------------|
| AI-TEST-01 | `OllamaService` shall have unit tests verifying behavior when configured, not configured, on timeout, and on HTTP error |
| AI-TEST-02 | `AiAssistantService` shall have unit tests for each rewrite method verifying prompt construction and result propagation |
| AI-TEST-03 | `AiAssistantService` shall have unit tests for each summary method verifying data gathering and prompt construction |
| AI-TEST-04 | `AiController` shall have integration tests verifying authentication and authorization enforcement on all endpoints |
| AI-TEST-05 | `AiController` shall have integration tests verifying correct behavior when Ollama is not configured (endpoints return appropriate failure responses) |
| AI-TEST-06 | UI rendering shall be tested to verify AI buttons are hidden when `OLLAMA_URL` is empty and shown when it is configured |
| AI-TEST-07 | Seed data tests shall verify that the `OLLAMA_URL`, `OLLAMA_MODEL`, and `OLLAMA_TIMEOUT` keys are created on first run |

---

## 11. Future Considerations

The following features are out of scope for this release but may be considered for future iterations:

- **Task breakdown and subtask suggestions** — AI suggests smaller subtasks from a high-level task (Idea 2 from the [idea document](OllamaIntegrationIdea.md))
- **Smart inbox triage** — AI suggests project assignment, priority, and expanded descriptions during inbox clarification (Idea 4)
- **Standup/status report generation** — AI drafts daily standups from activity data (Idea 5; depends on standup feature)
- **Per-feature model selection** — Allow different Ollama models for different AI features
- **Admin-editable prompts** — Expose system prompts as `GlobalConfiguration` keys for customization
- **Response caching** — Cache identical prompt results to reduce Ollama load on slower hardware
- **Streaming responses** — Use Ollama's streaming API to display results incrementally as they are generated

---

## 12. References

- [OllamaIntegrationIdea.md](OllamaIntegrationIdea.md) — Original idea document and design exploration
- [Specification.md](Specification.md) — Main TeamWare specification
- [SocialFeaturesSpecification.md](SocialFeaturesSpecification.md) — Social Features specification
- [ProjectLoungeSpecification.md](ProjectLoungeSpecification.md) — Project Lounge specification
- [Ollama API Documentation](https://github.com/ollama/ollama/blob/main/docs/api.md) — Ollama HTTP API reference
