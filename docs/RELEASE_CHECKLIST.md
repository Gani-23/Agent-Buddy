# Release Checklist (Unsigned Builds)

Use this list when shipping a new version.

## 1) Update version (optional)
- Update `AgentBuddy.csproj` version fields if you want them to change.

## 2) Create Git tag
```bash
git tag v1.0.X
git push origin v1.0.X
```

## 3) Wait for GitHub Actions
- Go to **Actions** tab and wait for **Build and Release** to finish.

## 4) Verify Release assets
- Open **Releases** → latest tag
- Ensure these assets exist:
  - `AgentBuddy-win-x64.zip`
  - `AgentBuddy-win-x86.zip`
  - `AgentBuddy-macos-osx-arm64.zip`
  - `AgentBuddy-macos-osx-x64.zip`
  - `AgentBuddy-linux-x64.tar.gz`
  - `AgentBuddy-linux-arm64.tar.gz`

## 5) Share install steps
- Point users to: `docs/UNSIGNED_INSTALL.md`
