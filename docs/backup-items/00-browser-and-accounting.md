# ブラウザー・会計ソフト項目

更新日: 2026-07-28

## ブラウザーブックマーク

### Chrome / Edge

対象:

- `User Data/*/Bookmarks`
- `User Data/*/Bookmarks.bak`

対象外:

- `Login Data`
- `History`
- `Cookies`
- `Sessions`
- `Web Data`
- 拡張機能やブラウザー全プロファイル

バックアップカタログは`FilesOnly`を使うため、`Bookmarks`という名前のディレクトリを再帰コピーしない。復元時はJSONルートと`roots`オブジェクトを確認する。

### Firefox

対象:

- `Profiles/*/bookmarkbackups/*.jsonlz4`
- `Profiles/*/bookmarkbackups/*.json`

対象外:

- `places.sqlite`
- `logins.json`
- `key4.db`
- `cookies.sqlite`
- セッションファイル

`places.sqlite`にはブックマークだけでなく履歴も含まれるため、ブックマーク限定要件では使用しない。復元はFirefox公式のバックアップファイル復元操作へ案内する。

Mozilla公式:

- https://support.mozilla.org/en-US/kb/restore-bookmarks-from-backup-or-move-them

### Internet Explorer

従来互換として各ユーザーの`Favorites`を通常ファイルとして保存する。専用復元画面は現時点でChrome / Edge / Firefoxを対象とする。

## 年賀状ソフト・住所録

旧版が検索していた13拡張子を引き継ぎ、次のようにソフト別フォルダーへ整理する。

- `年賀状ソフト/筆王`: `.FZD`
- `年賀状ソフト/筆ぐるめ`: `.FGA`
- `年賀状ソフト/筆まめ`: `.FWA`、`.FWB`、`.SDL`
- `年賀状ソフト/楽々はがき`: `.JSR`
- `年賀状ソフト/宛名職人`: `.ATA`
- `年賀状ソフト/筆休め`: `.FYA`
- `年賀状ソフト/はがきスタジオ`: `.HSA`、`.HSD`
- `年賀状ソフト/はがき作家`: `.HWA`、`.HWC`、`.HWL`
- `年賀状ソフト/はがきデザインキット`: `designKit.*`

各ソフト名の下には元ドライブからの相対パスを維持する。旧版のようにファイル名だけへ
平坦化しないため、同名住所録を上書きせず、どの利用者・保存場所から取得したかを
フォルダー構造で確認できる。

## 会計ソフト

### 自動対象

#### 弥生会計 / やよいの青色申告

ユーザーのDocuments/My Documents配下にある次の事業所データフォルダーを対象とする。

- `Yayoi/弥生会計*データフォルダ`
- `Yayoi/やよいの青色申告*データフォルダ`

会計ソフトを終了した状態でコピーする。製品版をまたぐ変換は会計ソフト側で行う。
出力先は`会計ソフト/弥生会計・やよいの青色申告`とし、その下に元ドライブからの
相対パスを維持する。

弥生公式のデータ移行・保存場所資料:

- https://support.yayoi-kk.co.jp/subcontents.html?page_id=17754

#### フリーウェイ経理

選択したWindowsドライブ直下の`KAIKEI_K`を対象とする。公式FAQでフォルダーコピーによる移行手順が案内されているため、自動対象に含める。
出力先は`会計ソフト/フリーウェイ経理`とし、その下に`KAIKEI_K`を保存する。

フリーウェイ公式FAQ:

- https://faq.freeway-japan.com/faq/show/2453?category_id=8&site_domain=default

### 自動対象外

#### ブルーリターンA

製品内のデータバックアップ/復元操作が前提のため除外する。

- https://www.bluereturna.jp/cgi-bin/PDF/pdf.cgi?file=jimusidou.pdf

#### PCA会計

製品のバックアップ/リカバリーメニューを使う手順が前提のため除外する。

- https://faq.pca.jp/faq/show/5540?category_id=49&site_domain=default

### 32/64ビット

自動対象の2製品はファイルとしてコピーし、アプリ固有DLLやCOMを読み込まない。このバックアップ処理自体は32/64ビット共通とする。

ただし、移行先の会計ソフトが古いデータ版、旧OS、32/64ビット構成を受け付けるかは製品側仕様であり、このアプリはデータ変換を行わない。

## 合成データ試験

実会計データを使わず、弥生の事業所データフォルダーとフリーウェイの`KAIKEI_K`を模した一意な合成フォルダーを作成した。年賀状ソフトの住所録拡張子を模した合成ファイルも用意し、Windows 7 SP1 / 8 / 8.1 / 10 / 11の対象VMすべてで、項目検出、バックアップ計画、ソフト別出力先への実コピー、内容一致が32件の自動試験に含まれて成功している。

これは本アプリのパス検出・コピー機能の確認である。製品版会計ソフトでの読込、版変換、製品固有の整合性確認は未実施で、実製品側の受入が別途必要になる。
