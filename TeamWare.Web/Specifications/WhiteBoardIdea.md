# TeamWare - Whiteboard Idea Document

## Overview

This idea document explores a simpler shared whiteboard concept for TeamWare. Instead of treating a whiteboard like a project-owned collaboration surface, this version treats it more like a temporary collaborative room with drawing tools.

Users would create whiteboards from a top-level **Whiteboards** area, invite other users to join, and collaborate around a single shared canvas. The interaction model is intentionally narrower than a full multi-editor whiteboard: many people may view a board at once, but only one person is the active presenter at any given time, and only the presenter can draw.

The goal of this document is to capture the simplified direction, identify tradeoffs, and surface the remaining questions that should be answered before a formal specification is written.

## Human-Provided Direction

The following inputs were provided by the human stakeholder and define the new direction for this idea:

1. Whiteboards should **not be tied to projects when created**.
2. A whiteboard should feel more like a **temporary chat room with a shared canvas** than a deeply structured project artifact.
3. The creator can **invite other users to join** using the existing notification system.
4. A whiteboard has **one, and only one, presenter** at a time.
5. The presenter defaults to the **creator** when the board is created.
6. The current presenter is the **only user allowed to draw**.
7. The owner can allow **one currently viewing user** to become presenter.
8. The owner can **take presenter back at any time**.
9. Saving a whiteboard is **optional**.
10. If a whiteboard is saved, it may be associated with **one project only**.
11. Unsaved whiteboards are visible to **invited users** and **site admins**.
12. If a whiteboard is saved to a project, **project visibility trumps invitations**. Users who are no longer members of the project cannot access it, even if they still have an invitation.
13. Creators are responsible for **deleting whiteboards** that are no longer needed.
14. The whiteboard should be **diagram-focused**, while still allowing **freehand drawing**.
15. If the owner of a saved board is removed from the associated project, ownership of that board should transfer to the **owner of the project**.
16. If a project is deleted, any boards associated with that project should also be **deleted**.

These points are treated here as the framing constraints for the revised idea.

## Problem Statement

TeamWare currently supports collaboration through projects, tasks, lounges, comments, and notifications. There is still room for a lightweight, visual, real-time space that supports sketching and discussion without requiring the heavier structure of a project workflow.

The revised whiteboard concept addresses that gap by offering a quick, shared canvas that users can spin up, invite others into, use for diagramming or sketching, and optionally persist to a project later if the result becomes worth keeping.

Potential use cases include:
- Quick architecture sketch sessions
- Ad hoc troubleshooting diagrams
- Brainstorming during a live discussion
- Network and software design walkthroughs
- Temporary planning sessions that may later become project artifacts

## Goals

- Keep the whiteboard model lightweight and easy to start
- Support real-time collaboration without allowing unrestricted simultaneous editing
- Make it easy to invite users into a board session
- Emphasize diagramming and sketching over broad document editing
- Allow a board to remain temporary unless the creator chooses to save it
- Allow optional project association only when a board is worth keeping
- Keep the feature accessible from the top-level navigation

## Non-Goals

- A full multi-user simultaneous drawing surface where everyone edits at once
- Deep integration with tasks, comments, lounges, or attachments in the first version
- Binding every board to a project from the moment it is created
- Building a long-lived archive or favorites system
- Supporting multiple presenters at the same time
- Replacing dedicated diagramming products

---

## Core Concept

The revised concept can be summarized as follows:

- A user creates a whiteboard from the top-level **Whiteboards** area
- The creator becomes the **owner** and also the initial **presenter**
- Other users may join the whiteboard after being invited
- Multiple users may watch the board at once
- Only the current presenter may draw or modify the canvas
- The owner may transfer presenter control to one currently viewing user
- The owner may take presenter control back at any time
- The board may remain temporary, or the creator may choose to save it and associate it with a single project

This creates a collaboration model that is live and shared, but still deliberately controlled.

---

## Collaboration Model

### Single-Presenter Whiteboard

The key interaction model is a shared-view, single-presenter whiteboard.

Possible characteristics:
- One canvas shared by all connected viewers
- One active presenter at a time
- Presenter actions update the board for all viewers in real time
- Non-presenters may observe but not draw
- Presenter status can change during a session

Potential advantages:
- Much simpler than true simultaneous editing
- Reduces edit conflicts substantially
- Fits presentation, walkthrough, and teaching scenarios well
- Keeps authorship clear during a live session

Potential concerns:
- Less suitable for sessions where many people expect to draw together
- Requires a clear and visible presenter handoff experience
- May frustrate users if presenter transfer feels slow or confusing

### Presenter Handoff

The human-provided direction requires that the owner can let one currently viewing user become presenter.

Important characteristics of this rule:
- The owner remains the authority over presenter assignment
- Only a user who is actively viewing the board can be promoted to presenter
- Only one presenter may exist at a time
- The owner can reclaim presenter status at any time

Open questions:
- The current presenter does not need a decline flow. The owner can assign presenter control directly and take it back if needed.
- Presenter handoff should be immediate with no confirmation step.
- The system should visibly show the owner and the current presenter as separate UI elements, even when they are the same user.
- The owner should be able to click an active user and assign presenter control through a context menu.

### Viewer Participation

Because the board behaves somewhat like a temporary room, people other than the presenter still need a role in the session.

Possible viewer capabilities to consider:
- View live canvas updates
- See who is currently presenting
- See who else is present
- Receive presenter control if the owner grants it
- Use a text chat sidebar alongside the board

Open questions:
- Lightweight reactions or pointer pings should be deferred to a future revision.
- Viewers should not have an explicit request-control feature in the initial version; they can ask through chat.
- The board should include text chat in a sidebar.

---

## Creation, Joining, and Invitations

### Creation Model

The new direction favors an intentionally lightweight create flow.

Possible create flow:
1. User opens the top-level **Whiteboards** page
2. User clicks **New Whiteboard**
3. User enters a title
4. The board is created immediately
5. The user enters as owner and initial presenter

Potential advantages:
- Very low friction
- Fits the temporary-room concept
- Encourages quick use during live collaboration

Potential concerns:
- Minimal metadata at creation time
- May produce many short-lived boards unless deletion is easy

### Invitation Model

The human-provided direction calls for invitations through the notification system.

Possible invitation flow:
- Owner opens the whiteboard
- Owner selects one or more users to invite
- Invited users receive notifications with a link to join the board

Potential advantages:
- Reuses an existing application pattern
- Makes board access feel intentional
- Helps keep live sessions targeted

Potential concerns:
- Depends on notification delivery and usability
- Needs a clear way to tell whether an invite is still relevant
- May be awkward for very spontaneous sessions if inviting is too slow

Open questions:
- Only the owner can send invitations.
- Invitation lifetime details matter less under the revised gated-access model and should be defined later if needed.
- Reopen behavior also depends on the final gated-access rules and does not need separate treatment at this stage.
- Access should be enforced on board page load as well as through landing-page visibility.

---

## Visibility and Discovery

### Landing Page Model

The landing page for whiteboards should be visible from the top-level navigation.

The current human-provided rule is:
- Show boards a user has been **invited to**
- Also show boards the user can access through **project membership** because they were saved to that project
- Site admins can see **all boards**

This implies two board visibility states:

| Board State | Who Can See It on Landing Page |
|-------------|--------------------------------|
| Unsaved / temporary | Invited users and site admins |
| Saved to project | Members of the associated project and site admins |

Potential advantages:
- Keeps access aligned with invitations for temporary boards
- Easy to find active rooms and recently relevant boards
- Saved boards become more structured without forcing project linkage up front

Potential concerns:
- The landing page may become noisy if many invitations accumulate
- Saved-board and invited-board access paths may need careful explanation
- Clear status cues will still be needed so users understand why they can see a board

Open questions:
- The landing page should emphasize active boards the user is invited to first, then recent boards.
- Temporary boards should show who created them.
- Saved and temporary boards do not need separate sections in the initial version.
- Invitations are access controls and discoverability paths; users should only see temporary boards they have been invited to, unless they are site admins.
- For saved boards, project membership overrides invitations.

### Deletion Model

The human-provided direction is that creators should delete boards that are no longer needed.

Possible implications:
- No archive concept
- No automatic cleanup unless later introduced
- Board lifecycle is lightweight and manual

Open questions:
- Owners and site admins can delete boards.
- Deletion should be immediate.
- Saved boards do not need special deletion rules beyond the standard confirmation dialog.
- Deleting a project should also delete any boards associated with that project.

---

## Optional Project Association

The revised concept keeps project association optional and secondary.

### Save to Project

The human-provided direction is:
- A whiteboard is not project-scoped when created
- The creator may optionally save the whiteboard
- If saved, it may be associated with one project only

Potential advantages:
- Keeps the initial whiteboard flow lightweight
- Lets temporary collaboration become structured only when useful
- Avoids over-modeling short-lived sessions

Potential concerns:
- The transition from temporary board to project-associated board needs to be clear
- Visibility rules change once a board is saved to a project
- Ownership and deletion rules may need to behave differently after saving

Open questions:
- Project association can be changed later by the owner only.
- Saving changes persistence and visibility, but does not otherwise redefine the board model.
- Saving should include a clear note that only project members will be able to see the board.
- Clearing project association should return the board to temporary status.
- If the owner of a saved board loses membership in the associated project, ownership should transfer to the project owner.

---

## Whiteboard Content Direction

The human-provided direction is to keep the whiteboard diagram-focused with support for freehand drawing.

### Diagram-Focused Canvas

Possible element types:
- Boxes and rectangles
- Circles and ellipses
- Lines and arrows
- Text labels
- Connectors
- Freehand pen drawing

Possible specialized diagram support:
- Software engineering diagram shapes
- Network diagram shapes

Potential advantages:
- Supports architecture and design discussions well
- More focused than a broad all-purpose canvas
- Easier to explain than a full whiteboard toolkit

Potential concerns:
- Specialized shape sets can expand scope quickly
- Diagramming UX can still become complex even with a narrowed goal
- Freehand and structured diagramming tools may need different interaction patterns

Open questions:
- The initial release should support standard circles, squares, text, lines, arrows, and the following specialized shapes: servers, desktops, mobile devices, data, switch, router, firewall, and cloud.
- Freehand drawing and diagram tools should be treated as equal modes; users explicitly switch modes.
- Only the presenter can manipulate the board, including editing existing shapes.

---

## Persistence Model

The revised direction suggests a lightweight persistence approach.

Possible interpretation:
- Temporary boards exist until deleted
- Saving a board means it should persist as a project-associated board
- The latest state of the canvas is sufficient

Potential advantages:
- Simpler than full versioning
- Aligns with the temporary-room concept
- Reduces data-model complexity

Potential concerns:
- No recovery path if a board is accidentally changed or deleted
- Temporary boards may linger if creators forget to clean them up

Open questions:
- Temporary boards should autosave while in use.
- The only intended difference between unsaved and saved boards is project association and the resulting viewership rules.
- Saved boards should display last updated time in the user's local time.

---

## Presence and Real-Time Behavior

This idea depends heavily on live collaboration, even though drawing is restricted to one presenter.

Possible real-time features:
- Live board updates from the presenter to all viewers
- Presence list showing who is currently viewing
- Clear presenter indicator
- Owner controls for presenter reassignment
- Viewer list above the chat area, with presenter badge shown in that list
- Owner and presenter displayed prominently above the board
- Side panel showing active users, with chat below that list

Potential advantages:
- Preserves the feeling of a live collaborative room
- Makes handoff and observation understandable
- Keeps the feature dynamic without allowing uncontrolled simultaneous edits

Potential concerns:
- Requires dependable real-time connection behavior
- Presenter transfer must avoid race conditions
- The system must distinguish between invited users, authorized users, and currently present viewers

Open questions:
- If the presenter disconnects, the board stays active for the other users in the room.
- Presenter control does not automatically return; the owner can manually take back control.
- Any presenter must be actively viewing the board in order to present.
- SignalR is the expected realtime transport for this feature.
- Viewer badges are not needed in the active-user list; owner and presenter names should instead be shown prominently above the board.

---

## Security and Access Considerations

The revised model is simpler than the earlier project-scoped direction, but it still has important access questions.

Areas to consider:
- Who is allowed to open an unsaved board under the invite-gated model
- Invitation as access control rather than just a notification
- Whether site admins have universal visibility or management rights
- How saved-to-project boards should respect project membership changes over time

Open questions:
- Invitations should gate entry to unsaved boards.
- Invitations are access controls and also provide discoverability through the linked notification.
- Site admins should be able to see and manage all boards.
- Invitations should remain until declined or until the board is deleted.
- If a user follows an invite to a deleted board, the invite should be removed and the user should be informed that the board is no longer available.
- If a user has an invitation to a saved board but is no longer part of the associated project, they should be told they no longer have permission and the invitation should be deleted.
- Temporary-board active-user context menus should support **Make Presenter** and **Remove User**. Removing a user should revoke their invitation and return them to the whiteboard landing page.

---

## Potential MVP Shape

One possible MVP interpretation of the current direction would include:

- Top-level **Whiteboards** landing page
- Lightweight board creation with title only
- Owner and single-presenter model
- Real-time viewing for multiple users
- Presenter-only drawing and editing
- Invitation via notifications
- Chat sidebar
- Optional save-to-project action
- Diagram tools plus freehand drawing
- Manual deletion by owner or site admin

This list is included to clarify the rough scope implied by the current direction, not to replace a formal specification.

---

## Specification-Phase Clarifications

The previously open questions in this area now have stakeholder-provided answers:

1. Project visibility trumps invitations. If a user is no longer part of the project for a saved board, they cannot access that board even if they still hold an invitation. Attempting to use such an invitation should delete it and inform the user that they no longer have permission.
2. The owner's context menu for an active user should contain **Make Presenter** and, for temporary boards, **Remove User**. Removing a user should revoke the invitation and return that user to the whiteboard landing page.
3. Viewer badges are not needed. The owner and current presenter should instead be shown prominently above the board, while the side panel lists active users and chat.
4. Automated stale-board cleanup may be added later via Hangfire, but it is out of scope for this idea document.

Additional edge cases to carry into the later specification:

1. If a saved board's owner is removed from the associated project, ownership transfers to the owner of that project.
2. If a project is deleted, all boards associated with that project are deleted as well.

## Next Step Suggestions

Possible next steps before writing a formal specification:
- Define the owner, presenter, and viewer rules precisely in UI and authorization terms
- Sketch the landing page, board session page, invite flow, and presenter handoff UI
- Define the initial diagram toolset and freehand drawing capabilities
- Clarify the lifecycle difference between temporary boards and saved-to-project boards
- Note stale temporary board cleanup as a possible future Hangfire enhancement, but keep it out of the initial scope

This document is intentionally exploratory. Final decisions should be captured in a dedicated specification once the remaining behavior and access details are resolved.
