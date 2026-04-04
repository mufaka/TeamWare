import type { TeamWareMcpClient } from "../mcp/client.js";

/**
 * Handles task status transitions with proper comments and lounge messages.
 * Mirrors StatusTransitionHandler from the .NET agent.
 *
 * Rules:
 * - Always post a comment before changing status
 * - Post lounge messages only for Blocked and Error
 * - Never set status to Done (only humans approve)
 * - Lounge messages are plain text, no icons/emoticons
 */
export class StatusTransitionHandler {
  constructor(private readonly mcp: TeamWareMcpClient) {}

  async pickUp(taskId: number): Promise<void> {
    await this.mcp.addComment(taskId, "Starting work on this task.");
    await this.mcp.updateTaskStatus(taskId, "InProgress");
  }

  async complete(taskId: number, summary: string): Promise<void> {
    await this.mcp.addComment(taskId, summary);
    await this.mcp.updateTaskStatus(taskId, "InReview");
  }

  async block(
    taskId: number,
    title: string,
    explanation: string,
    projectId: number,
  ): Promise<void> {
    await this.mcp.addComment(taskId, explanation);
    await this.mcp.updateTaskStatus(taskId, "Blocked");
    await this.mcp.postLoungeMessage(
      `I need help with Task #${taskId} \u2014 ${title}. I've posted a comment explaining what information I need. Can someone take a look?`,
      projectId,
    );
  }

  async error(
    taskId: number,
    title: string,
    errorDetails: string,
    projectId: number,
  ): Promise<void> {
    await this.mcp.addComment(taskId, errorDetails);
    await this.mcp.updateTaskStatus(taskId, "Error");
    await this.mcp.postLoungeMessage(
      `I ran into a problem on Task #${taskId} \u2014 ${title}. I've posted a comment with the error details. Someone will need to triage this.`,
      projectId,
    );
  }
}
