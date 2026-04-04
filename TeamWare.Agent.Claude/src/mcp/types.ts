// Response types matching the TeamWare MCP tool contract.
// See: TeamWare.Agent/Mcp/ for the canonical C# definitions.

export interface AgentProfile {
  userId: string;
  displayName: string;
  email?: string;
  isAgent: boolean;
  agentDescription?: string;
  isAgentActive?: boolean;
  lastActiveAt?: string;
  configuration?: AgentProfileConfiguration;
}

export interface AgentProfileConfiguration {
  pollingIntervalSeconds?: number;
  model?: string;
  autoApproveTools?: boolean;
  dryRun?: boolean;
  taskTimeoutSeconds?: number;
  systemPrompt?: string;
  repositoryUrl?: string;
  repositoryBranch?: string;
  repositoryAccessToken?: string;
  repositories?: AgentProfileRepository[];
  mcpServers?: AgentProfileMcpServer[];
  agentBackend?: string;
  claudeApiKey?: string;
}

export interface AgentProfileRepository {
  projectName: string;
  url: string;
  branch?: string;
  accessToken?: string;
}

export interface AgentProfileMcpServer {
  name: string;
  type: string;
  url?: string;
  authHeader?: string;
  command?: string;
  args?: string[];
  env?: Record<string, string>;
}

export interface AgentTask {
  id: number;
  title: string;
  projectName?: string;
  projectId: number;
  status: string;
  priority: string;
  dueDate?: string;
  isOverdue: boolean;
  isNextAction: boolean;
}

export interface AgentTaskDetail {
  id: number;
  title: string;
  description?: string;
  status: string;
  priority: string;
  dueDate?: string;
  isNextAction: boolean;
  isSomedayMaybe: boolean;
  projectId: number;
  createdByUserId?: string;
  createdAt?: string;
  updatedAt?: string;
  assignees: AgentTaskAssignee[];
  comments: AgentTaskComment[];
}

export interface AgentTaskAssignee {
  userId: string;
  displayName?: string;
}

export interface AgentTaskComment {
  id: number;
  authorName?: string;
  content: string;
  createdAt?: string;
  updatedAt?: string;
}
