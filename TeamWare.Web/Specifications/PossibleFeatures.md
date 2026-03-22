# TeamWare - Possible Features

This document captures groupware-inspired feature ideas for future consideration. These features draw from classic groupware applications of the 80s and 90s (Lotus Notes/Domino, Novell GroupWise, FirstClass, etc.) and early 2000s intranets, reimagined for TeamWare's "small team, self-hosted, lightweight" philosophy.

---

## Feature Ideas

### 1. Project Lounge (Real-Time Chat)

**Heritage:** Lotus Notes "rooms," IRC channels, FirstClass conferences

The SignalR infrastructure (Phase 12) was explicitly designed as "foundational infrastructure for future features" — the spec even mentions "Project Lounge" by name. This is the natural next step:

- Per-project persistent chat rooms with message history
- `@mention` support that ties into the existing notification system
- Pinned messages for important announcements
- A **#general** global lounge for cross-project chatter
- Lightweight — think Zulip-style topics within a room, not full Slack

**Infrastructure leverage:** SignalR is already in place.

---

### 2. Team Wiki / Knowledge Base

**Heritage:** Lotus Notes document databases, early intranets, Microsoft SharePoint team sites

Classic groupware's killer feature was the shared structured document — Lotus Notes databases were essentially custom wikis before wikis existed.

- Per-project wiki with Markdown pages and a simple page tree
- A global wiki area for team-wide documentation (onboarding, runbooks, conventions)
- Page revision history with diff view
- Internal linking between wiki pages and tasks (`[[Task #42]]`)
- "Living document" — wiki pages can embed task status or project progress via HTMX partials

---

### 3. Bulletin Board / Announcements

**Heritage:** GroupWise bulletin boards, Lotus Notes broadcast databases, intranet homepages

Every 90s intranet had an announcement board. A modern take:

- Admin-level **site announcements** (pinned to dashboard, dismissible per user)
- Project-level **announcements** posted by owners/admins
- Announcement types: Info, Warning, Action Required
- Optional acknowledgment tracking ("Mark as read" with read receipts for admins)
- HTMX-powered toast/banner that appears on page load when new announcements exist

---

### 4. Shared Calendar

**Heritage:** Lotus Organizer, GroupWise calendar, Exchange shared calendars

Task due dates already exist, but there's no calendar view or team-level event concept:

- **Calendar view** of task deadlines, milestones, and events (month/week view)
- **Project milestones** — named date markers that aren't tasks but represent goals
- **Team events** — meetings, standups, retrospectives (simple: title, datetime, description, attendees)
- Calendar overlay showing all your projects' deadlines in one view
- iCal export for integration with external calendars

---

### 5. File Sharing / Document Library

**Heritage:** Lotus Notes attachments, Novell NetWare file shares, SharePoint document libraries

- Per-project file area for uploading and organizing documents
- Task-level file attachments
- Simple folder structure or tag-based organization
- File versioning (upload a new version, keep history)
- Preview for common formats (images, PDFs, Markdown, plain text)
- Storage quotas configurable by admin (keeps it self-hosted-friendly)

---

### 6. Polls / Quick Votes

**Heritage:** Lotus Notes "approval" workflows, GroupWise voting buttons

Classic groupware had "voting buttons" on emails. A modern lightweight take:

- Create a poll within a project (question + options)
- Anonymous or named voting
- Auto-close after deadline or after all members vote
- Results visible as a simple bar chart
- Tie into notifications: "New poll in Project X"
- Could double as **decision logs** — the poll result becomes a recorded team decision

---

### 7. Shared Bookmarks / Link Board

**Heritage:** Netscape bookmark sharing, intranet link pages, del.icio.us (early 2000s)

Every intranet had a curated links page. Modern version:

- Per-project and global bookmark collections
- Tags/categories for organization
- Auto-fetch page title and favicon (or let users provide them)
- Upvote/pin mechanism for most useful links
- Could integrate with wiki pages

---

### 8. Status Reports / Standup Log

**Heritage:** GroupWise "status tracking," weekly status email templates

The weekly review exists for personal GTD, but teams often need a shared status cadence:

- **Async standup**: Each member posts "Yesterday / Today / Blockers" entries
- Per-project standup log with history
- Dashboard widget: "Who has posted today?"
- Optional reminder notification if you haven't posted by a configurable time
- Weekly/monthly rollup view for project leads

---

### 9. Contact / Resource Directory (Extended)

**Heritage:** Lotus Notes Name & Address Book, LDAP directories, corporate yellow pages

The User Directory exists but could be enriched into a true "corporate directory":

- **Skills / expertise tags** on user profiles (searchable)
- **Availability status** beyond online/offline: "In a meeting," "Focused," "Out of office"
- **Custom status message** (like old-school AIM away messages)
- **Team/department grouping** for organizing users
- **Org chart** view showing project membership relationships

---

### 10. Internal Direct Messages

**Heritage:** Lotus Notes mail, GroupWise messaging, FirstClass messaging

Currently communication is task-scoped (comments) or broadcast (notifications). There's no private 1:1 channel:

- Direct messages between users
- Conversation threads (not single messages)
- Leverages existing SignalR for real-time delivery
- Unread count badge in the sidebar (like inbox count)
- Could optionally tie into the notification system

---

### 11. Shared Whiteboard

**Heritage:** Early collaborative tools, Microsoft NetMeeting whiteboard, Lotus Sametime

A real-time collaborative canvas for visual brainstorming and diagramming:

- Per-project whiteboards for sketching ideas, flowcharts, and diagrams
- Real-time multi-user drawing via SignalR (leverages existing infrastructure)
- Basic drawing tools: freehand, shapes, lines, arrows, text labels, sticky notes
- Color palette and simple styling options
- Export to PNG/SVG for archival or embedding in wiki pages
- Whiteboard snapshots saved to project history
- Could integrate with tasks — link whiteboard regions to tasks or create tasks from sticky notes

---

### 12. Collaborative Writing

**Heritage:** Lotus Notes shared documents, early Google Docs predecessors, SubEthaEdit, EtherPad

Real-time multi-user document editing for drafting proposals, meeting notes, and project documentation:

- Markdown-based collaborative documents within projects
- Real-time co-editing with presence cursors via SignalR
- Document version history with named snapshots and diff view
- Conflict-free merge using operational transformation or CRDT-based approach
- Comments and suggestions inline (similar to Google Docs review mode)
- Export to Markdown, HTML, or PDF
- Could complement or integrate with the Team Wiki — collaborative editing for wiki pages
- Templates for common document types: meeting notes, decision records, retrospectives

---

## Honorable Mentions (Lighter Lifts)

| Feature | Heritage | Modern Take |
|---------|----------|-------------|
| **Saved Searches** | Lotus Notes views | Save filtered task/project views as named bookmarks |
| **Custom Fields** | Notes forms | User-defined metadata fields on tasks (dropdown, text, date) |
| **Templates** | Notes design templates | Project templates with pre-built task structures |
| **Recurring Tasks** | GroupWise recurring events | Tasks that auto-recreate on a schedule |
| **Time Tracking** | Intranet timesheets | Log time against tasks, simple reports |
| **RSS/Atom Feeds** | Early 2000s intranets | Per-project activity feeds as RSS for external consumption |

---

## Suggested Priority

Based on existing infrastructure leverage and impact:

1. **Project Lounge** — SignalR is already there; highest bang-for-buck
2. **Team Wiki** — Fills the biggest content gap; complements tasks
3. **Bulletin Board** — Small scope, high visibility, admin feature
4. **Shared Calendar** — Task dates exist; this is mostly a view layer
5. **Polls** — Lightweight, fun, drives engagement
6. **Direct Messages** — Natural extension of Lounge + SignalR
7. **Shared Whiteboard** — Visual collaboration; SignalR infrastructure reuse
8. **Collaborative Writing** — Rich real-time editing; builds on wiki + SignalR
9. **File Sharing** — Common need but requires storage management decisions
10. **Status Reports** — Complements GTD review workflow
11. **Extended Directory** — Incremental enhancement to existing feature
12. **Shared Bookmarks** — Lightweight community feature
