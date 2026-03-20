# General Idea 
This document is to be used for collaboration and discussion of the general idea of the project. It is not meant to be a detailed specification, but rather a high-level overview of the project and its goals.

## Project Overview
TeamWare is a web application that is intended to be used by small teams to manage their projects and tasks. The application will allow users to create projects, add tasks to those projects, and assign those tasks to team members. The application will also include features for tracking progress, setting deadlines, and communicating with team members. It is expected that other functionality will be added as the project progresses, but the core features will be focused on project and task management.

### UI 
The User Interface (UI) must be modern, intuitive, and user-friendly. It should be designed to facilitate easy navigation and efficient task management. The UI should also be responsive, ensuring that it works well on both desktop and mobile devices. Support for dark mode is also a requirement, allowing users to switch between light and dark themes based on their preferences. The design should prioritize clarity and simplicity, making it easy for users to understand and use the application effectively.

### Technology Stack

- ASP.NET Core for the backend
- HTMX, Alpine.js, and Tailwind CSS 4 for the frontend
- SQLite for the database
- Microsoft Identity for authentication and authorization

### AI Instructions
- No use of emoticons or emojis in the user interface or documentation.
- One type per file. Do not create multiple types in a single file. Each type should be in its own file for better organization and maintainability.
- Mandatory test cases for all new features. Every new feature must have corresponding test cases to ensure that it works as expected and to prevent regressions in the future.