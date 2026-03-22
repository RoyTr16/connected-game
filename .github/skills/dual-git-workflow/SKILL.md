---
name: dual-git-workflow
description: Strict guidelines for managing the split GitHub (code) and local Synology Gitea (LFS) architecture for the "Connected" project.
---

# Dual-Remote Git & LFS Workflow

## Infrastructure Overview
- **Primary Remote (`origin`)**: Hosted on GitHub. Used strictly for text, code, and lightweight configuration files.
- **Secondary Remote (`local-gitea`)**: Hosted locally on a Synology NAS. Used as a full backup and the EXCLUSIVE host for all Git LFS binary files.
- **Git LFS**: Configured via `.lfsconfig` to route all LFS traffic to `local-gitea`. Heavy files MUST NEVER be pushed to `origin`.

## Git Workflow Directives
1. **Pushing Code**: When pushing a branch (e.g., `main`), you must push to both remotes sequentially to keep them synced:
   `git push origin <branch>`
   `git push local-gitea <branch>`
2. **Binary Assets**: Any new binary assets (images, audio, Unity binaries) must be tracked by LFS. The `.gitattributes` file is already configured for standard Unity formats. Do not alter `.gitattributes` to remove LFS tracking for binary files.
3. **Unity Ignore Rules**: Never stage or commit files from auto-generated Unity directories, specifically `Library/`, `Temp/`, `Obj/`, `Logs/`, and `Builds/`. Rely on the existing `.gitignore`.
4. **No Force Pushing LFS to GitHub**: Never attempt to bypass `.lfsconfig` to push LFS objects to `origin`. GitHub's storage limits will break.