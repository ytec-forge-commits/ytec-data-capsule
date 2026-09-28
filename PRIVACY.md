# Privacy / プライバシー

Y-TEC Data Capsule is an offline desktop application. It does not include
telemetry, analytics, advertising, cloud synchronization, account sign-in,
automatic update checks, or any other application-initiated network transfer.

Y-TEC Data Capsuleはオフラインのデスクトップアプリです。テレメトリー、
アクセス解析、広告、クラウド同期、アカウントログイン、自動更新確認、その他の
アプリ主導の外部通信を行いません。

## Data the app reads / 読み取るデータ

The app reads only the sources needed for the items selected by the user. This
can include ordinary files, browser bookmark files, and Windows Wi-Fi profiles.
The read-only preview enumerates candidates before copying.

アプリは利用者が選択した項目に必要な範囲だけを読み取ります。通常ファイル、
ブラウザーのブックマーク、WindowsのWi-Fiプロファイルが含まれる場合があります。
コピー前に読み取り専用プレビューで候補を列挙します。

Browser passwords, cookies, browsing history, and sessions are outside the
browser-backup scope. Thunderbird password databases, cookies, and session state
are also excluded.

ブラウザーのパスワード、Cookie、閲覧履歴、セッションは対象外です。Thunderbirdも
保存パスワードDB、Cookie、セッション状態を除外します。

## Where data is stored / 保存先

Backup data, the result manifest, and warnings are written only to the new
backup folder selected by the user. Browser-restore rollback copies are stored
locally under `%LOCALAPPDATA%\Y-TEC\WindowsBackup\bookmark-restore-rollbacks`.

バックアップデータ、結果マニフェスト、警告は、利用者が選んだ保存先へ新規作成する
バックアップフォルダー内だけに書き込みます。ブックマーク復元前の退避はローカルの
`%LOCALAPPDATA%\Y-TEC\WindowsBackup\bookmark-restore-rollbacks`へ保存します。

Wi-Fi XML containing a plaintext network key exists only in an ACL-restricted
temporary directory while it is being validated and encrypted. SSIDs and
passwords are never displayed or written to the result manifest or application
logs. A narrowly scoped cleanup removes abandoned temporary directories on the
next launch after a crash.

Wi-Fiの平文鍵を含むXMLは、検証と暗号化の間だけACLで制限した一時フォルダーに
存在します。SSIDとパスワードは画面、結果マニフェスト、アプリログへ書きません。
異常終了時の一時フォルダーは、次回起動時に限定範囲で削除します。

## Cloud placeholders / クラウドのオンライン専用ファイル

OneDrive, iCloud, and similar online-only placeholders are skipped without
opening or downloading their content. Files that are already fully available on
the PC remain eligible for backup.

OneDrive、iCloud等のオンライン専用ファイルは内容を開かず、自動ダウンロードも
せずに除外します。PC内へ完全に保存済みのファイルは通常どおり対象にできます。

## User responsibility / 利用者の管理

Backups may contain personal or confidential information. Protect the destination
with physical security and, where appropriate, drive encryption such as
BitLocker. NTFS/ReFS output intentionally receives Everyone full control for
migration convenience; exFAT has no Windows ACL support.

バックアップには個人情報や機密情報が含まれる場合があります。物理的な施錠保管や、
必要に応じてBitLocker等のドライブ暗号化を利用してください。NTFS/ReFSの出力物は
移行しやすさのためEveryoneフルアクセスとなり、exFATにはWindows ACLがありません。
