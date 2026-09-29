# Technical Specification: DevOps Kanban Board for QA Tools (IIS / Windows Server)

## 1. Executive Summary & Architecture Overview
This system is an on-premises, lightweight Azure DevOps Boards clone designed to run natively under Internet Information Services (IIS) on Windows Server. It provides an unauthenticated web interface for internal developers and QA teams, coupled with a secured REST API for autonomous AI agents to query, create, update, document, and close work items.

### Core Stack
- **Backend**: ASP.NET Core 8 Web API running in-process via the ASP.NET Core Module v2 (`AspNetCoreModuleV2`) in IIS.
- **Database**: SQLite embedded database located at `App_Data/kanban.db` managed via Entity Framework Core with automatic WAL mode enabled.
- **Frontend**: Pure TypeScript Single Page Application bundled with Vite/Tailwind CSS, served directly as static assets from the ASP.NET Core `wwwroot` pipeline.
- **API Documentation**: OpenAPI / Swagger UI served at `/api/docs` and raw schema at `/api/openapi.json`.
- **Packaging Target**: Self-contained or framework-dependent IIS deployment package with a PowerShell deployment automation script (`deploy-iis.ps1`).

---

## 2. Directory Structure & Workspace Constraints
The application must adhere strictly to the following workspace layout:

```text
/
├── .gitignore
├── KanbanBoard.sln
├── deploy-iis.ps1                   # Automated IIS site provisioning & App_Data ACL script
├── package.json                     # Root tooling orchestration
├── src/
│   ├── Backend/
│   │   ├── KanbanBoard.Api/
│   │   │   ├── Controllers/         # REST API & Board Controllers
│   │   │   ├── Data/                # DbContext, Migrations, Seeders
│   │   │   ├── Middleware/          # API Key Auth & AI Identity Interceptors
│   │   │   ├── Models/              # Domain entities & DTOs
│   │   │   ├── Services/            # Board logic & history auditing
│   │   │   ├── appsettings.json
│   │   │   ├── appsettings.Production.json
│   │   │   ├── web.config           # IIS in-process handler definition
│   │   │   └── Program.cs
│   │   └── KanbanBoard.Tests/       # Unit & Integration tests
│   └── Frontend/
│       ├── index.html
│       ├── package.json
│       ├── vite.config.ts
│       ├── tailwind.config.js
│       └── src/
│           ├── assets/
│           ├── components/          # Kanban board, cards, modal, theme switcher
│           ├── services/            # API client
│           └── styles/              # WCAG 2.1 AA token styling
└── docs/
    └── qa/
        └── test_plan.md             # Human QA ELI5 verification guide

## **3\. Data Models & SQLite Schema (App\_Data/kanban.db)**

### **3.1 WorkItem**

* `Id` (INTEGER, Primary Key, Autoincrement)  
* `Title` (TEXT, Required, Max 255\)  
* `Description` (TEXT, Markdown supported)  
* `Type` (TEXT: `Bug`, `Feature`, `UserStory`, `Epic`, `Task`)  
* `State` (TEXT: `New`, `Active`, `Resolved`, `Closed`, `Removed`)  
* `Priority` (INTEGER: 1-Critical, 2-High, 3-Medium, 4-Low)  
* `Severity` (TEXT: `1 - Critical`, `2 - High`, `3 - Medium`, `4 - Low`)  
* `AssignedTo` (TEXT, Nullable)  
* `AreaPath` (TEXT, Default: `Tools\QA`)  
* `IterationPath` (TEXT, Default: `Current`)  
* `AiModified` (BOOLEAN, Default: `0`)  
* `AiAgentIdentity` (TEXT, Nullable \- captures AI agent name/model if updated via API)  
* `LastModifiedBy` (TEXT)  
* `CreatedAt` (DATETIME, UTC)  
* `UpdatedAt` (DATETIME, UTC)

### **3.2 WorkItemHistory (Audit Trail)**

* `Id` (INTEGER, Primary Key, Autoincrement)  
* `WorkItemId` (INTEGER, Foreign Key \-\> WorkItem.Id)  
* `ChangeDate` (DATETIME, UTC)  
* `Author` (TEXT)  
* `IsAiAction` (BOOLEAN)  
* `AgentName` (TEXT, Nullable)  
* `ChangedFieldsJson` (TEXT \- JSON payload of diffs)  
* `Comment` (TEXT, Nullable)

### **3.3 BoardColumn & Swimlane**

* Default Columns: `New` (WIP: unlimited), `Active` (WIP: 5), `Resolved` (WIP: 5), `Closed` (WIP: unlimited).  
* Supports WIP limit violations with high-contrast UI alerts.

## **4\. API Specification & AI Tagging Engine**

### **4.1 AI Agent Authentication & Tracking**

* **Header Authentication**: AI Agents must submit:  
  * `X-API-Key`: Pre-shared secret defined in `appsettings.json`.  
  * `X-Agent-Identity`: Identifying string (e.g., `Claude-Code-Agent-v1`, `Codex-Fixer`).  
* **Audit Rules**:  
  * Whenever an API endpoint modifies a card (`POST`, `PATCH`, `PUT`), the backend automatically marks `AiModified = true`, records `AiAgentIdentity = Request.Headers["X-Agent-Identity"]`, and logs an entry in `WorkItemHistory` with `IsAiAction = true`.  
  * The UI highlights AI-modified items with an accessible purple badge containing a robot glyph and tooltip: `"Updated by AI Agent: [AgentIdentity]"`.

### **4.2 Endpoints (`/api/v1/`)**

* `GET /api/v1/workitems` (Supports filtering by `type`, `state`, `aiModified`, `assignedTo`)  
* `GET /api/v1/workitems/{id}` (Returns details \+ full audit history)  
* `POST /api/v1/workitems` (Create bug or feature request)  
* `PATCH /api/v1/workitems/{id}` (Update fields: move state, update description, reassign)  
* `POST /api/v1/workitems/{id}/comments` (Add discussion comment or automated test output)  
* `GET /api/v1/board` (Returns board metadata, columns, WIP limits, and grouped items)  
* `GET /api/openapi.json` (OpenAPI 3.0 schema optimized for AI function calling / tools)

## **5\. User Interface & WCAG 2.1 AA Compliance**

### **5.1 Azure DevOps Board UX Elements**

* Drag-and-drop Kanban columns with real-time WIP limit validation.  
* Quick-filter toolbar: Filter by Work Item Type, Assigned To, State, or AI-modified status.  
* Card context modal: Full markdown editing, state transitions, and revision history stream.  
* Zero-authentication access: Direct URL access grants full browser manipulation.

### **5.2 Accessibility & Contrast (WCAG 2.1 AA)**

* Minimum 4.5:1 text-to-background contrast ratio across all states and UI badges.  
* Full keyboard operability: Move cards using focus keys (`Space` / `Enter` to select, `Arrow Keys` to move between columns).  
* `aria-live` announcements for column movements and WIP limit overages.

### **5.3 Theme Switcher**

* Fixed anchor: Bottom-right viewport (`position: fixed; bottom: 1rem; right: 1rem; z-index: 50;`).  
* Three-way toggle button group: **Light**, **Dark**, and **Auto** (system preference via `prefers-color-scheme`).  
* State stored in `localStorage` under `kanban_theme_preference`.

## **6\. IIS Hosting, SQLite Integrity & Deployment Packaging**

### **6.1 `web.config` Definition**

XML  
\<?xml version="1.0" encoding="utf-8"?\>  
\<configuration\>  
  \<location path="." inheritChildApplications="false"\>  
    \<system.webServer\>  
      \<handlers\>  
        \<add name="aspNetCore" path="\*" verb="\*" modules="AspNetCoreModuleV2" resourceType="Unspecified" /\>  
      \</handlers\>  
      \<aspNetCore processPath="dotnet" arguments=".\\KanbanBoard.Api.dll" stdoutLogEnabled="false" stdoutLogFile=".\\logs\\stdout" hostingModel="inprocess"\>  
        \<environmentVariables\>  
          \<environmentVariable name="ASPNETCORE\_ENVIRONMENT" value="Production" /\>  
        \</environmentVariables\>  
      \</aspNetCore\>  
      \<security\>  
        \<requestFiltering\>  
          \<hiddenSegments\>  
            \<add segment="App\_Data" /\>  
          \</hiddenSegments\>  
        \</requestFiltering\>  
      \</security\>  
    \</system.webServer\>  
  \</location\>  
\</configuration\>

### **6.2 SQLite Persistence & IIS Permissions**

* The SQLite file is placed at `App_Data/kanban.db`.  
* Hidden segment rule in `web.config` ensures `App_Data` cannot be downloaded over HTTP.  
* Automated PowerShell deployment script (`deploy-iis.ps1`) configures NTFS ACLs granting `Read & Execute`, `Write`, and `Modify` permissions to `IIS AppPool\<AppPoolName>` and `IUSR` on the `App_Data` directory.  
* Database connection string uses `Data Source=App_Data/kanban.db;Cache=Shared;Mode=ReadWriteCreate;`.

### **6.3 Packaging Delivery**

* The build produces a consolidated zip archive containing:  
  * Compiled binaries (`.dll`) and .NET in-process assets.  
  * Bundled frontend assets inside `wwwroot/`.  
  * Configured `web.config`.  
  * Initialized `App_Data` directory with empty migration seed.  
  * `deploy-iis.ps1` setup script for turnkey server deployment.

## **7\. Version Control Strategy**

* **Mode**: Local-Only Git repository.  
* **Workflow**: Feature delivery executed across isolated feature branches (`feature/backend-api`, `feature/sqlite-persistence`, `feature/board-ui`, `feature/accessibility-themes`, `feature/ai-integration`) merged locally into `main`.  
* **Protection**: Merges to `main` require all automated unit tests and integration smoke tests to pass cleanly.

