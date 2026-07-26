# 배포 실행 가이드 — 운영자용 단계별 워크스루

| 항목 | 값 |
|------|-----|
| 목적 | 백엔드 전환(서비스계정 키 제거)을 **순서대로 따라 하며** 각 단계의 성공 판정까지 확인하는 실행 가이드. |
| 전체 참조 | 체크리스트·주석 전체는 [`USER-ACTIONS.md`](./USER-ACTIONS.md). 이 문서는 그 "따라 하는" 버전. |
| 프로젝트 | Firebase `mcphoto-955fb` · 리전 `asia-northeast3` · 버킷 `mcphoto-955fb.firebasestorage.app` |
| 함수 URL(예상) | `https://asia-northeast3-mcphoto-955fb.cloudfunctions.net/api` (배포 후 실제 출력값 사용) |

> **지금 상태는 안전합니다.** 앱은 `UseBackend=false`(기본)로 기존 Firebase 경로로 동작 중입니다.
> 아래 **Part 1**을 끝내야 비로소 백엔드 경유로 전환되고, 그 전까지는 언제든 손대지 않아도 됩니다.

## 전제
- **셸**: 명령은 `web/` 디렉토리 기준. `.sh` 스크립트는 **Git Bash**에서, 나머지 `firebase`/`node`/`gcloud`는 PowerShell·Git Bash 무관.
- 필요한 것: Firebase CLI(설치됨), (IAM용) gcloud 또는 웹 콘솔.
- 각 단계는 **실행 → 성공 판정**으로 구성. 판정이 안 맞으면 다음으로 넘어가지 말 것.

---

# Part 1 — 백엔드 전환 (필수, ①→⑦ 순서 엄수)

## ① Firebase 로그인 (한 번만)
```
firebase login
```
> 이 세션에서 바로 하려면 프롬프트에 `! firebase login` 입력(출력이 대화로 들어옴).

**성공 판정**
```
firebase projects:list
```
→ 목록에 `mcphoto-955fb` 가 보이면 OK.

---

## ② 시크릿 등록 (스크립트 한 방)
Git Bash에서, `web/` 디렉토리:
```
bash functions/scripts/set-secrets.sh
```
- JWT_SECRET·CLIENT_API_KEYS(강한 랜덤) + SendGrid/Google placeholder를 Secret Manager에 등록.
- (Secret Manager API 활성화 프롬프트가 처음 뜨면 `y`.)

**성공 판정** — 출력 마지막에 이 줄이 나옵니다:
```
    BackendApiKey=xxxxxxxxxxxxxxxx
```
→ 이 값을 **메모**하세요(④·⑤에서 사용). 분실 시:
```
firebase functions:secrets:access CLIENT_API_KEYS --project mcphoto-955fb
```

---

## ③ IAM 서명 권한 (⚠️ 빼먹으면 프레임 저장·업로드가 프로덕션에서만 실패)
프레임/업로드는 v4 **서명 PUT URL**을 발급하고, 이때 함수 런타임 계정이 자기 자신에 대해 `signBlob`(**Service Account Token Creator**) 권한이 필요합니다. (에뮬레이터로는 못 잡는 지점)

**3-1. 런타임 계정 이메일 확인** (2nd gen 기본 = Compute SA)
```
gcloud projects describe mcphoto-955fb --format="value(projectNumber)"
```
→ 출력이 `123456789012` 면 계정 이메일은
`123456789012-compute@developer.gserviceaccount.com`

**3-2. 권한 부여 — 방법 ① gcloud** (아래 `123456789012`를 실제 번호로 치환)
```
gcloud iam service-accounts add-iam-policy-binding \
  123456789012-compute@developer.gserviceaccount.com \
  --member="serviceAccount:123456789012-compute@developer.gserviceaccount.com" \
  --role="roles/iam.serviceAccountTokenCreator" \
  --project=mcphoto-955fb
```

**3-2. 권한 부여 — 방법 ② 웹 콘솔** (gcloud 없을 때)
1. [Google Cloud Console](https://console.cloud.google.com/) → 프로젝트 `mcphoto-955fb`.
2. **IAM & Admin → Service Accounts** → `...-compute@developer.gserviceaccount.com` 클릭.
3. 상단 **PERMISSIONS** 탭 → **GRANT ACCESS**.
4. **New principals** = 같은 SA 이메일(자기 자신) 붙여넣기.
5. **Role** = `Service Account Token Creator` 선택 → **SAVE**.

**성공 판정**: 콘솔 그 SA의 PERMISSIONS(또는 `gcloud ... get-iam-policy`)에 `roles/iam.serviceAccountTokenCreator`가 자기 자신에게 보임.

---

## ④ 함수 배포 + 스모크
```
firebase deploy --only functions
```
- 첫 배포는 Cloud Functions·Build·Artifact Registry·Run·Eventarc **API 활성화 프롬프트** → 전부 `y`.
- 배포 성공 후 콘솔에 **함수 URL**이 출력됩니다(끝에 `/api` 포함). 이 값을 아래 `BASE_URL`에 씁니다.

**배포 직후 스모크** (읽기전용, 데이터 영향 없음):

PowerShell:
```
$env:BASE_URL="https://asia-northeast3-mcphoto-955fb.cloudfunctions.net/api"
$env:API_KEY="<②의 BackendApiKey>"
node functions/scripts/post-deploy-smoke.mjs
```
Git Bash:
```
export BASE_URL="https://asia-northeast3-mcphoto-955fb.cloudfunctions.net/api"
export API_KEY="<②의 BackendApiKey>"
node functions/scripts/post-deploy-smoke.mjs
```

**성공 판정** — 출력:
```
  PASS  health 200 + status:ok
  PASS  frames 키 없음 → 401
  PASS  frames 유효 키 → 200 배열
결과: 3 passed, 0 failed
```
→ 도달·API키·서명 경로 정상. (`frames 유효 키 200`이 PASS면 ②의 키와 ③ 서명이 함께 맞은 것.)

---

## ⑤ 앱을 백엔드로 전환 (feature flag ON)
배포 PC의 `MCPhoto.ini` `[MCPhoto]` 섹션에:
```
UseBackend=true
BackendBaseUrl=https://asia-northeast3-mcphoto-955fb.cloudfunctions.net/api
BackendApiKey=<②의 BackendApiKey>
```
> `BackendBaseUrl`은 ④에서 출력된 실제 함수 URL(끝에 `/api`). 빈 값이면 앱이 자동으로 off로 되돌립니다(안전장치).

**성공 판정**: 앱 재실행 → 로그인/프레임/촬영업로드가 정상 동작(= 백엔드 경유).
**롤백**: 이상 시 `UseBackend=false` 한 줄로 즉시 기존 키 경로 복귀.

---

## ⑥ 프로덕션 E2E 검증
아래를 실제 앱에서 확인(체크리스트 원본 = USER-ACTIONS §A6):
- [ ] 로그인(manager/user) → 화면 진입. (선택) 스모크로도:
  `$env:LOGIN_ID="devmcjo"; $env:LOGIN_PW="<비번>"; node functions/scripts/post-deploy-smoke.mjs`
- [ ] (파워) 프레임 저장 → 서명 PUT 성공, 목록 반영
- [ ] 촬영 업로드 → **웹 다운로드 페이지(`/?s=...`)에 사진/타임랩스 표시**
- [ ] 역할 위계: manager로 admin 삭제/초기화 시도 → **403**

**성공 판정**: 위 모두 통과 + 며칠간 프로덕션에서 안정.

---

## ⑦ ⚠️ 서비스 계정 키 폐기 (불가역 · 최후)
> **⑤·⑥가 프로덕션에서 안정 확인된 뒤에만.** 되돌릴 수 없습니다.

1. publish 산출물·배포 PC에서 `serviceAccountKey.json` 제거.
2. GCP **IAM & Admin → Service Accounts → (키 소유 SA) → KEYS** 에서 해당 키 **삭제/회전**.

**성공 판정**: 배포물 어디에도 키 파일이 없고, 앱은 백엔드 경유로만 DB 접근. 키는 함수 런타임(ADC)에만 존재 → **목표 달성**.

---

# Part 2 — 이메일 실발송 (B1) *(필요할 때)*
> 지금은 개발용 `log` sender라 인증/재설정 메일이 **실제로 안 나갑니다.** 실발송하려면:

1. **SendGrid 가입**(<https://sendgrid.com>) → Settings → **API Keys → Create API Key**(권한 "Mail Send") → **키는 한 번만 표시되니 즉시 복사**.
2. Settings → **Sender Authentication** → 도메인 인증(SPF/DKIM CNAME 추가) 또는 Single Sender(주소 1개 이메일 인증) → 발신 주소 확보.
3. 키 등록 (Git Bash):
   ```
   SENDGRID_API_KEY="SG.복사한키" bash functions/scripts/set-secrets.sh
   ```
4. `web/functions/.env.mcphoto-955fb` 생성:
   ```
   EMAIL_PROVIDER=sendgrid
   EMAIL_FROM=no-reply@your-domain     # 2에서 인증한 주소
   ```
   (로컬 에뮬레이터를 log로 유지하려면 `web/functions/.env.local`에 `EMAIL_PROVIDER=log` 한 줄 — 이 파일은 배포 안 됨.)
5. `firebase deploy --only functions` 재배포.

**성공 판정**: 앱에서 이메일 인증/비번 찾기 시 실제 메일 수신.

---

# Part 3 — Google 로그인 (B2) *(필요할 때)*
> 지금은 client id/secret 미설정이라 `/auth/google`가 비활성(501). 켜려면 **id·secret 둘 다** 필요.

1. Cloud Console → **APIs & Services → OAuth consent screen**: User Type(사내 Workspace면 Internal), 앱 이름·지원 이메일, scope `openid`·`email`·`profile`. (External이면 Test users 등록 또는 게시.)
2. **APIs & Services → Credentials → Create Credentials → OAuth client ID → Application type: `Desktop app`** ⚠️(Web application 아님) → 생성 → **Client ID·Client Secret 복사**.
3. Secret 등록 (Git Bash):
   ```
   GOOGLE_OAUTH_CLIENT_SECRET="복사한secret" bash functions/scripts/set-secrets.sh
   ```
4. `web/functions/.env.mcphoto-955fb`에 추가:
   ```
   GOOGLE_OAUTH_CLIENT_ID=<복사한 Client ID>
   ```
   → `firebase deploy --only functions` 재배포. (**id·secret 하나만** 설정하면 서버가 부분구성 오류로 전 요청 실패하니 둘 다.)
5. 배포 PC `MCPhoto.ini` `[MCPhoto]`에 `GoogleClientId=<Client ID>` 추가(secret은 클라에 넣지 않음).

**성공 판정**: 로그인 화면에 "Google로 로그인" 노출 + 등록·검증된 운영자 계정으로 로그인 성공.

---

## 빠른 참조
| 상황 | 명령 |
|------|------|
| 클라 API 키 재확인 | `firebase functions:secrets:access CLIENT_API_KEYS --project mcphoto-955fb` |
| 배포 스모크 | `node functions/scripts/post-deploy-smoke.mjs` (BASE_URL·API_KEY 지정) |
| 즉시 롤백 | `MCPhoto.ini`의 `UseBackend=false` |
| 함수 로그 | `firebase functions:log --project mcphoto-955fb` |
