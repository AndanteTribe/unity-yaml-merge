# Unity YAML Merge
[![dotnet-test](https://github.com/AndanteTribe/unity-yaml-merge/actions/workflows/dotnet-test.yml/badge.svg)](https://github.com/AndanteTribe/unity-yaml-merge/actions/workflows/dotnet-test.yml)
[![docker-publish](https://github.com/AndanteTribe/unity-yaml-merge/actions/workflows/docker-publish.yml/badge.svg)](https://github.com/AndanteTribe/unity-yaml-merge/actions/workflows/docker-publish.yml)
[![GitHub Actions](https://img.shields.io/badge/GitHub%20Actions-Marketplace-2088FF?logo=githubactions)](https://github.com/marketplace/actions/unity-yaml-merge)
[![Docker Hub](https://img.shields.io/docker/v/andantetribe/unity-yaml-merge?label=Docker%20Hub&logo=docker&sort=semver)](https://hub.docker.com/r/andantetribe/unity-yaml-merge)
[![Docker Hub](https://img.shields.io/docker/v/andantetribe/unity-yaml-merge-git?label=Docker%20Hub&logo=docker&sort=semver)](https://hub.docker.com/r/andantetribe/unity-yaml-merge-git)
[![GitHub license](https://img.shields.io/github/license/AndanteTribe/unity-yaml-merge.svg)](./LICENSE)

English | [日本語](README_JA.md)

## Overview

**Unity YAML Merge** is a Docker-based wrapper around UnityYAMLMerge for Unity text-serialized assets.

This repository currently provides the following artifacts:

| Artifact | Name | Summary |
| --- | --- | --- |
| GitHub Actions Marketplace | `AndanteTribe/unity-yaml-merge` | Git-oriented automation for resolving conflicted Unity YAML files in CI. |
| Docker image | `andantetribe/unity-yaml-merge` | Standalone merge runner when you already have `base`, `ours`, `theirs`, and `output` paths. |
| Docker image | `andantetribe/unity-yaml-merge-git` | Git-aware merge runner that detects conflicted Unity files and can optionally push the result. |

## Quick Start

### GitHub Actions Marketplace

Recommended triggers are `push` and `workflow_dispatch`.

`pull_request` cannot be used for the main conflict-resolution workflow because GitHub does not run that event for pull requests that are already in a conflicted state. `pull_request_target` can run in that situation, but GitHub documents important security risks around it, so this repository does not recommend it as the default approach.

```yaml
name: unity-yaml-merge

on:
  push:
    branches:
      - feature/**
  workflow_dispatch:

jobs:
  merge:
    runs-on: ubuntu-latest
    permissions:
      contents: write
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - uses: AndanteTribe/unity-yaml-merge@v1
        with:
          auto-push: true
          github-token: ${{ secrets.GITHUB_TOKEN }}
          base-branch: main
          project-path: .
          unity-version-source: project
          target-extensions: unity prefab
```

### Docker: Standalone Merge

```bash
docker run --rm \
  -v "$PWD:$PWD" \
  -w "$PWD" \
  andantetribe/unity-yaml-merge:latest \
  --project-path . \
  --unity-version-source project \
  Assets/_merge_base.unity \
  Assets/_merge_ours.unity \
  Assets/_merge_theirs.unity \
  Assets/Merged.unity
```

### Docker: Git-Aware Merge

```bash
docker run --rm \
  -v "$PWD:$PWD" \
  -w "$PWD" \
  -v /var/run/docker.sock:/var/run/docker.sock \
  andantetribe/unity-yaml-merge-git:latest \
  --base-branch main \
  --project-path . \
  --unity-version-source project \
  --target-extensions unity prefab
```

## Requirements

- Docker must be available.
- Unity assets should be text serialized so UnityYAMLMerge can process them.
- When `unity-version-source` is `project`, `ProjectSettings/ProjectVersion.txt` must exist under `project-path`.
- The Git-oriented flow expects a Git repository and access to the Docker socket because it launches the standalone merge image internally.

## Arguments

### `UnityYamlMerge`

Command shape:

```text
UnityYamlMerge [options...] <base1> <ours1> <theirs1> <output1> [<base2> <ours2> <theirs2> <output2> ...]
```

| Option | Default | Description |
| --- | --- | --- |
| `--project-path <path>` | `.` | Unity project root. |
| `--unity-version-source <project\|latest-lts\|manual>` | `project` | How to determine the Unity Editor version. |
| `--unity-version <version>` | `""` | Unity version string. Required when `--unity-version-source manual` is used. |

| Positional Argument | Description |
| --- | --- |
| `<base>` | Base file path. Must exist. |
| `<ours>` | Ours file path. Must exist. |
| `<theirs>` | Theirs file path. Must exist. |
| `<output>` | Output file path. `output == ours` is allowed. |

Validation rules:

- Positional file arguments must be provided in groups of 4.
- `base`, `ours`, and `theirs` must exist.
- `project-path` must exist.
- `project` mode requires `ProjectSettings/ProjectVersion.txt`.
- `manual` mode requires `unity-version`.

### `UnityYamlMerge.Git`

Command shape:

```text
UnityYamlMerge.Git [options...] [--target-extensions <ext1> <ext2> ...]
```

| Option | Default | Description |
| --- | --- | --- |
| `--auto-push` | `false` | Push resolved results back to the remote repository. |
| `--git-user-email <email>` | `""` | Git commit email used when creating a commit. |
| `--git-user-name <name>` | `""` | Git commit name used when creating a commit. |
| `--base-branch <branch>` | `""` | Base branch to compare and merge against. When omitted, the repository default branch is used. |
| `--project-path <path>` | `.` | Unity project root. |
| `--unity-version-source <project\|latest-lts\|manual>` | `project` | How to determine the Unity Editor version. |
| `--unity-version <version>` | `""` | Unity version string. Required when `--unity-version-source manual` is used. |
| `--target-extensions <ext1> <ext2> ...` | `unity prefab` | Extensions to treat as Unity merge targets. |

Behavior notes:

- If `--base-branch` is omitted, the tool resolves the repository default branch from `origin/HEAD`.
- Only conflicted files whose extensions match `target-extensions` are passed to UnityYAMLMerge.
- When `--auto-push` is enabled and all conflicts are Unity-target files, the tool starts a real `git merge`, resolves files, and either runs `git merge --continue` or creates a commit for partial resolution.

### GitHub Action Inputs

The Marketplace action exposes the following inputs from [action.yml](action.yml):

| Input | Default | Description |
| --- | --- | --- |
| `auto-push` | `true` | Automatically push resolved files back to the repository. |
| `git-user-email` | `41898282+github-actions[bot]@users.noreply.github.com` | Git commit email used when auto-push is enabled. |
| `git-user-name` | `github-actions[bot]` | Git commit name used when auto-push is enabled. |
| `github-token` | `""` | Token used for authenticated pushes. Defaults to `${{ github.token }}` when omitted. |
| `base-branch` | `""` | Base branch override. If omitted, the action uses the pull request base branch or the repository default branch. |
| `target-extensions` | `unity prefab` | Space-separated target extensions. |
| `project-path` | `.` | Unity project root. |
| `unity-version-source` | `project` | Version resolution strategy. |
| `unity-version` | `""` | Manual Unity version. |

## Environment Variables

### User-Facing Environment Variables

The CLI tools do not require any user-facing environment variables.

### Internally Used Environment Variables

These environment variables are set internally by the action or subprocess helpers and usually should not be set manually:

| Variable | Set By | Purpose |
| --- | --- | --- |
| `GIT_CONFIG_COUNT=1` | GitHub Action wrapper | Enables inline Git config injection for Docker-based Git credentials. |
| `GIT_CONFIG_KEY_0=credential.helper` | GitHub Action wrapper | Configures Git to use the stored credentials file. |
| `GIT_CONFIG_VALUE_0=store` | GitHub Action wrapper | Paired value for `credential.helper`. |
| `GIT_EDITOR=true` | `UnityYamlMerge.Git` | Prevents interactive editor prompts during `git merge --continue`. |
| `GIT_MERGE_AUTOEDIT=no` | `UnityYamlMerge.Git` | Prevents Git from opening an editor during merge continuation. |
| `GIT_TERMINAL_PROMPT=0` | `UnityYamlMerge.Git` | Disables interactive credential prompts during `git push`. |

The GitHub Action also relies on standard GitHub-hosted environment values such as `GITHUB_WORKSPACE` and `HOME` when mounting volumes into Docker.

## Unity Version Resolution

| Mode | Behavior | Notes |
| --- | --- | --- |
| `project` | Reads `m_EditorVersion` from `ProjectSettings/ProjectVersion.txt`. | Requires `project-path/ProjectSettings/ProjectVersion.txt`. |
| `latest-lts` | Fetches the latest Unity LTS version. | If the newest Docker tag is unavailable, the tool retries a few older LTS patch versions. |
| `manual` | Uses the value passed via `unity-version`. | `unity-version` is required. |

## License

[MIT License](LICENSE)
