# Validation / 検証状況

最終更新: 2026-09-29
対象候補: Y-TEC Data Capsule 1.2.0

## Current release gates / 現在のリリースゲート

| Gate | Result | Evidence |
|---|---|---|
| AnyCPU Release rebuild | PASS | 0 warnings, 0 errors |
| x86 Release rebuild | PASS | 0 warnings, 0 errors |
| x64 Release rebuild | PASS | 0 warnings, 0 errors |
| Synthetic regression suite | PASS | 46/46 tests on the current public source |
| Japanese/English UI resources | PASS | 215 matching keys |
| Japanese/English backup catalog | PASS | 22 matching items |
| Public-source worktree audit | PASS | current tracked-source pattern scan; final snapshot and package audited separately |
| Japanese user-manual PDF | PASS | newly generated A4, 11 pages, rendered and independently reviewed; owner approved |
| English user-manual PDF | PASS | newly generated A4, 11 pages, rendered and independently reviewed; owner approved |
| Product screenshots | PASS (preservation) | existing 4 Japanese + 4 English synthetic screenshots preserved; not newly captured |
| Cross-architecture format check | PASS | x86/x64 reciprocal reads with fixed synthetic fixture input, not official Wi-Fi credentials |
| Official compatibility input | PASS | unchanged input; fresh binary equality, synthetic decrypt and tamper rejection |

These are the current local 2026-09-29 development-build results (46/46 on
AnyCPU, x86 and x64), not a new nine-OS VM test. The official build has a separate
compatibility probe; the development-only resource assertion is not claimed to
pass on official binaries. No real Wi-Fi operation or user backup was performed.
Final signature, artifact, snapshot and publication results are recorded with
the release's actual checksums and are not inferred from a source scan.

## Historical Windows VM compatibility (through 2026-08-26)

The application targets .NET Framework 4.6.1 and an AnyCPU executable. The
then-current 46-test public-source payload passed on all nine offline VirtualBox
guests. All tested 64-bit guests ran both x86 and x64 test executables; 32-bit
guests ran x86. The AnyCPU UI process also launched successfully.
当時の公開ソースの46件は、ネットワーク無効のVirtualBoxゲスト9台すべてで
合格しました。64ビットOSではx86/x64双方、32ビットOSではx86を実行し、
AnyCPU UIの起動も確認しています。

- Windows 7 SP1 x86 / x64
- Windows 8 x86 / x64
- Windows 8.1 x86 / x64
- Windows 10 22H2 x86 / x64
- Windows 11 25H2 x64

The old VirtualBox guests were retired on 2026-09-06. Historical test evidence
was retained; these guests are not currently available. The current Windows 11
VMware lab was not run for this republication and does not replace old-OS tests.
Windows 7 remains a technical target, not a currently Microsoft-serviced OS.

旧VirtualBoxは2026-09-06に廃止し、過去の証跡を保持しています。今回の旧OS再試験と
現行Windows 11 VMware試験は未実施です。過去試験を今回の合格とは扱いません。

## User acceptance / 利用者受入

The user reported successful backup of Wi-Fi profiles, browser bookmarks, and
more than 100 GB of data to a USB SSD on a workplace PC. This is user acceptance
evidence, not a reproducible developer-lab benchmark.

利用者の職場PCでは、Wi-Fi、ブックマーク、100GB超データのUSB外付けSSDへの
バックアップ成功が報告されています。これは利用者受入の証跡であり、開発環境で
再現した性能ベンチマークではありません。

## Known limitations and tests not claimed / 未検証・対象外

- Import acceptance with real commercial accounting applications was omitted.
  Synthetic detection, classification, and copying pass, but each vendor's
  official backup/restore procedure takes priority.
- The republication uses Y-TEC self-signing, not SignPath or a commercial
  CA-trusted certificate. SmartScreen warnings may remain; trust is not installed.
- Dedicated Codex Security scan and current VM/UI acceptance are NOT RUN for
  this unchanged-runtime republication. Source privacy, package, signatures,
  synthetic regression and independent release review are separate checks.
- Screen-reader product quality has not been accepted with a dedicated reader.
- Real ReFS media was excluded. NTFS behavior and exFAT copy-without-ACL behavior
  are covered; FAT32 is rejected before execution.
- No claim is made that every OneDrive or iCloud provider state was tested.
  Offline/recall attributes are covered with synthetic files, and online-only
  files are skipped without triggering a download.

詳細な社内VM構成、ISO台帳、資格情報の場所、実行証跡は公開配布物ではなく、
Y-TECの非公開検証環境で管理します。
