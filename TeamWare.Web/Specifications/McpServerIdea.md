# MCP Server Integration - Ideas

This document is for brainstorming and discussion around exposing a Model Context Protocol (MCP) server from TeamWare via HTTP. The goal is to let external AI agents and MCP-compatible clients (VS Code Copilot, Claude Desktop, custom agents, etc.) interact with TeamWare data and operations through a standardized protocol, consistent with TeamWare's "small team, self-hosted" philosophy.

---

## Context

The [Model Context Protocol](https://modelcontextprotocol.io) (MCP) is an open standard that defines how AI applications discover and invoke tools, read resources, and execute prompts exposed by a server. By hosting an MCP server within TeamWare, external AI agents gain structured access to project data, task management, inbox processing, and more - without screen-scraping or custom API integrations.

TeamWare already integrates with a local Ollama instance for AI-assisted features (content rewriting, summary generation). An MCP server is a natural complement: Ollama powers internal AI features, while MCP exposes TeamWare's capabilities to external AI agents.

### Key Constraints

- **Optional feature** - The MCP server endpoint must be entirely optional. TeamWare must function fully without any MCP clients connected. The endpoint can be enabled or disabled through configuration.
- **Authentication required** - MCP endpoints must be protected. Anonymous access to project data and task operations is not acceptable. The MCP SDK supports `[Authorize]` attributes on tools, prompts, and resources.
- **Read-heavy by default** - Initial tools and resources should emphasize read operations (listing projects, viewing tasks, reading activity). Write operations (creating tasks, updating status) should be introduced carefully with appropriate authorization.
- **Self-hosted alignment** - No external service dependencies. The MCP server runs in-process within the existing TeamWare ASP.NET Core application.
- **Existing service reuse** - MCP tools should delegate to existing service interfaces (`IProjectService`, `ITaskItemService`, `IInboxService`, etc.) rather than duplicating business logic.

---

## Idea 1: Project and Task Resources

### Problem
AI agents working alongside a developer have no structured way to access TeamWare project and task data. The agent cannot answer questions like "What tasks are assigned to me?" or "What is the status of Project X?" without the user manually copying information.

### Features
- **List projects** - Expose all projects the authenticated user has access to as MCP resources.
- **Project details** - Return project name, description, status, member count, and recent activity.
- **List tasks** - Return tasks for a given project, filterable by status, assignee, and priority.
- **Task details** - Return full task information including description, comments, subtasks, and history.
- **My assignments** - Return all tasks assigned to the authenticated user across all projects.

### MCP Concept
These would be exposed as **MCP Resources** (read-only data) and/or **MCP Tools** (callable functions that accept parameters like project ID, filters, etc.).

---

## Idea 2: Task Management Tools

### Problem
When an AI agent identifies work to be done (e.g., from a code review or conversation), there is no way to create or update tasks in TeamWare without leaving the AI tool and switching to the browser.

### Features
- **Create task** - Create a new task in a specified project with title, description, priority, and optional assignee.
- **Update task status** - Move a task through workflow states (e.g., To Do, In Progress, Done).
- **Assign task** - Assign or reassign a task to a team member.
- **Add comment** - Post a comment on a task.
- **Create subtask** - Break a task into subtasks.

### MCP Concept
These would be **MCP Tools** with parameters. Each tool delegates to the corresponding TeamWare service. Write operations require authentication and appropriate project membership.

---

## Idea 3: Inbox and GTD Tools

### Problem
A user working in an AI-assisted environment (e.g., coding with Copilot) may want to quickly capture thoughts or tasks into their TeamWare inbox without context-switching to the browser.

### Features
- **Capture to inbox** - Add a new item to the authenticated user's inbox with a title and optional note.
- **List inbox items** - Return unprocessed inbox items.
- **Clarify inbox item** - Update an inbox item with project assignment, priority, and expanded description.
- **Process inbox item** - Convert an inbox item to a task or mark it as not actionable.

### MCP Concept
**MCP Tools** that wrap the existing `IInboxService`. The capture tool is particularly valuable as a low-friction way to get items into the GTD workflow from any AI client.

---

## Idea 4: Activity and Summary Resources

### Problem
AI agents lack context about what has been happening in a project. Without recent activity data, they cannot make informed suggestions or generate status updates.

### Features
- **Recent activity** - Return the activity log for a project or for the authenticated user, covering a configurable time window.
- **Project summary** - Return a structured summary of project health: task counts by status, overdue items, recent completions.
- **My dashboard data** - Return the same data shown on the user's personal dashboard: assigned tasks, upcoming deadlines, unread notifications.

### MCP Concept
These are primarily **MCP Resources** that return structured data. An AI agent can use this data to generate reports, answer questions, or proactively surface issues.

---

## Idea 5: AI-Assisted Prompts

### Problem
Users interacting with AI agents often need to provide context about their TeamWare workflow. Repeating project conventions, task formats, and team practices in every conversation is tedious.

### Features
- **Project context prompt** - An MCP Prompt that gathers the project description, conventions, and recent tasks, and formats them as context for an AI conversation.
- **Task breakdown prompt** - A Prompt that takes a high-level task description and produces a structured prompt asking the AI to suggest subtasks, referencing existing project tasks to avoid duplication.
- **Standup prompt** - A Prompt that gathers the user's recent activity and formats it as a standup report draft.

### MCP Concept
**MCP Prompts** are reusable prompt templates that MCP clients can discover and invoke. They differ from Tools in that they return prompt messages rather than executing actions.

---

## Idea 6: Lounge Integration

### Problem
Project Lounge messages are a key communication channel, but AI agents have no way to read or post messages.

### Features
- **Read channel messages** - Return recent messages from a lounge channel.
- **Post message** - Send a message to a lounge channel on behalf of the authenticated user.
- **Search messages** - Search lounge messages by keyword or date range.

### MCP Concept
**MCP Tools** for write operations (posting), **MCP Resources** for read operations (listing/searching). Channel access should respect existing membership rules.

---

## Technical Approach

### NuGet Packages

The official C# MCP SDK provides ASP.NET Core integration:

| Package | Purpose |
|---------|---------|
| `ModelContextProtocol` | Core MCP server abstractions, tool/resource/prompt attributes |
| `ModelContextProtocol.AspNetCore` | HTTP transport, `AddMcpServer()`, `MapMcp()`, authorization filters |

### Hosting Model

The MCP server runs in-process within the existing TeamWare web application. No separate process or port is needed. The MCP endpoint is mapped to a dedicated route (e.g., `/mcp`) alongside the existing MVC routes.

```csharp
// In Program.cs or startup configuration
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .AddAuthorizationFilters()
    .WithToolsFromAssembly();

// After existing middleware
app.MapMcp("/mcp");
```

This serves both the Streamable HTTP protocol (modern clients connect to `/mcp`) and legacy SSE (clients connect to `/mcp/sse`) automatically.

### Authentication and Authorization

MCP tools and resources are protected using the same ASP.NET Core Identity authentication that protects the rest of TeamWare. The MCP SDK supports `[Authorize]` and `[AllowAnonymous]` attributes on tool classes and methods.

External AI clients (VS Code Copilot, Claude Desktop, etc.) cannot perform interactive browser login, so **Personal Access Tokens (PATs)** are used for authentication. This is a prerequisite feature that must be built in Phase A.

**PAT design:**
- Users generate tokens in their profile settings page. Each token has a name (for the user's reference) and an optional expiration date.
- Tokens are stored as hashed values in a new `PersonalAccessToken` table linked to `ApplicationUser`.
- The raw token is shown once at creation time and never again.
- Clients send the token as a Bearer token in the `Authorization` header.
- A custom ASP.NET Core authentication handler validates the token, resolves the associated user, and establishes a `ClaimsPrincipal` - making the MCP request indistinguishable from a regular authenticated session for authorization purposes.

### Configuration

Similar to the Ollama integration, MCP server settings would be stored in the `GlobalConfiguration` table:

| Key | Example Value | Description |
|-----|---------------|-------------|
| `MCP_ENABLED` | `true` | Whether the MCP endpoint is active. When `false`, the `/mcp` route returns 404. |
| `MCP_REQUIRE_AUTH` | `true` | Whether authentication is required. Should default to `true` and be difficult to set to `false`. |

### Tool Organization

MCP tools are organized into classes by domain, each delegating to existing services:

```
TeamWare.Web/
  McpTools/
    ProjectTools.cs       -> IProjectService
    TaskTools.cs          -> ITaskItemService
    InboxTools.cs         -> IInboxService
    ActivityTools.cs      -> IActivityLogService
    LoungeTools.cs        -> ILoungeMessageService
  McpPrompts/
    ProjectContextPrompt.cs
    TaskBreakdownPrompt.cs
  McpResources/
    ProjectResource.cs
    DashboardResource.cs
```

Each tool class uses constructor injection to receive the relevant service:

```csharp
[McpServerToolType]
[Authorize]
public class ProjectTools
{
    [McpServerTool, Description("Lists all projects the authenticated user is a member of.")]
    public static async Task<string> ListProjects(
        IProjectService projectService,
        IMcpServerContext context,
        CancellationToken cancellationToken)
    {
        // Resolve current user from context
        // Delegate to projectService
        // Return JSON result
    }
}
```

---

## Decisions

1. **Authentication mechanism** - **Personal Access Tokens (PATs).** Users generate tokens in their profile settings. Tokens are sent as Bearer tokens in the Authorization header. This requires a new feature (token generation, storage, validation) as a prerequisite in Phase A.

2. **Granular permissions** - **Reuse existing project membership roles.** An MCP client operates as the authenticated user. The same authorization logic that governs the web UI applies to MCP tool calls. An Observer cannot create tasks via MCP just as they cannot via the browser.

3. **Rate limiting** - **No rate limiting.** TeamWare targets small teams in a self-hosted environment. The overhead and complexity of rate limiting is not justified.

4. **Hosting model** - **In-process.** The MCP server runs within the existing TeamWare ASP.NET Core application. No separate process, port, or API layer. This is the simplest approach and appropriate for a homelab deployment.

5. **Tool discoverability** - **Expose all tools to all authenticated users.** Filtering the tool list by role or membership adds complexity and can cause issues with MCP clients that cache the tool list. Authorization is enforced at invocation time, not at discovery time.

6. **Ollama via MCP** - **No.** The calling agent will generally have access to a better LLM than the local Ollama instance. Exposing Ollama through MCP adds complexity without meaningful value.

7. **Phasing** - Confirmed:
   - Phase A: NuGet packages, endpoint setup, PAT authentication
   - Phase B: Read-only tools (list projects, list tasks, my assignments)
   - Phase C: Write tools (create task, update status, capture to inbox)
   - Phase D: Prompts and resources
   - Phase E: Lounge integration, advanced features

---

## References

- [Model Context Protocol Specification](https://spec.modelcontextprotocol.io)
- [C# MCP SDK Documentation](https://csharp.sdk.modelcontextprotocol.io)
- [ModelContextProtocol NuGet Package](https://www.nuget.org/packages/ModelContextProtocol)
- [ModelContextProtocol.AspNetCore NuGet Package](https://www.nuget.org/packages/ModelContextProtocol.AspNetCore)
