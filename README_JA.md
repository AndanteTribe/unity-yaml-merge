# Unity YAML Merge
[![dotnet-test](https://github.com/AndanteTribe/unity-yaml-merge/actions/workflows/dotnet-test.yml/badge.svg)](https://github.com/AndanteTribe/unity-yaml-merge/actions/workflows/dotnet-test.yml)
[![docker-publish](https://github.com/AndanteTribe/unity-yaml-merge/actions/workflows/docker-publish.yml/badge.svg)](https://github.com/AndanteTribe/unity-yaml-merge/actions/workflows/docker-publish.yml)
[![GitHub Actions](https://img.shields.io/badge/GitHub%20Actions-Marketplace-2088FF?logo=githubactions)](https://github.com/marketplace/actions/unity-yaml-merge)
[![Docker Hub](https://img.shields.io/docker/v/andantetribe/unity-yaml-merge?label=Docker%20Hub&logo=docker&sort=semver)](https://hub.docker.com/r/andantetribe/unity-yaml-merge)
[![Docker Hub](https://img.shields.io/docker/v/andantetribe/unity-yaml-merge-git?label=Docker%20Hub&logo=docker&sort=semver)](https://hub.docker.com/r/andantetribe/unity-yaml-merge-git)
[![GitHub license](https://img.shields.io/github/license/AndanteTribe/unity-yaml-merge.svg)](./LICENSE)

[English](README.md) | 日本語

## 概要

**Unity YAML Merge** は、Unity のテキストシリアライズされたアセットに対して UnityYAMLMerge を実行するための Docker ベースのラッパーです。

このリポジトリでは、現在以下三点をデプロイしています。

| 公開物 | 名前 | 概要 |
| --- | --- | --- |
| GitHub Actions Marketplace | `AndanteTribe/unity-yaml-merge` | CI 上で Unity YAML の競合解決を行う Git 指向の自動化です。 |
| Docker イメージ | `andantetribe/unity-yaml-merge` | `base`、`ours`、`theirs`、`output` のパスが揃っている場合に使う単体マージ実行用イメージです。 |
| Docker イメージ | `andantetribe/unity-yaml-merge-git` | Git リポジトリを調べて競合ファイルを検出し、必要に応じて push まで行う Git 連携用イメージです。 |

## クイックスタート

### GitHub Actions Marketplace

推奨トリガーは `push` と `workflow_dispatch` です。

`pull_request` は、競合状態にある Pull Request だと GitHub がイベントを発火しないため使えません。`pull_request_target` なら回避できますが、知られているとおりセキュリティリスクがあるため、利用方法としては推奨しません。

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

### Docker: 単体マージ

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

### Docker: Git 連携マージ

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

## 要件

- Docker が利用可能であること。
- Unity アセットは UnityYAMLMerge が扱えるよう、テキストシリアライズされていること。
- `unity-version-source` が `project` の場合、`project-path` 配下に `ProjectSettings/ProjectVersion.txt` が必要です。
- Git 連携フローでは、Git リポジトリであることに加えて、内部で単体マージ用イメージを起動するため Docker ソケットへのアクセスが必要です。

## 引数

### `UnityYamlMerge`

コマンド形式:

```text
UnityYamlMerge [options...] <base1> <ours1> <theirs1> <output1> [<base2> <ours2> <theirs2> <output2> ...]
```

| オプション | 既定値 | 説明 |
| --- | --- | --- |
| `--project-path <path>` | `.` | Unity プロジェクトのルートパスです。 |
| `--unity-version-source <project\|latest-lts\|manual>` | `project` | Unity Editor のバージョン決定方法です。 |
| `--unity-version <version>` | `""` | Unity のバージョン文字列です。`--unity-version-source manual` のとき必須です。 |

| 位置引数 | 説明 |
| --- | --- |
| `<base>` | Base ファイルのパスです。存在している必要があります。 |
| `<ours>` | Ours ファイルのパスです。存在している必要があります。 |
| `<theirs>` | Theirs ファイルのパスです。存在している必要があります。 |
| `<output>` | 出力先のパスです。`output == ours` は許可されます。 |

バリデーション:

- 位置引数は 4 個単位で渡す必要があります。
- `base`、`ours`、`theirs` は存在している必要があります。
- `project-path` は存在している必要があります。
- `project` モードでは `ProjectSettings/ProjectVersion.txt` が必要です。
- `manual` モードでは `unity-version` が必要です。

### `UnityYamlMerge.Git`

コマンド形式:

```text
UnityYamlMerge.Git [options...] [--target-extensions <ext1> <ext2> ...]
```

| オプション | 既定値 | 説明 |
| --- | --- | --- |
| `--auto-push` | `false` | 解決結果をリモートへ push します。 |
| `--git-user-email <email>` | `""` | commit 作成時に使う Git の email です。 |
| `--git-user-name <name>` | `""` | commit 作成時に使う Git の user name です。 |
| `--base-branch <branch>` | `""` | 比較およびマージ対象のベースブランチです。省略時はリポジトリのデフォルトブランチを使います。 |
| `--project-path <path>` | `.` | Unity プロジェクトのルートパスです。 |
| `--unity-version-source <project\|latest-lts\|manual>` | `project` | Unity Editor のバージョン決定方法です。 |
| `--unity-version <version>` | `""` | Unity のバージョン文字列です。`--unity-version-source manual` のとき必須です。 |
| `--target-extensions <ext1> <ext2> ...` | `unity prefab` | Unity マージ対象として扱う拡張子です。 |

動作メモ:

- `--base-branch` を省略すると、`origin/HEAD` からデフォルトブランチを解決します。
- `target-extensions` に一致する拡張子を持つ競合ファイルだけが UnityYAMLMerge に渡されます。
- `--auto-push` が有効で、競合がすべて Unity 対象ファイルだった場合は実際の `git merge` を開始し、解決後に `git merge --continue` または部分解決用 commit を行います。

### GitHub Action Inputs

Marketplace アクションは [action.yml](action.yml) で次の input を公開しています。

| Input | 既定値 | 説明 |
| --- | --- | --- |
| `auto-push` | `true` | 解決したファイルを自動でリポジトリへ push します。 |
| `git-user-email` | `41898282+github-actions[bot]@users.noreply.github.com` | auto-push 時に使う Git commit email です。 |
| `git-user-name` | `github-actions[bot]` | auto-push 時に使う Git commit user name です。 |
| `github-token` | `""` | 認証付き push に使う token です。省略時は `${{ github.token }}` を使います。 |
| `base-branch` | `""` | ベースブランチの上書き値です。省略時は pull request の base branch、またはリポジトリの default branch を使います。 |
| `target-extensions` | `unity prefab` | スペース区切りの対象拡張子です。 |
| `project-path` | `.` | Unity プロジェクトのルートパスです。 |
| `unity-version-source` | `project` | バージョン解決戦略です。 |
| `unity-version` | `""` | 手動指定する Unity バージョンです。 |

## 環境変数

### ユーザー向け環境変数

CLI ツールは、ユーザーが設定すべき環境変数を必要としません。

### 内部的に利用する環境変数

以下はAction ラッパーや subprocess ヘルパーが内部で設定する環境変数で、基本的に手動設定は不要です。

| 変数 | 設定元 | 用途 |
| --- | --- | --- |
| `GIT_CONFIG_COUNT=1` | GitHub Action ラッパー | Docker 内で Git credential 設定を注入するために使います。 |
| `GIT_CONFIG_KEY_0=credential.helper` | GitHub Action ラッパー | Git の `credential.helper` を設定します。 |
| `GIT_CONFIG_VALUE_0=store` | GitHub Action ラッパー | `credential.helper` の値です。 |
| `GIT_EDITOR=true` | `UnityYamlMerge.Git` | `git merge --continue` で対話エディタを開かないようにします。 |
| `GIT_MERGE_AUTOEDIT=no` | `UnityYamlMerge.Git` | merge continuation 時の自動編集を抑止します。 |
| `GIT_TERMINAL_PROMPT=0` | `UnityYamlMerge.Git` | `git push` 時の対話的な credential prompt を無効化します。 |

また GitHub Action では、Docker volume mount のために `GITHUB_WORKSPACE` や `HOME` のような GitHub 標準環境変数も利用します。

## Unity バージョン解決

| モード | 動作 | 補足 |
| --- | --- | --- |
| `project` | `ProjectSettings/ProjectVersion.txt` の `m_EditorVersion` を読み取ります。 | `project-path/ProjectSettings/ProjectVersion.txt` が必要です。 |
| `latest-lts` | 最新の Unity LTS を取得します。 | もし最新の Docker tag が存在しなければ、少し前の LTS patch まで再試行します。 |
| `manual` | `unity-version` で渡した値をそのまま使います。 | `unity-version` が必須です。 |

## ライセンス

[MIT License](LICENSE)
