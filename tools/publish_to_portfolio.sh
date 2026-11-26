#!/usr/bin/env bash
# ヘルピアの画面を mukai.works（../portfolio）の public/helpia/ に組み込む。公開は portfolio 側で vercel deploy。
set -euo pipefail
cd "$(dirname "$0")/../web"
PORTFOLIO="${PORTFOLIO_DIR:-../../portfolio}"
MSYS_NO_PATHCONV=1 MSYS2_ARG_CONV_EXCL='*' HELPIA_BASE=/helpia/ npm run build
grep -q 'src="/helpia/assets/' dist/index.html || { echo "部品のパスが /helpia/ になっていません" >&2; exit 1; }
rm -rf "$PORTFOLIO/public/helpia" && mkdir -p "$PORTFOLIO/public/helpia" && cp -r dist/. "$PORTFOLIO/public/helpia/"
echo "組み込み完了: $PORTFOLIO/public/helpia"
