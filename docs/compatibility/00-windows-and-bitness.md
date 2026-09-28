# Windows・ビット数の互換方針

更新日: 2026-07-28

## 配布方式

通常配布は `AnyCPU` 1版とする。

- 32ビットWindows: 32ビットCLRで起動
- 64ビットWindows: 64ビットCLRで起動
- 起動後のモード選択画面にOS/プロセスのビット数を表示

障害切り分け用にx86固定版とx64固定版もビルドできる。通常利用者にEXE選択を求める方式は採用しない。

## OSマトリクス

| OS | OSアーキテクチャ | アプリ対象 | 前提 |
|---|---:|---:|---|
| Windows 7 SP1 | x86 / x64 | 対象 | .NET Framework 4.6.1以降 |
| Windows 8 | x86 / x64 | 対象 | .NET Framework 4.6.1 |
| Windows 8.1 | x86 / x64 | 対象 | .NET Framework 4.6.1以降 |
| Windows 10 | x86 / x64 | 対象 | 導入済み4.xまたは4.6.1以降 |
| Windows 11 | x64 | 対象 | OS同梱の4.8/4.8.1 |

Windows 11の32ビット版OSは存在しない。

Windows 7 / 8 / 8.1と.NET Framework 4.6.1はMicrosoftのサポートを終了している。ここでの「対象」は技術的な互換目標であり、OSベンダーによるセキュリティ更新を意味しない。

## 機能別

| 機能 | x86→x64 | x64→x86 | OS固有条件 |
|---|---:|---:|---|
| 通常ファイル | 対応 | 対応 | 移行先アプリがデータ形式を読めること |
| Wi-Fi設定 | 対応 | 対応 | WLAN AutoConfig / `netsh wlan` が利用可能 |
| Chrome/Edgeブックマーク | 対応 | 対応 | 移行先に同ブラウザーのプロファイルが存在 |
| Firefoxブックマーク | 対応 | 対応 | Firefox内の公式復元操作を利用 |
| 弥生/フリーウェイ会計データ | 対応 | 対応 | 対応製品・版側のデータ互換性は別途確認 |
| Everyone所有者・ACL | 対応 | 対応 | 管理者権限、NTFSまたはReFS。exFATは適用対象外 |

会計データは通常ファイルとして扱うため、アプリの32/64ビットを理由に除外しない。会計ソフト自体の対応OS、製品版、データ形式の互換性はこのアプリでは変換しない。

保存先がexFATの場合もバックアップコピーは実行できる。ただしexFATは所有者・ACLを保存しないため、Everyone所有者・フルアクセス設定は行わず、画面とマニフェストへ理由を記録する。FAT32は実行前に拒否する。

## バックアップ形式

- JSONマニフェストはUTF-8、パス区切りは`/`
- Wi-Fiコンテナは固定長ヘッダーとリトルエンディアン32ビット長
- CPUポインター、ネイティブハンドル、プロセス依存シリアライズを保存しない
- マニフェストに作成プロセスとOSのアーキテクチャを記録

## 実施済み検証

- AnyCPUビルド: 成功
- x86固定ビルド: 強制再ビルド成功、32ビットプロセスでテスト32/32
- x64固定ビルド: 強制再ビルド成功、64ビットプロセスでテスト32/32
- x86で作成したWi-Fi暗号化ファイルをx64で復号: 成功
- x64で作成したWi-Fi暗号化ファイルをx86で復号: 成功
- 現在のWindows 11 Home x64（OSビルド26200）でAnyCPU版が64ビット動作することを画面表示で確認
- Windows 7 SP1 x86 / x64クリーンVM
  - x86 OSでx86固定版32/32
  - x64 OSでx86 / x64固定版各32/32
  - AnyCPU UIプロセスの起動継続
  - x86 OSで作成したWi-Fi暗号化ファイルをx64 OSで読込、逆方向も成功
- Windows 8 x86 / x64クリーンVM
  - x86 OSでx86固定版32/32
  - x64 OSでx86 / x64固定版各32/32
  - AnyCPU UIプロセスの起動継続
- Windows 8.1 x86 / x64クリーンVM
  - x86 OSでx86固定版32/32
  - x64 OSでx86 / x64固定版各32/32
  - AnyCPU UIプロセスの起動継続
- Windows 10 22H2 Pro x86クリーンVM
  - x86固定版32/32
  - AnyCPU UIプロセスの起動継続
  - exFAT仮想ディスクへ576MiB・1,032ファイルを実コピーし、SHA-256一致
  - 管理者ACLランナーでEveryone所有者・フルアクセスを読戻し確認
- Windows 10 22H2 Pro x64クリーンVM
  - x86 / x64固定版テスト各32/32
  - Wi-Fi暗号化ファイルのx86→x64、x64→x86読込
  - AnyCPU UIプロセスの起動継続
- Windows 11 25H2 Pro x64クリーンVM
  - x86 / x64固定版テスト各32/32
  - Wi-Fi暗号化ファイルのx86→x64、x64→x86読込
  - AnyCPU UIプロセスの起動継続

固定版は、同一出力先の増分ビルドによる直前EXEの再利用を避けるため、`-t:Rebuild`で作り直して実行ビット数を確認する。VMはネットワーク無効で、検証済みスナップショットとともに今後の回帰試験用としてY-TECの非公開検証環境へ保存している。公開可能な結果と未検証範囲は`VALIDATION.md`を参照する。

AnyCPUのポータブル評価版`0.3.0`は、Windows 10 x86 / x64 VMで通常起動し、UAC承認後にそれぞれ32ビット / 64ビットCLRで動作することを画面表示で確認した。Windows 10 x86 / x64では、共有ロック中の合成ファイルをVSSから取得できることも管理者受入で確認した。

## 未実施

- 各OS間の実Wi-Fiプロファイル移行
- 各OS・各ブラウザー版間の実ブックマーク移行

合成データとアプリ形式の技術的互換性はWindows 7～11の対象構成で確認した。実Wi-Fi機器、実ブラウザープロファイル、実会計製品側の受入は別途必要である。

## 公式資料

- Microsoft: .NET Framework 4.6.1 offline installer
  - https://support.microsoft.com/en-us/servicing/dotnetframework/2017/01/the-net-framework-4-6-1-offline-installer-for-windows
- Microsoft: C# PlatformTarget
  - https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-options/output
- Microsoft: Application manifests
  - https://learn.microsoft.com/en-us/windows/win32/sbscs/application-manifests
