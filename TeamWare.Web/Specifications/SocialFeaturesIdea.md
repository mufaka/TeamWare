# Social Features - Ideas

This document is for brainstorming and discussion around social and user-management features that are currently missing from TeamWare. Nothing here is final; the goal is to identify the gaps, explore options, and converge on a set of features worth specifying formally.

---

## Problem Statement

TeamWare currently has no way for users to discover each other outside the context of a specific project. There is also no system-level administration capability for managing the user population. Specifically:

1. **User Discovery** - A project owner who wants to invite someone (PROJ-04) has to already know their exact username or email. There is no directory, search, or browse capability.
2. **System Administration** - AUTH-04 says administrators can reset passwords and AUTH-05 defines roles, but the only roles that exist today (`ProjectRole`: Owner, Admin, Member) are scoped to individual projects. There is no system-wide "site admin" role, no admin dashboard, and no way to manage the user base (list users, disable accounts, promote/demote admins).
3. **User Visibility** - There is no sense of who is on the platform, who is active, or what anyone is working on unless you share a project with them.

---

## Idea 1: User Directory

A searchable directory of all registered users, available to any authenticated user. Since TeamWare targets home use and small teams (not public-facing deployments), all registered users are listed in the directory. There is no opt-out mechanism.

### Features
- List/search users by display name or email
- View a user profile page showing:
  - Display name, avatar, and email
  - List of projects the user belongs to (visible to anyone on the platform)
  - Task statistics (e.g., tasks assigned, tasks completed, tasks overdue)
  - Recent activity feed (task completions, comments, project joins)
- Link directly to "invite to project" from a user's profile

### Decisions
- The directory is visible to **all authenticated users**. No project-sharing prerequisite.
- There is **no opt-out**. If you have an account, you appear in the directory.
- Email, project membership, task stats, and recent activity are all visible on the profile page.

### Additional Decisions
- The profile page shows tasks from **all projects**, not just shared ones. Consistent with the open, small-team philosophy.
- The recent activity feed shows the **last 30 days** of activity.
- The directory list is **sortable by display name or email**. No additional filtering beyond the existing search.

---

## Idea 2: System-Level Admin Role

Introduce a site-wide administrator role that is separate from per-project roles.

### Features
- A site-wide admin role implemented via **ASP.NET Identity roles** (the conventional approach)
- Two tiers only: **Admin** and **User**
- An admin dashboard with:
  - List all users (with search/filter)
  - Disable or lock a user account
  - Reset a user's password (fulfills AUTH-04 properly)
  - Promote or demote users to/from site admin
  - View system-wide statistics (total users, total projects, total tasks)
- Admins can **view and edit any project**, regardless of project membership
- An **admin activity log** tracking administrative actions (account locks, password resets, role changes, project edits) with timestamps and the acting admin's identity
- Seed the first admin account at startup (the existing admin seed can be extended to assign the Admin role)

### Decisions
- Use **ASP.NET Identity roles** for the site admin role. No custom column on `ApplicationUser` or claims-based approach.
- **Two tiers only**: Admin and User. No moderator or other intermediate roles.
- Admins have **full access to all projects** (view and edit), in addition to user management capabilities.
- Admin actions are recorded in an **activity log** for accountability. The log captures what action was taken, who performed it, who/what it targeted, and when.

---

## ~~Idea 3: Team / Organization Concept~~

**Removed.** TeamWare targets small teams where the entire installation effectively *is* the team. Project-scoped membership is sufficient. Adding an explicit Team/Organization entity would be overengineering for the target audience.

---

## Idea 4: User Activity and Presence

Give users visibility into what is happening across the platform.

### Features
- "Last seen" or "last active" timestamp on user profiles
- A global activity feed (recent task completions, new projects, new members) visible on the dashboard
- Online/offline indicator powered by **SignalR**, laying the groundwork for future real-time features (e.g., live task updates, collaborative editing, chat)
- Activity is shown for **all projects**:
  - For projects the viewer **is a member of**: full activity detail (task title, description changes, comments, etc.)
  - For projects the viewer **is not a member of**: masked with a generic message showing only the activity type and project name (e.g., "completed a task in Project X", "added a comment in Project Y")

### Decisions
- Activity is visible across **all projects**, with a **masked/generic format** for non-member projects. This keeps the platform feeling connected without exposing specifics the viewer shouldn't see.
- Use **SignalR** for the online/offline presence indicator. This lays infrastructure groundwork for future real-time features beyond just presence.
- **No status messages or do-not-disturb** for now. Keep presence simple: online/offline.

---

## Idea 5: Member Invitation Improvements

The current flow for PROJ-04 (invite team members) could be made more social and discoverable.

### Features
- Autocomplete search when inviting members (pulls from user directory)
- **Invitation acceptance/decline workflow**: invitations are not instant additions; the invitee must accept or decline
- **Pending invitation list** visible to project owners/admins showing who has been invited and is awaiting a response
- Bulk invite (invite multiple users at once)
- A **notification** is sent to the user when they are invited to a project, using the existing notification system

### Decisions
- Invitations **require acceptance**. Project admins send an invite; the user sees a notification and accepts or declines. This replaces the current direct-add behavior.
- **Pending invitations are visible** to project owners and admins so they can track who hasn't responded yet.
- Users **receive a notification** when invited to a project. This ties into the existing NOTIF system and adds a new notification type (e.g., "ProjectInvitation").

---

## Priority Ranking (Initial Gut Feel)

| Priority | Idea | Rationale |
|----------|------|-----------|
| High | 2 - System Admin Role | Necessary for any real deployment. Cannot manage users without it. |
| High | 1 - User Directory | Unblocks user discovery, makes invitations practical. |
| Medium | 5 - Invitation Improvements | Better UX for the most common social action. |
| Low | 4 - Activity and Presence | Nice to have, not blocking anything. |
| Removed | 3 - Team/Organization | Project-scoped membership is sufficient for the target audience. |

---

## Next Steps

- [x] Discuss and refine the ideas above
- [x] Decide which ideas make the cut
- [ ] Determine if this becomes Phase 10 or is split across multiple phases
- [ ] Write formal specification entries (new requirements with IDs)
- [ ] Update the implementation plan with new work items and issues

---

## Deferred Ideas

The following ideas came up during brainstorming but are deferred to a future iteration:

- **Project Lounge** - A per-project persistent chat room powered by SignalR. Members could discuss work in real time, with @ mentions triggering notifications. Builds on the SignalR infrastructure from Idea 4. Deferred due to the significant scope of a full chat feature.
