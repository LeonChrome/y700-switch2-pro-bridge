# Token Hygiene

Date: 2026-06-06

Do not paste GitHub tokens, personal access tokens, API keys, or bearer
tokens into chat, README files, scripts, commits, issues, pull requests, or
logs.

If a token is exposed:

1. Revoke it immediately in GitHub settings.
2. Create a new least-privilege token only for the required repository and
   operation.
3. Prefer environment variables or GitHub CLI authentication.
4. Scan the worktree and git history before committing.

Recommended local auth methods:

```powershell
$env:GITHUB_TOKEN = "<redacted>"
gh auth login
```

Suggested scans from the repository root:

```powershell
rg -n "ghp_|github_pat_|GITHUB_TOKEN|Authorization:" README.md CHANGELOG.md docs firmware tools
rg -n "token" README.md CHANGELOG.md docs firmware tools
git log -S"ghp_" --all
git log -S"github_pat_" --all
git log -S"GITHUB_TOKEN" --all
```

Plain documentation hits for the word `token` are not secrets. When reporting a
real finding, include only the file and commit. Never repeat the token value in
logs.

Current scan summary:

```text
actual_worktree_ghp=false
actual_worktree_github_pat=false
actual_worktree_authorization_bearer=false
actual_worktree_token_value=false
history_ghp=false
history_github_pat=false
history_GITHUB_TOKEN=false
action_required=revoke previously exposed token in GitHub settings
```
