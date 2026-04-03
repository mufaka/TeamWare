import type { AgentTaskDetail } from "../mcp/types.js";

/**
 * Builds the task prompt sent to the Codex LLM session.
 * Mirrors the prompt construction in TaskProcessor.cs from the .NET agent.
 */
export function buildTaskPrompt(task: AgentTaskDetail, projectName?: string): string {
  const lines: string[] = [];

  lines.push(`Task #${task.id}: ${task.title}`);
  if (projectName) {
    lines.push(`Project: ${projectName}`);
  }
  lines.push(`Priority: ${task.priority}`);
  lines.push(`Status: ${task.status}`);

  if (task.description) {
    lines.push("");
    lines.push("Description:");
    lines.push(task.description);
  }

  if (task.comments.length > 0) {
    lines.push("");
    lines.push("Comments:");
    for (const comment of task.comments) {
      const timestamp = comment.createdAt ?? "unknown time";
      const author = comment.authorName ?? "unknown";
      lines.push(`[${timestamp}] ${author}: ${comment.content}`);
    }
  }

  return lines.join("\n");
}
