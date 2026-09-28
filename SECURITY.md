# Security Policy / セキュリティポリシー

## Reporting a vulnerability / 脆弱性の報告

Please do not open a public issue for a vulnerability that could expose Wi-Fi
credentials, bypass backup-path boundaries, alter source data, escape the
selected output folder, or abuse the signed release pipeline. Use a private
GitHub Security Advisory for
[`ytec-forge-commits/ytec-data-capsule`](https://github.com/ytec-forge-commits/ytec-data-capsule/security/advisories/new).

Wi-Fi資格情報の露出、バックアップ対象範囲の逸脱、元データの変更、選択した
出力先からのパス脱出、署名済みリリース工程の悪用につながる問題は、公開Issueへ
書かず、上記リポジトリの非公開GitHub Security Advisoryから報告してください。

Include the affected version, Windows version and bitness, a minimal synthetic
reproduction, the expected and actual behavior, and any relevant stack trace.
Never attach real Wi-Fi profiles, passwords, customer data, browser profiles,
private keys, or production backup folders.

対象バージョン、Windowsの版とビット数、合成データだけの最小再現手順、期待結果と
実際の結果、必要なスタックトレースを記載してください。実Wi-Fiプロファイル、
パスワード、顧客データ、ブラウザープロファイル、秘密鍵、本番バックアップは
添付しないでください。

## Supported versions / 対応バージョン

Security fixes are applied to the latest published release. Older Windows
versions remain compatibility targets, but Windows 7, 8, and 8.1 are no longer
serviced by Microsoft. A fix that cannot be implemented safely on an obsolete
platform may require disabling the affected feature there.

セキュリティ修正は最新公開版を対象にします。Windows 7 / 8 / 8.1は互換対象ですが、
Microsoftのサポートは終了しています。安全な修正を古いOSへ適用できない場合は、
そのOSで該当機能を無効化することがあります。

## Security boundaries / セキュリティ境界

- Source data must never be deleted, moved, renamed, modified, or have its ACL
  changed.
- NTFS/ReFS ACL changes are limited to the new output created by the current run.
- exFAT cannot store Windows ACLs; FAT32 is rejected.
- Browser scope is bookmarks only. Passwords, cookies, history, and sessions are
  excluded.
- Wi-Fi plaintext XML is temporary, ACL-restricted, and must not appear in logs,
  manifests, tests, repositories, or release artifacts.
- The transferable embedded Wi-Fi key prevents casual plaintext exposure but is
  not a hardware-backed secret and does not resist determined executable analysis.
- The official compatibility key, SignPath token, and signing identities are
  release secrets. They must not be committed or exposed to pull-request builds.

These boundaries are described in more detail in
`docs/security/00-wifi-encryption-boundary.md` and `CODE_SIGNING_POLICY.md`.
