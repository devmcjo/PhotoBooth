#!/usr/bin/env bash
#
# set-secrets.sh — 백엔드 배포용 Secret Manager 값 일괄 등록 (USER-ACTIONS §A1).
#
# 사전조건: `firebase login` 완료 + web/ 에서 실행(또는 --project 지정). Git Bash/WSL/macOS/Linux.
# 하는 일:
#   1) JWT_SECRET(강한 랜덤) 생성·등록
#   2) CLIENT_API_KEYS(강한 랜덤) 생성·등록 → WPF MCPhoto.ini 의 BackendApiKey 에 넣을 값을 출력
#   3) SENDGRID_API_KEY / GOOGLE_OAUTH_CLIENT_SECRET 는 코드에 defineSecret 으로 선언돼 있어
#      "배포 시 반드시 존재"해야 하므로, 아직 안 쓰면 placeholder 로 등록(첫 배포 실패 방지).
#      실제 값은 나중에 §B1/§B2 에서 이 스크립트 재실행 또는 개별 set 으로 교체.
#
# 재실행하면 새 secret '버전'이 추가된다(회전). 값 확인은:
#   firebase functions:secrets:access CLIENT_API_KEYS --project mcphoto-955fb
#
set -euo pipefail

PROJECT="${FIREBASE_PROJECT:-mcphoto-955fb}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
FUNCTIONS_DIR="$(dirname "$SCRIPT_DIR")"   # web/functions
cd "$FUNCTIONS_DIR"

echo "== MCPhoto 백엔드 시크릿 등록 (project=$PROJECT) =="

# --- 강한 랜덤 생성기: openssl 우선, 없으면 node ---
rand() { # $1=바이트수
  if command -v openssl >/dev/null 2>&1; then
    openssl rand -base64 "$1" | tr -d '\n'
  else
    node -e "process.stdout.write(require('crypto').randomBytes($1).toString('base64'))"
  fi
}
rand_hex() { # $1=바이트수
  if command -v openssl >/dev/null 2>&1; then
    openssl rand -hex "$1" | tr -d '\n'
  else
    node -e "process.stdout.write(require('crypto').randomBytes($1).toString('hex'))"
  fi
}

set_secret() { # $1=name  $2=value
  printf '%s' "$2" | firebase functions:secrets:set "$1" --data-file - --project "$PROJECT" --force >/dev/null
  echo "  [OK] $1 등록"
}

JWT_SECRET_VALUE="$(rand 48)"
CLIENT_API_KEY_VALUE="$(rand_hex 24)"

set_secret JWT_SECRET "$JWT_SECRET_VALUE"
set_secret CLIENT_API_KEYS "$CLIENT_API_KEY_VALUE"

# SendGrid: 환경변수로 실키를 주면 그걸, 아니면 placeholder(첫 배포용).
if [ -n "${SENDGRID_API_KEY:-}" ]; then
  set_secret SENDGRID_API_KEY "$SENDGRID_API_KEY"
else
  set_secret SENDGRID_API_KEY "placeholder-set-real-key-when-enabling-email"
  echo "        (placeholder — 이메일 실발송은 §B1에서 실키로 교체)"
fi

# Google OAuth secret: 환경변수로 실값을 주면 그걸, 아니면 placeholder(첫 배포용).
if [ -n "${GOOGLE_OAUTH_CLIENT_SECRET:-}" ]; then
  set_secret GOOGLE_OAUTH_CLIENT_SECRET "$GOOGLE_OAUTH_CLIENT_SECRET"
else
  set_secret GOOGLE_OAUTH_CLIENT_SECRET "placeholder-set-real-secret-when-enabling-sso"
  echo "        (placeholder — Google SSO는 §B2에서 실값으로 교체)"
fi

echo ""
echo "== 완료. 아래 CLIENT API KEY 를 WPF 배포 PC 의 MCPhoto.ini [MCPhoto] 에 넣으세요 =="
echo ""
echo "    BackendApiKey=$CLIENT_API_KEY_VALUE"
echo ""
echo "  (분실 시 재확인: firebase functions:secrets:access CLIENT_API_KEYS --project $PROJECT)"
echo "  다음 단계: §A2 IAM 권한 → §A4 배포(firebase deploy --only functions)."
