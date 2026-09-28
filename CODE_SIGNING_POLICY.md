# Code Signing and Official Build Policy

Current direct-download provider: Y-TEC self-signed Authenticode, with an
RFC3161 SHA-256 timestamp. SignPath Foundation is a future candidate, not the
provider used for the 2026-09-29 republication. No service approval or configured
release secrets are claimed.

## Team roles

- Committers and reviewers: [Y-TEC organization owners](https://github.com/orgs/ytec-forge-commits/people?query=role%3Aowner)
- Signing approvers: [Y-TEC organization owners](https://github.com/orgs/ytec-forge-commits/people?query=role%3Aowner)

Every release signing request requires manual approval. Project members who can
modify source, approve signing, or administer the repository must use multi-factor
authentication for GitHub and for any future signing service before activation.

## Purpose

This document distinguishes public-source development builds from official
Y-TEC distributions and describes how signing secrets and the Wi-Fi compatibility
key are kept out of the public repository.

## Build types

### Public development build

- Built directly from the public repository without private inputs.
- Uses a documented public development Wi-Fi key.
- Displays a clear development-build warning.
- Must not be used with real Wi-Fi credentials.
- Is intentionally incompatible with official Y-TEC Wi-Fi backups.

### Custom-key build

- A maintainer can pass a 32-byte file outside the repository through the
  `YtecDataCapsuleCustomKeyFile` MSBuild property.
- The resulting Wi-Fi container is compatible only with builds using that same
  custom key.
- It is not an official Y-TEC distribution and must not claim official status.

### Official Y-TEC build

- Built from the fixed, audited source snapshot in a controlled local release
  environment; SDK, dependency lock, source revision and final hashes are recorded.
- Receives the unchanged 32-byte official Wi-Fi compatibility input only for
  this build. The owner authorized recovery of that input alone from the previous
  official app; no previous binary or archive is a public output.
- A temporary input file is restricted to the current user, SYSTEM and
  Administrators outside the workspace, never printed, and removed in a finally block.
- The build must embed the official resource and must not contain the public
  development resource.
- Sign the final Y-TEC EXE, Core DLL and Windows DLL using the existing protected,
  non-exportable signing key. Do not create, export, relocate or rotate keys.
- Retain the upstream signature of Newtonsoft.Json.dll without modifying it.
- Verify all signatures before final ZIP assembly and hashing. Never install the
  public certificate automatically or change certificate trust stores.
- The source snapshot excludes the standalone compatibility input, build outputs
  and signing secrets. The official binary embeds the input by design; it is not
  strong confidentiality against binary analysis.

## Secret handling

The following must never be committed, added to a source archive, printed in a
log, uploaded as an unsigned public artifact, or exposed to pull-request jobs:

- official Wi-Fi compatibility key
- SignPath API token
- signing certificate private material
- local DPAPI recovery copy
- any `.key`, `.pfx`, or `.p12` file

Repository history must be scanned before public release. The current public
repository must start from a clean history that has never contained the official
key. Historical development commits are retained in a separate private archive.

Fork pull requests and Dependabot jobs do not receive release secrets. Workflow
permissions are read-only by default and are expanded only for the release step
that needs them. Third-party Actions are pinned to reviewed immutable commit
SHAs when the final workflow is enabled.

## Future SignPath transparency

The existing manual-dispatch signing and unsigned-release workflows are retained
as future candidates. They are not the current release path and are not executed
or given secrets for this republication. Enabling them requires separate review
and authorization; CI uses public development inputs only.

The SignPath Foundation application and project description must disclose that
the public source can reproduce the full application behavior except for one
non-public 32-byte Wi-Fi compatibility key used by official builds. The key is
not a signing secret and does not provide strong confidentiality against binary
analysis; its purpose is cross-PC interoperability without a user password.

The public development key keeps the repository buildable and testable without
the private compatibility input. Regression tests must cover public, custom,
and official resource selection without logging the key bytes.

Y-TEC will disclose this private compatibility input in the SignPath Foundation
application. It is configuration data rather than proprietary executable code,
but it changes Wi-Fi-container interoperability. SignPath Foundation must decide
whether that boundary is acceptable under its verifiable-build and fully open
source requirements; the project will not conceal or work around that decision.

## Privacy statement

This program will not transfer any information to other networked systems unless
specifically requested by the user or the person installing or operating it.
The current application has no network operation at all: no telemetry, analytics,
cloud synchronization, automatic update, or remote API. See [PRIVACY.md](PRIVACY.md).

## Release verification

Before publication, verify at minimum:

1. The fixed source, build environment and public snapshot commit are recorded.
2. Localization, public-source, x86, x64, and AnyCPU checks pass; CI status is
   recorded separately, without claiming unexecuted workflows succeeded.
3. The official input was injected only in the controlled local build.
4. The key file was removed even if the build or signing step failed.
5. Every Y-TEC binary matches the reviewed public CER signer, SHA-256 digest and
   timestamp. SignTool `/pa /all /v` must succeed, or have exactly the sole
   expected untrusted-root error for this self-signed CER. All other errors,
   missing signatures, digest mismatch or missing timestamps stop publication.
   This narrow exception does not make the certificate CA-trusted. The upstream
   Newtonsoft.Json signature must be Valid and issued to Microsoft Corporation.
6. The signed EXE hash and portable ZIP hash are published.
7. The signed artifact passes the same portable-package verification as the
   unsigned staging artifact, with explicit self-signed verification via
   `eng/Test-PortableRelease.ps1 -ExpectedSignature SelfSigned
   -PublicCertificatePath <reviewed public CER>`.
8. GitHub Release and Y-TEC Forge link to the same verified package.

## 日本語要約

公開ソースの通常ビルドは公開開発鍵を使うため、実Wi-Fi設定には使用できず、
Y-TEC公式版とも互換性がありません。今回の公式版は、承認されたローカル工程で
旧公式版と同じ互換入力を一時注入し、既存の保護された非エクスポート鍵で自己署名します。
秘密入力の値を公開せず、一時ファイルは必ず削除します。EXEと自社DLLの署名・
タイムスタンプ・ダイジェストを確認し、第三者DLLの元の署名を保持します。
自己署名はMicrosoftや商用CAの信頼済み署名ではなく、SmartScreen警告が出る場合があります。
SignPathは将来候補であり、今回の署名には利用していません。

公式鍵、SignPathトークン、署名秘密情報、DPAPI復旧コピーはリポジトリ、ログ、
Pull Request、公開Artifactへ含めません。公開リポジトリは公式鍵を一度も含まない
新しい履歴とし、旧開発履歴は別の非公開リポジトリへ保管します。
