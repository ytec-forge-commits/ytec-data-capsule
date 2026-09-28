# Contributing / コントリビューション

Thank you for helping improve Y-TEC Data Capsule. Small, focused pull requests
with reproducible synthetic tests are easiest to review.

Y-TEC Data Capsuleへの改善提案を歓迎します。変更範囲を絞り、合成データだけで
再現できるテストを添えたPull Requestが最も確認しやすい形です。

## Before opening a pull request / Pull Requestの前に

1. Open an issue first for architecture changes, backup-format changes, new
   external dependencies, or a new source-data category.
2. Do not include real customer data, Wi-Fi profiles, browser profiles,
   credentials, secrets, signing material, VM credentials, or release keys.
3. Preserve the safety boundaries in `AGENTS.md`, `README.md`, and
   `docs/architecture/00-target-architecture.md`.
4. Keep Japanese and English UI resource keys synchronized.
5. Add or update synthetic regression coverage for behavioral changes.

アーキテクチャ、保存形式、外部依存、バックアップ分類を変える提案は、先にIssueで
相談してください。実データ、実Wi-Fi設定、ブラウザープロファイル、認証情報、秘密、
署名材料、VM資格情報、リリース鍵を含めてはいけません。

## Local verification / ローカル検証

```powershell
& "C:\Program Files\dotnet\dotnet.exe" restore .\Ytec.WindowsBackup.slnx
& "C:\Program Files\dotnet\dotnet.exe" format .\Ytec.WindowsBackup.slnx --verify-no-changes --no-restore
& "C:\Program Files\dotnet\dotnet.exe" build .\Ytec.WindowsBackup.slnx -c Release --no-restore
& .\tests\Ytec.WindowsBackup.Tests\bin\Release\net461\Ytec.WindowsBackup.Tests.exe
& .\eng\Test-Localization.ps1
```

Changes affecting CPU compatibility must also rebuild and test `x86` and `x64`.
UI changes must be checked at the 1280×720 baseline and at narrow widths. Use
`eng/New-Screenshots.ps1` only with synthetic preview data.

CPU互換性へ影響する変更はx86/x64もRebuildして確認します。UI変更は1280×720と
狭い幅で確認し、スクリーンショットには合成データ専用の撮影モードだけを使います。

## Pull request notes / PRへ記載する内容

- Why the change is needed
- Files and behavior changed
- Tests actually run and their results
- Windows versions or bitness checked
- Data-format, security, privacy, and compatibility impact
- Screenshots for visible UI changes
- Known limitations or tests not run

Security vulnerabilities should follow `SECURITY.md`, not a public issue.

## Contribution license / コントリビューションのライセンス

Unless explicitly stated otherwise before submission, any contribution
intentionally submitted for inclusion in this project is provided under the
Apache License 2.0, in accordance with section 5 of that license. By submitting
a contribution, you confirm that you have the right to do so and that it does
not contain third-party material under incompatible or undisclosed terms.

特に提出前の明示がない限り、本プロジェクトへ取り込む目的で提出された変更は、
Apache License 2.0第5条に従い同ライセンスで提供されます。提出者は、その変更を
提供できる権利を持ち、互換性のない条件または未開示の第三者素材を含まないことを
確認してください。
