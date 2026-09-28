# Y-TEC Data Capsule 1.2.0 公開・ライセンス監査

確認日: 2026-08-26
対象: `ytec-forge-commits/ytec-data-capsule`へ作成する新規公開履歴

ワークスペース全体の移行前スナップショットは
`governance/licensing/reports/2026-08-26-license-inventory.md`、判定基準は
`governance/licensing/Y-TEC_STANDARD_LICENSE_POLICY.md`と
`governance/licensing/guides/SIGNPATH_COMPATIBILITY_PROFILE.md`を参照した。
同台帳でData Capsuleが「独自ライセンス・private・要確認」だった状態から、
本監査に記載するApache-2.0の新規公開履歴へ移行する。

## 結論

| 対象 | 判定 | 条件・根拠 |
|---|---|---|
| Y-TEC制作ソース | PASS | ユーザーがY-TECの権利保有とOSS公開を確認し、Apache-2.0採用を明示承認 |
| 文書・PDF | PASS | Y-TEC制作。HTML/PDFともApache-2.0対象 |
| 公式アイコン | PASS | 2026-07-28に参照画像なしでOpenAI画像生成を使用し、Y-TEC向けに加工。Apache-2.0で公開 |
| スクリーンショット | PASS | 実WPF UIと合成データだけで再生成。Apache-2.0で公開 |
| Newtonsoft.Json 13.0.3 | PASS | MIT。配布ZIPへDLLと通知を同梱 |
| Microsoft.NETFramework.ReferenceAssemblies 1.0.3 | PASS | MIT。PrivateAssetsのビルド専用で配布しない |
| System.ValueTuple 4.5.0 | PASS | MIT。テスト専用で配布しない |
| Windows / .NET Framework system libraries | PASS | 対応OSが提供するSystem Libraryで、ZIPへ再配布しない |
| 旧Git履歴 | PASS | 非公開アーカイブに保管し、新公開リポジトリは単一の新規履歴から開始 |
| 公式Wi-Fi互換入力 | 要確認 | 非公開32バイト値をGitHub Actions Secretから公式ビルド時だけ注入。SignPathへ完全開示し判断を依頼 |

## 採用する法定・運用文書

- `LICENSE.txt`: Apache License 2.0全文
- `NOTICE`: Y-TEC帰属と公式リポジトリ
- `BRAND_POLICY.md`: Apache-2.0の著作権許諾を制限せず、商標・公式性の誤認を防止
- `ASSET_PROVENANCE.md`: アイコン、派生画像、画面、PDFの出自
- `THIRD-PARTY-NOTICES.txt`: 配布・ビルド・テスト依存の版と条件
- `CODE_SIGNING_POLICY.md`: 署名役割、承認、GitHub Actions、秘密値境界
- `SECURITY.md`, `PRIVACY.md`, `CONTRIBUTING.md`

## 過去版

1.0.0と1.1.0の既存配布物には、公開時に同梱した独自利用条件が適用されます。
今回のApache-2.0採用を過去ZIPや過去Releaseへ遡及適用しません。1.2.0以降の
新しい公開履歴と配布物を対象とします。

## SignPath Foundation

SignPath Foundationの現行条件は、OSI承認ライセンス、非プロプライエタリ、
公開・既リリース・保守中・文書化済みのプロジェクト、およびGitHub-hosted
runnerからのorigin verificationを求めます。本体と通常資産はApache-2.0、
配布依存はMITで説明できます。

ただし公式ビルドだけへ注入する非公開Wi-Fi互換入力が「全コンポーネントOSS」と
「公開ソースからの検証可能ビルド」に適合するかはY-TEC側で断定しません。
`docs/release/SIGNPATH-APPLICATION.md`に用途、限界、注入・削除方法を記載し、
申請時にSignPath Foundationの判断を求めます。

## 判定境界

本監査は技術・配布・運用上の確認であり、個別案件の法的助言ではありません。
SignPathの採否はSignPath Foundationが決定します。
