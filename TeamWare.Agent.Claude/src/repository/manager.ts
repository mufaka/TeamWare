import { execFile } from "child_process";
import { existsSync, mkdirSync } from "fs";
import { basename, join, resolve } from "path";
import { promisify } from "util";
import type { RepositoryConfig } from "../config.js";

const execFileAsync = promisify(execFile);

export interface ResolvedRepository {
  url: string;
  branch: string;
  accessToken?: string;
  workingDirectory: string;
}

/**
 * Resolves which repository to use for a task based on its project name.
 * Mirrors RepositoryManager and ResolveRepository from the .NET agent.
 */
export function resolveRepository(
  projectName: string | undefined,
  baseWorkingDirectory: string,
  repositories: RepositoryConfig[],
  fallbackUrl?: string,
  fallbackBranch?: string,
  fallbackAccessToken?: string,
): ResolvedRepository | null {
  // Try project-specific repository first
  if (projectName && repositories.length > 0) {
    const match = repositories.find(
      (r) => r.projectName.toLowerCase() === projectName.toLowerCase(),
    );
    if (match) {
      const safeName = sanitizeProjectName(projectName);
      return {
        url: match.url,
        branch: match.branch ?? "main",
        accessToken: match.accessToken,
        workingDirectory: join(baseWorkingDirectory, safeName),
      };
    }
  }

  // Fall back to flat repository config
  if (fallbackUrl) {
    return {
      url: fallbackUrl,
      branch: fallbackBranch ?? "main",
      accessToken: fallbackAccessToken,
      workingDirectory: baseWorkingDirectory,
    };
  }

  // No repository configured — agent works in base directory without git
  return null;
}

/** Sanitize project name to prevent path traversal. */
function sanitizeProjectName(name: string): string {
  // Take only the basename, strip path separators and ".."
  const safe = basename(name).replace(/\.\./g, "");
  if (!safe) {
    throw new Error(`Invalid project name: '${name}'`);
  }
  // Verify the result stays within the parent directory
  const test = resolve("/test", safe);
  if (!test.startsWith("/test/")) {
    throw new Error(`Invalid project name: '${name}'`);
  }
  return safe;
}

/**
 * Ensures the repository is cloned and up to date.
 * Mirrors RepositoryManager.EnsureRepositoryAsync from the .NET agent.
 */
export async function ensureRepository(repo: ResolvedRepository): Promise<void> {
  // Ensure parent directory exists
  mkdirSync(repo.workingDirectory, { recursive: true });

  const gitDir = join(repo.workingDirectory, ".git");
  const authenticatedUrl = buildAuthenticatedUrl(repo.url, repo.accessToken);

  if (existsSync(gitDir)) {
    // Update remote URL in case token changed
    await git(repo.workingDirectory, ["remote", "set-url", "origin", authenticatedUrl]);

    // Discard uncommitted changes from previous task processing (issue #284)
    await git(repo.workingDirectory, ["reset", "--hard", "HEAD"]);
    await git(repo.workingDirectory, ["clean", "-fd"]);

    // Fetch latest remote refs before checkout
    await git(repo.workingDirectory, ["fetch", "origin"]);

    // Switch to configured branch before pulling (issue #284).
    // -B force-creates/resets, handling diverged or missing local branches.
    await git(repo.workingDirectory, ["checkout", "-B", repo.branch, `origin/${repo.branch}`]);
  } else {
    await git(".", [
      "clone",
      "--branch", repo.branch,
      "--single-branch",
      authenticatedUrl,
      repo.workingDirectory,
    ]);
  }
}

function buildAuthenticatedUrl(url: string, accessToken?: string): string {
  if (!accessToken) return url;

  try {
    const parsed = new URL(url);
    parsed.username = accessToken;
    parsed.password = "";
    return parsed.toString();
  } catch {
    return url;
  }
}

async function git(cwd: string, args: string[]): Promise<string> {
  const { stdout } = await execFileAsync("git", args, { cwd });
  return stdout;
}
