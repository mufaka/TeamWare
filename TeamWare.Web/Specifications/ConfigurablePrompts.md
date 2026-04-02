# Configurable AI Prompts - Ideas

This document is for brainstorming and discussion around making the AI prompts used throughout TeamWare configurable from the web application's admin interface. Currently, all prompts — both the agent system prompt and the Ollama-powered AI assistant prompts — are hard-coded in C# source files. Moving them into server-side configuration would allow administrators to tune AI behavior without redeploying code.

---

## Context

TeamWare uses AI prompts in two distinct areas:

### 1. Agent System Prompt (TeamWare.Agent)

The `DefaultSystemPrompt.Text` in `TeamWare.Agent/Pipeline/DefaultSystemPrompt.cs` defines the behavioral instructions for the Copilot Agent when it processes tasks. This prompt governs how the agent reads tasks, makes code changes, commits, and reports results. The `TaskProcessor` uses it as the system message for every Copilot session unless a custom `SystemPrompt` is set on the agent's `AgentIdentityOptions`.

The existing server-side agent configuration (`AgentConfiguration.SystemPrompt`) already allows setting a per-agent system prompt through the admin UI. However, this is a single free-text field with no structure, no templates, and no way to manage prompt evolution across multiple agents.

### 2. Ollama AI Assistant Prompts (TeamWare.Web)

The `AiAssistantService` in `TeamWare.Web/Services/AiAssistantService.cs` contains multiple hard-coded system prompts for different operations:

| Operation | Current Prompt Location |
|-----------|------------------------|
| Rewrite project description | `RewriteProjectDescription()` |
| Rewrite task description | `RewriteTaskDescription()` |
| Polish comment | `PolishComment()` |
| Expand inbox item | `ExpandInboxItem()` |
| Generate project summary | `GenerateProjectSummary()` |
| Generate personal digest | `GeneratePersonalDigest()` |
| Generate review preparation | `GenerateReviewPreparation()` |

These prompts are entirely static. To change the tone, style, or instructions for any AI feature, a developer must modify the source code and redeploy.

### Key Constraints

- **Backward compatible** — If no custom prompts are configured, the existing hard-coded defaults must continue to work unchanged.
- **Self-hosted philosophy** — No external prompt management services. Configuration lives in the TeamWare database and is managed through the existing admin UI.
- **Secure** — Prompt content is not secret, but only administrators should be able to modify prompts.
- **Consistent with existing patterns** — Follow the same EF Core entity, service, and UI patterns used throughout TeamWare (Tailwind CSS 4, HTMX, Alpine.js).

---

## Idea 1: Prompt Template Registry

### Problem

Prompts are scattered across multiple C# files with no central management. When an administrator wants to adjust the tone of AI-rewritten descriptions or change how the agent approaches tasks, they must either modify source code or (for the agent system prompt only) paste an entire replacement prompt into a single text field.

### Approach

Create a **prompt template registry** — a database-backed collection of named prompt templates that can be viewed and edited through the admin UI. Each prompt has a well-known key (e.g., `agent.system`, `assistant.rewrite-project`, `assistant.polish-comment`) and a text body.

#### Data Model

A new `PromptTemplate` entity:

| Field | Type | Description |
|-------|------|-------------|
| `Id` | int | Primary key |
| `Key` | string (unique, max 100) | Well-known identifier (e.g., `agent.system`) |
| `DisplayName` | string (max 200) | Human-readable name for the UI |
| `Category` | string (max 50) | Grouping: `Agent` or `Assistant` |
| `Description` | string (max 500) | Explanation of what this prompt is used for and when |
| `PromptText` | string (max 10000) | The actual prompt template text |
| `IsCustomized` | bool | `false` if using the built-in default, `true` if an admin has edited it |
| `CreatedAt` | DateTime | Record creation timestamp |
| `UpdatedAt` | DateTime | Last modification timestamp |

On first run (or via a migration seed), the system populates the table with default entries matching the current hard-coded prompts. The `IsCustomized` flag lets the system distinguish admin-edited prompts from defaults, enabling a "Reset to Default" action.

#### Service Layer

An `IPromptTemplateService` that:

- Retrieves a prompt by key, falling back to the hard-coded default if no database record exists.
- Lists all prompts (for the admin UI).
- Updates a prompt's text (admin only).
- Resets a prompt to its default (sets `IsCustomized = false` and restores the built-in text).

The `AiAssistantService` would take a dependency on `IPromptTemplateService` and load prompts by key instead of using inline strings.

#### Admin UI

A new "AI Prompts" section under the existing admin area:

- List view showing all prompt templates grouped by category (`Agent` / `Assistant`).
- Edit view with the prompt key (read-only), display name, description, and a large text area for the prompt body.
- A "Reset to Default" button that restores the built-in prompt.
- A "Preview" section showing a sample of what the prompt produces (optional, stretch goal).

---

## Idea 2: Per-Agent Prompt Templates

### Problem

The current `AgentConfiguration.SystemPrompt` field allows a single custom system prompt per agent. This works for simple overrides, but it has limitations:

- No structure or guidance — the admin pastes in a wall of text with no indication of what the default looks like or what sections are important.
- No inheritance — if the admin wants most agents to use a slightly customized version of the default prompt, they must copy-paste the full text into every agent's configuration.
- No versioning or diff — there is no way to see what changed or revert to a previous version.

### Approach

Extend the prompt template concept to support per-agent overrides:

1. The global `PromptTemplate` registry (from Idea 1) defines the base prompts.
2. Each `AgentConfiguration` can optionally reference a prompt template key and provide an override text.
3. Resolution order: **agent-specific override > global custom template > built-in default**.

A new `AgentPromptOverride` entity (or a simpler approach — just keep the existing `SystemPrompt` field but add a UI that shows the global default alongside it for reference).

The simpler version: on the Edit Agent page's configuration section, the `SystemPrompt` field is enhanced with:

- A read-only display of the current global default prompt (from the registry).
- A toggle: "Use global default" vs. "Use custom prompt for this agent."
- When custom is selected, the text area is pre-populated with the global default for editing.

This preserves the existing data model while giving administrators much better UX.

---

## Idea 3: Prompt Variables and Placeholders

### Problem

Some prompts benefit from dynamic context injection. For example, the agent system prompt might want to include the agent's name, the project it is working on, or the team's coding conventions. Currently, the prompt is static text with no variable substitution.

### Approach

Support simple placeholder variables in prompt templates using a `{{variable}}` syntax. Variables are resolved at runtime by the service consuming the prompt.

#### Agent Prompt Variables

| Variable | Resolved To |
|----------|-------------|
| `{{agent.name}}` | The agent's display name |
| `{{agent.description}}` | The agent's description from its user profile |
| `{{project.name}}` | The current project's name |
| `{{project.description}}` | The current project's description |
| `{{task.id}}` | The task ID being processed |
| `{{task.title}}` | The task title |

#### Assistant Prompt Variables

| Variable | Resolved To |
|----------|-------------|
| `{{user.name}}` | The current user's display name |
| `{{project.name}}` | The current project's name (when applicable) |

The variable resolution is a simple string replacement — no expression language or logic. Unknown variables are left as-is (or stripped) with a warning logged.

This would let an admin write prompts like:

```
You are {{agent.name}}, a coding agent working on the {{project.name}} project.
{{agent.description}}

When assigned a task:
1. Read the task details...
```

---

## Idea 4: Agent Prompt Delivery via MCP Profile

### Problem

Even with a prompt template registry on the server, the agent currently receives its system prompt through the `AgentConfiguration.SystemPrompt` field delivered via the `get_my_profile` MCP tool response. If prompts are managed centrally, they need to reach the agent through this same channel.

### Approach

Extend the existing config merge flow:

1. The `get_my_profile` MCP response already includes a `configuration.systemPrompt` field.
2. On the server side, when building the profile response, resolve the prompt for this agent: check for an agent-specific override first, then fall back to the global template, then to the built-in default.
3. Perform variable substitution (if Idea 3 is implemented) using the agent's profile data.
4. The agent's `ApplyServerConfiguration()` method already handles the `SystemPrompt` field — no agent-side changes needed for basic delivery.

For more advanced scenarios (e.g., per-project prompts), the MCP response could include a `prompts` dictionary keyed by prompt key, allowing the agent to use different prompts for different operations in the future.

---

## Idea 5: Prompt History and Audit Trail

### Problem

When an admin changes a prompt, there is no record of what the previous version was. If an agent starts behaving differently after a prompt change, there is no way to diff or revert.

### Approach

Add a `PromptTemplateHistory` entity that captures a snapshot every time a prompt is saved:

| Field | Type | Description |
|-------|------|-------------|
| `Id` | int | Primary key |
| `PromptTemplateId` | int | FK to `PromptTemplate` |
| `PromptText` | string | The prompt text at this point in time |
| `ChangedByUserId` | string | The admin who made the change |
| `ChangedAt` | DateTime | Timestamp |

The admin UI shows a "History" tab on each prompt template, displaying previous versions with timestamps and who changed them. A "Restore" button lets the admin revert to any previous version.

This is a nice-to-have that adds operational confidence but is not required for the initial implementation.

---

## Summary and Recommended Approach

The recommended path is:

1. **Start with Idea 1** (Prompt Template Registry) — This provides the core value: centralized, database-backed prompt management with an admin UI. All existing hard-coded prompts get entries in the registry. The `AiAssistantService` and `AgentConfiguration` resolution logic are updated to use the registry.

2. **Include Idea 2** (Per-Agent Prompt Templates) in the initial implementation as a UX enhancement — specifically the simpler version where the Edit Agent page shows the global default alongside the custom override field.

3. **Include Idea 4** (MCP Profile Delivery) — This is necessary for the agent to receive centrally managed prompts. The existing infrastructure already supports this; it is mostly a matter of resolving the prompt from the registry before building the MCP profile response.

4. **Defer Idea 3** (Prompt Variables) to a follow-up phase. It adds significant value but also complexity in variable resolution and documentation.

5. **Defer Idea 5** (Prompt History) to a follow-up phase. Useful for operational maturity but not essential for initial rollout.

### Dependencies

- This feature builds on the existing server-side agent configuration (Phases 44-48) and the Ollama AI assistant (Phases 22-25).
- No new external dependencies. Uses existing EF Core, ASP.NET Core, and the admin UI patterns.
- The agent-side changes are minimal since `ApplyServerConfiguration()` already handles `SystemPrompt`.
