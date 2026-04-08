# Edit Agent Repositories and MCP Servers - Ideas

This document is for brainstorming and discussion around adding edit support for an agent's existing repositories and MCP server configurations on the **Edit Agent** page. Currently, repositories and MCP servers can only be added or removed — there is no way to update an existing entry without deleting it and re-creating it.

---

## Context

The Edit Agent page (`/Admin/EditAgent/{userId}`) currently supports:

- **Add** — A collapsible form below each section to add a new repository or MCP server entry.
- **Remove** — A small inline form per row that posts to `RemoveAgentRepository` or `RemoveAgentMcpServer`.
- **No edit** — To change any field on an existing entry, the user must remove it and add a new one. This is especially painful when the entry has a secret (access token, auth header, environment variables) because there is no way to update a single field without re-entering all credentials.

The `AgentRepository` entity has: `ProjectName`, `Url`, `Branch`, `EncryptedAccessToken`, `DisplayOrder`.

The `AgentMcpServer` entity has: `Name`, `Type`, `Url`, `AuthHeader` (HTTP), `Command`, `Args`, `Env` (stdio), `DisplayOrder`.

All secret fields (`EncryptedAccessToken`, `AuthHeader`, `Env`) are encrypted at rest and currently masked in the table (e.g. `ghp_****1234`). They should continue to be treated as passwords throughout any edit experience — the server should never send a decrypted value to the browser.

---

## Idea 1: Inline Row Expansion (Expand-to-Edit)

The user clicks "Edit" on a table row and an edit form expands in place, pre-populated with the current non-secret values. Saving posts the form and redirects back to the same page — the same redirect pattern used by the existing Add and Remove actions. No navigation away from the Edit Agent page.

Secret fields would show the masked value as placeholder text (e.g. `ghp_****1234`) and include a "Clear" checkbox for explicitly removing a secret, consistent with how the Codex and Claude API key fields already work on this page. Leaving the secret input blank on save would mean "keep the current value."

The table already exists; each row would get an Edit button alongside the existing Remove button. Alpine.js (already used on the page) could manage which row's form is currently open.

### Decisions

- **One form at a time.** Only one edit form is open at a time. Opening a second row's form closes the first.
- **Preserve type-specific values.** If the admin changes the type (HTTP ↔ stdio) during an edit, existing field values are preserved in case they switch back before saving.
- **Disabled option for missing projects.** If the project a repository was originally linked to is no longer in `AvailableProjects`, it is shown as a disabled `<option>` pre-selected in the dropdown. No free-text fallback.
- **Scroll into view.** When an edit row expands, it scrolls into view automatically so all of its fields are visible.

---

## Idea 2: Dedicated Edit Page (Navigate-Away)

Clicking "Edit" on a row navigates to a separate full-page form (`/Admin/EditAgentRepository/{id}` or `/Admin/EditAgentMcpServer/{id}`). The form looks and behaves like the Add form but is pre-populated with the existing values. On save it redirects back to the Edit Agent page.

This is simpler to implement than inline expansion — it is just a new GET action (loads the entity into a view model), a new view (copy of the add form fields with pre-populated values), and a new POST action (update instead of insert). No Alpine.js state management needed for the expand/collapse behavior.

### Not Chosen

Idea 1 (Inline Row Expansion) was selected instead. The navigate-away approach was not chosen because keeping the admin in context on the Edit Agent page is preferable, even accounting for the additional Alpine.js state management required.

### Open Questions

- Is losing the Edit Agent page context a meaningful friction point, or is it acceptable given the infrequency of edits?
- Should the dedicated edit page share a partial view with the Add form fields to avoid duplicating markup, or is duplication acceptable here?
- Should validation errors on the edit page re-render the edit page (standard MVC pattern) or redirect back to Edit Agent with a `TempData` error message?

---

## Decisions

These decisions apply regardless of approach and were settled alongside the Idea 1 selection:

- **Secret field behavior on save:** Leaving the secret input blank means keep the existing encrypted value. No explicit "keep existing" checkbox is needed.
- **Clear secret UX:** Use the same "Clear …" checkbox pattern already present for Codex and Claude API keys to remain consistent across the page.
- **`DisplayOrder`:** Editing a row preserves the existing display order. No resequencing UI is needed.
- **Validation feedback:** Use the simpler `TempData["ErrorMessage"]` redirect pattern, consistent with the existing Add forms.
