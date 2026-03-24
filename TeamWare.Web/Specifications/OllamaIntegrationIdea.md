# Ollama AI Integration - Ideas

This document is for brainstorming and discussion around integrating a self-hosted Ollama instance with TeamWare. The goal is to identify practical use cases where a local LLM can enhance existing workflows without adding external service dependencies, consistent with TeamWare's "small team, self-hosted" philosophy.

---

## Context

TeamWare is deployed in a homelab environment alongside an Ollama instance. Ollama exposes a local HTTP API (typically at `http://<host>:11434`) that can run open-weight models (Llama 3.1, Mistral, Gemma, etc.) with no external API keys or cloud dependencies. This is a natural fit for TeamWare's self-hosted design.

### Key Constraints

- **Optional dependency** - Ollama integration must be entirely optional. TeamWare must function fully without it. If the Ollama endpoint is unreachable or unconfigured, all AI features are simply hidden or gracefully degraded.
- **Latency awareness** - Local LLM inference can take seconds, especially on consumer hardware. All AI operations should be asynchronous and never block the UI.
- **Model agnostic** - The integration should work with any model available in Ollama. A configurable model name (defaulting to something widely available like `llama3.1`) keeps things flexible.
- **No training or fine-tuning** - This is inference-only. TeamWare sends prompts and receives completions. No data leaves the local network.

---

## Idea 1: Content Rewriting and Improvement

### Problem
Users often write project descriptions, task descriptions, and comments quickly and informally. There is no built-in assistance for improving clarity, grammar, or completeness.

### Features
- **Rewrite project descriptions** - A button on the project edit form that sends the current description to Ollama and returns a polished version. The user reviews and accepts or discards the suggestion.
- **Rewrite task descriptions** - Same pattern for task edit forms. Useful for turning quick notes into clear requirements.
- **Improve comment drafts** - Before posting a comment, offer a "Polish" option that rewrites for clarity and tone.
- **Inbox item expansion** - When clarifying an inbox item, offer to expand a brief note into a fuller description suitable for a task.

### UX Pattern
A small "AI Rewrite" button appears next to text fields when Ollama is configured. Clicking it:
1. Shows a loading indicator (spinner or pulsing text).
2. Sends the current content to Ollama with an instruction prompt (e.g., "Rewrite the following project description to be clear, professional, and well-structured. Preserve the original meaning.").
3. Displays the result in a diff-like or side-by-side view.
4. The user clicks "Accept" to replace the current text, or "Discard" to keep the original.

---

## Idea 2: Task Breakdown and Subtask Suggestions

### Problem
Users create high-level tasks (e.g., "Build the reporting module") that are too large to act on directly. Breaking these down into smaller, actionable subtasks requires deliberate thought.

### Features
- **Suggest subtasks** - Given a task title and description, Ollama suggests a set of smaller subtasks.
- The user can review, edit, and selectively create the suggested subtasks.
- Context-aware: include the project description and existing tasks in the prompt so suggestions do not duplicate existing work.

### UX Pattern
A "Suggest Breakdown" button on the task detail page. Clicking it calls Ollama with the task context and displays a list of suggested subtasks. Each suggestion has a checkbox and editable title/description. The user selects which to create and clicks "Create Selected."

---

## Idea 3: Daily/Weekly Summary Generation

### Problem
Project leads and team members want a quick summary of what happened on a project over a given time period, but assembling this manually from activity logs, task changes, and comments is tedious.

### Features
- **Project activity summary** - Generate a human-readable summary of recent activity for a project (e.g., "This week: 5 tasks completed, 3 new tasks created, 2 tasks overdue. Key completions: API endpoint refactor, Database migration. Active discussion on Task #42.").
- **Personal daily digest** - Summarize what the current user accomplished today across all projects.
- **Review preparation** - Before a GTD review, generate a summary of unprocessed inbox items, stale tasks, and upcoming deadlines to help the user prepare.

### UX Pattern
A "Generate Summary" button on the project dashboard or the user's personal dashboard. The system gathers the raw activity data (from `ActivityLogService`, `ProgressService`, etc.), sends it to Ollama with an instruction to summarize, and displays the result. The summary could optionally be copied to clipboard or posted as a lounge message.

---

## Idea 4: Smart Inbox Triage

### Problem
Users capture items into their inbox quickly but postpone the clarification step. When they finally sit down to process the inbox, they face a list of cryptic one-liners with no context.

### Features
- **Suggest project assignment** - Based on the inbox item text and existing project names/descriptions, suggest which project the item likely belongs to.
- **Suggest priority** - Based on the item text and urgency-related keywords, suggest a priority level.
- **Draft description expansion** - Take a brief inbox item note (e.g., "fix login bug on mobile") and expand it into a more detailed task description.
- **Batch triage** - Process multiple inbox items at once, providing suggestions for each.

### UX Pattern
During the clarify step of an inbox item, Ollama suggestions appear as pre-filled defaults that the user can accept or override. A "Triage All" button processes all unprocessed inbox items and presents a summary view of suggestions.

---

## Idea 5: Standup/Status Report Generation

### Problem
If TeamWare adds async standups in the future (see PossibleFeatures.md, Feature #8), writing daily status updates can feel repetitive.

### Features
- **Auto-generate standup** - Based on the user's activity in the last 24 hours (tasks completed, tasks worked on, comments posted), draft a standup update.
- **Yesterday/Today/Blockers format** - Structure the output in the standard standup format.

### UX Pattern
A "Generate from activity" button in the standup input area. The user reviews and edits the generated standup before posting.

**Note:** Depends on the Standup feature being implemented first. Listed here for completeness.

---

## Technical Approach

### Configuration

Ollama settings are stored in the existing `GlobalConfiguration` key-value table, managed through the admin dashboard. This is consistent with how other site-wide settings (e.g., `ATTACHMENT_DIR`) are configured. There is no `appsettings.json` entry; the database is the single source of truth.

| Key | Example Value | Description |
|-----|---------------|-------------|
| `OLLAMA_URL` | `http://ollama-host:11434` | Base URL of the Ollama instance. When empty or absent, all AI features are disabled and hidden from the UI. |
| `OLLAMA_MODEL` | `llama3.1` | Model name to use for completions. Defaults to `llama3.1` if the key exists but the value is empty. |
| `OLLAMA_TIMEOUT` | `60` | Request timeout in seconds. Defaults to `60` if the key exists but the value is empty or non-numeric. |

**Enablement logic:** The integration is considered enabled when the `OLLAMA_URL` key has a non-empty value. There is no separate "Enabled" toggle. If the URL is blank or the key does not exist, AI buttons and UI elements are not rendered. This keeps the admin experience simple: paste a URL to enable, clear it to disable.

**Seeding:** The `OLLAMA_URL`, `OLLAMA_MODEL`, and `OLLAMA_TIMEOUT` keys are seeded with empty values (like `ATTACHMENT_DIR`) so they appear in the admin configuration list from first run. Descriptions explain their purpose. `OLLAMA_TIMEOUT` defaults to `60` seconds when empty.

**Runtime access:** Services read these values via `IGlobalConfigurationService.GetByKeyAsync`. Values can be cached in a short-lived memory cache (e.g., 60 seconds) to avoid a database round-trip on every AI-eligible page load.

### Service Layer

- `IOllamaService` / `OllamaService` - Thin wrapper around Ollama's HTTP API (`/api/generate` or `/api/chat`). Reads `OLLAMA_URL` and `OLLAMA_MODEL` from `IGlobalConfigurationService` (with caching). Returns a "not configured" result immediately if the URL is empty.
- `IAiAssistantService` / `AiAssistantService` - Higher-level service that constructs prompts, calls `IOllamaService`, and parses results. Each use case (rewrite, summarize, suggest, etc.) gets a dedicated method with an appropriate system prompt.
- Services are registered via DI unconditionally. They handle the disabled state internally by checking the configuration and short-circuiting when Ollama is not configured.

### Prompt Engineering

Each feature requires a carefully crafted system prompt. Prompts should:
- Include clear instructions on the expected output format.
- Provide relevant context (project name, existing tasks, etc.) but stay within token limits.
- Avoid leaking sensitive information beyond what is necessary for the specific request.

### UI Integration

AI features appear as progressive enhancements:
- Buttons/links are rendered only when Ollama is configured (check via `OLLAMA_URL` through a ViewComponent or tag helper that queries the cached configuration value).
- All AI calls are asynchronous. The UI shows a loading state and never blocks.
- Results are always presented as suggestions, not automatic changes. The user has final say.

### Error Handling

- If Ollama is unreachable, show a brief toast notification ("AI assistant unavailable") and hide the loading state.
- If the model returns an unusable response, show "Could not generate a suggestion. Try again or write manually."
- Never fail a primary action (saving a task, posting a comment) because of an AI error.

---

## Priority Ranking (Initial Assessment)

| Priority | Idea | Rationale |
|----------|------|-----------|
| High | 1 - Content Rewriting | Low complexity, high everyday value, works on existing text fields. Good proving ground for the integration. |
| High | 3 - Summary Generation | Leverages existing activity data, immediately useful for project leads. |
| Medium | 4 - Smart Inbox Triage | Enhances the GTD workflow, moderate complexity. |
| Medium | 2 - Task Breakdown | Useful but requires good prompt engineering to avoid generic suggestions. |
| Low | 5 - Standup Generation | Depends on unbuilt feature. |

---

## Decisions

| # | Topic | Decision |
|---|-------|----------|
| 1 | Model selection | A single `OLLAMA_MODEL` key in `GlobalConfiguration` is sufficient. The admin sets the model; all AI features use it. No per-feature or per-user model selection. |
| 2 | Rate limiting | Not needed for V1. The small-team deployment context makes saturation unlikely. Revisit if usage patterns change. |
| 3 | Prompt visibility | Prompts are not exposed to admins. They are hardcoded in `AiAssistantService`. Simplifies the initial implementation; can be reconsidered later. |
| 4 | Caching | No caching of prompt results for V1. Each request goes to Ollama fresh. Revisit if latency becomes a pain point on target hardware. |
| 5 | Audit trail | No special tagging or logging of AI-generated content. The user reviews and explicitly saves all AI suggestions, making the saved content the user's responsibility. Summaries are transient UI displays. |
| 6 | Timeout configuration | Configurable via `OLLAMA_TIMEOUT` key in `GlobalConfiguration`. Defaults to 60 seconds when empty. The `OllamaService` reads this value (with caching) and applies it as the `HttpClient` timeout per request. |

---

## Next Steps

- [x] Narrow focus to 5 core ideas (removed lounge summarization, tone check, smart search, review assistant, announcement drafting)
- [x] Resolve open questions (see Decisions table above)
- [ ] Decide which ideas make the cut for a first iteration
- [ ] Test Ollama API connectivity from the TeamWare deployment environment
- [ ] Prototype the `IOllamaService` wrapper with a simple rewrite endpoint
- [ ] Write a formal specification for the selected features
- [ ] Create an implementation plan with phases and GitHub issues
