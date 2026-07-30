# 05 · 저장·영속 (설정 · 프레임 · 세션 · 결과물 · 로그)

| 항목 | 값 |
|------|-----|
| 문서 | 웹 클라이언트가 기기에 저장하는 모든 것의 스키마·위치·수명·정리 규칙 |
| 규격 진실원 | **`docs/analysis/41-local-data-and-file-formats.md`** — 설정 키·기본값·범위·보정 규칙, `.slots` 포맷, 공용/개인 구분 의미, 결과물 파일명, 세션 공간 수명은 그 문서가 계약이다 |
| Windows 참조 | `src/MCPhoto.Core/Settings/{AppSettings,IniSettingsService}.cs`, `src/MCPhoto.Core/Frames/LocalFrameStore.cs`, `src/MCPhoto.Core/LocalSave/LocalSaveService.cs` |
| 갱신 규칙 | 설정 키·프레임 포맷이 바뀌면 `docs/analysis/41`을 먼저 고친다. 저장 매체·스키마 버전만 바뀌면 이 문서만 갱신 |

---

## 1. 무엇을 어디에 두는가

`analysis/41 §1`이 정한 계약/자유 경계를 지킨다: **키 이름·의미·기본값·범위·포맷은 계약**, **저장 매체·위치는 플랫폼 자유**.

| 용도 | Windows | 웹 | 수명 |
|------|---------|-----|------|
| 클라이언트 설정 | `MCPhoto.ini`(3단 폴백) | **localStorage** `mcphoto.settings.v1` | 영구(브라우저 저장소 정책 하) |
| 로컬 프레임 메타 | `Frame\*.slots` | **IndexedDB** `mcphoto` / `frames` | 영구 |
| 로컬 프레임 이미지 | `Frame\*.png` | **OPFS** `frames/{id}.png` | 영구 |
| 서버 프레임 이미지 캐시 | 같은 `Frame\` 폴더 | **OPFS** `frames/{id}.png` + IndexedDB 메타(`scope: "public"`) | 영구(이름 dedup) |
| 세션 임시 작업물 | `%ProgramData%\MCPhoto\sessions\{guid}\` | **OPFS** `sessions/{sessionId}/` | 세션 종료·앱 시작 시 정리 |
| 결과물 영구 보관 | `{실행경로}\result\mcphoto_YYMMDD_HHMM\` | **OPFS** `results/mcphoto_YYMMDD_HHMM/` (+ 선택된 실제 폴더) | 영구 + 용량 정책(§5.4) |
| 로그 | `%ProgramData%\MCPhoto\logs\*.log` | **IndexedDB** `logs`(링버퍼) | 14일 또는 5,000건 |
| 브랜딩·버전 | `branding.ini` / `bldinfo.ini` | **`/branding.json` fetch** + 빌드 상수 | 세션(매 시작 로드) |
| **JWT** | 메모리 | **메모리 전용 — 저장 금지**(M2) | 페이지 수명 |
| PIN 잠금 해제 시각 | (없음) | localStorage `mcphoto.pinLock.v1` | 5분(WD16) |
| 결과 폴더 핸들 | (해당 없음) | IndexedDB `handles`(FileSystemDirectoryHandle) — **Chromium 전용**(§5.3) | 권한 유지되는 동안 |

### 1.1 왜 IndexedDB와 OPFS를 섞는가

| 매체 | 잘하는 것 | 쓰는 곳 |
|------|-----------|---------|
| **localStorage** | 동기 읽기, 작은 키-값 | 설정(부트스트랩에서 렌더 전에 필요) |
| **IndexedDB** | 구조화 데이터·인덱스·트랜잭션 | 프레임 메타·로그·핸들 |
| **OPFS** | **큰 바이너리, 파일 단위 접근·삭제, Worker에서 동기 쓰기** | 이미지·영상 |

큰 Blob을 IndexedDB에 넣으면 브라우저별 성능 편차가 크고 부분 삭제가 비효율적이다. **파일은 OPFS, 메타는 IndexedDB**가 원칙이다.

---

## 2. 클라이언트 설정

### 2.1 스키마

```jsonc
// localStorage["mcphoto.settings.v1"]
{
  "schemaVersion": 1,
  "values": {
    "CutCount": 6, "CountdownSec": 6, "MirrorMode": true, "FlashMode": false,
    "ShutterSound": false, "RetakeEnabled": false, "RetakeLimit": 1,
    "OutputFormat": "Jpg", "RetentionHours": 24,
    "EnableQrDelivery": true, "SendPhoto": true, "SendTimelapse": true,
    "FilterGrayscale": true, "FilterBrightness": true, "FilterBeauty": true,
    "SaveLocalCopy": true, "LocalSavePath": "",
    "DisplayMode": "Windowed", "WindowBounds": { "Left": null, "Top": null, "Width": 1280, "Height": 720 },
    "CameraDevice": "", "HostingBaseUrl": "https://mcphoto-955fb.web.app",
    "StorageBucket": "mcphoto-955fb.firebasestorage.app",
    "BackendBaseUrl": "https://asia-northeast3-mcphoto-955fb.cloudfunctions.net/api",
    "GoogleClientId": "…",
    "ExternalCameraEnabled": false, "PhotoPrinterEnabled": false
  },
  "webExtras": {
    "CameraDeviceLabel": "", "CameraDeviceGroupId": "", "CameraFacing": "user"
  }
}
```

| 규칙 | 내용 |
|------|------|
| **키 이름·기본값·범위는 `analysis/41 §2.1` 그대로** | 이름을 웹 관례(camelCase)로 바꾸지 않는다 — 내보내기 파일이 다른 클라이언트와 호환되어야 한다 |
| **미노출 키도 보존** | `DisplayMode`·`WindowBounds`·`ExternalCameraEnabled`·`PhotoPrinterEnabled`는 UI가 없지만 **읽고 그대로 다시 쓴다**(WD7·WD8) |
| `BackendApiKey` | **저장하지 않는다**(`analysis/41 §2.5`). 빌드 주입값만 사용 |
| `CameraDevice` | 웹에서는 **`deviceId` 문자열**(`analysis/41 §2.2`가 허용). 보조 매칭 정보는 `webExtras`에 |
| 스키마 버전 | 구조 변경 시 `schemaVersion` 증가 + 마이그레이션 함수. **알 수 없는 키는 무시하고 보존** |

### 2.2 로드·저장 규격

```
load():
  1. localStorage 읽기 → JSON.parse
     실패·부재 → 기본값 (경고 로그, 크래시 금지)
  2. 알 수 없는 키는 그대로 보존, 누락 키는 기본값 채움
  3. clamp() 적용 (domain/settings/appSettings.ts — Windows AppSettings.Clamp 이식)
  4. QR 정규화 (analysis/41 §2.4)
save(values) -> boolean:
  1. clamp() → QR 정규화
  2. 게스트 제한 키는 기록에서 제외 (§2.4)
  3. localStorage.setItem  — QuotaExceededError 등 예외를 잡아 false 반환
  4. 성공/실패를 반환한다 (성공 오인 금지 — M4)
```

| 항목 | 값 |
|------|-----|
| clamp 규칙 | `CutCount`∈{6,8,10} 최근접 — **단 sentinel `0`(자동)은 보정 제외**(it17. 가드가 없으면 저장 왕복 1회에 "자동"이 6으로 덮여 소멸한다. `-1` 등 다른 이탈값은 종전대로 6) · `CountdownSec`∈{3,6,8,10} 최근접 · `RetakeLimit`∈{1,2,3} 최근접 · `RetentionHours` 1~72 · `HostingBaseUrl` 트레일링 `/` **제거** · `BackendBaseUrl` 트레일링 `/` **부여** |
| 자동 컷 수 sentinel | `0`은 **허용 컷수 집합에 넣지 않는다**(넣으면 `CutCount=3` 오입력이 6이 아니라 0으로 보정된다 — 거리 동률 시 집합 첫 값 승리). 최근접 보정 **앞에 sentinel 가드**를 둔다(`analysis/41 §2.7`) |
| ⚠️ 두 URL 정규화 | **방향이 반대다.** 같은 함수로 처리하지 말 것(`analysis/41 §2.1` 경고) |
| 저장 실패 문구 | *"저장 위치에 쓸 수 없습니다."* (웹에서는 용량 초과·프라이빗 모드) |
| 파서 내구성 | 손상 값·알 수 없는 키는 무시하고 계속(예외 금지) |

### 2.3 QR 토글 정규화 (`analysis/41 §2.4`)

```
정규화(로드·저장 시):
  if EnableQrDelivery && !SendPhoto && !SendTimelapse:  EnableQrDelivery = false   # 하위 값 보존
재활성(화면에서 off → on 순간):
  SendPhoto = true; SendTimelapse = true
```
재활성 규칙은 **화면 로직에만** 있고, 설정 로드 중에는 억제한다.

### 2.4 게스트 편집 제한 (`analysis/41 §2.3`)

게스트가 편집할 수 없는 키: `MirrorMode` · `RetakeEnabled` · `RetakeLimit` · `FilterGrayscale` · `FilterBrightness` · `FilterBeauty` · `EnableQrDelivery` · `SendPhoto` · `SendTimelapse` · `HostingBaseUrl` · `StorageBucket`.

- 화면에는 **OFF 표시 + 비활성 + "로그인 필요"** 를 보이지만, **저장 시 이 키들을 기록하지 않아 기존 값이 보존**된다.
- **런타임 동작은 저장된 값(운영자 설정)대로** 유지된다. 제한되는 것은 편집 권한뿐이다.
- 이 게이트는 **화면 로직에만** 존재한다. 설정 모델은 전 필드를 항상 직렬화한다.

### 2.5 내보내기 / 가져오기 (WD17 — 웹 전용)

| 항목 | 규격 |
|------|------|
| 파일명 | `mcphoto-settings-{YYMMDD_HHMM}.json` |
| 내용 | §2.1 구조 그대로(단 `BackendApiKey`는 절대 포함하지 않는다) |
| 내보내기 | Blob → `<a download>`(same-origin blob이므로 정상 동작) |
| 가져오기 | `<input type="file">` → 파싱 → **clamp 후 미리보기 표시 → [적용]** (즉시 덮어쓰지 않는다) |
| 검증 | `schemaVersion`이 더 높으면 거부: *"더 새 버전의 설정 파일입니다."* |

---

## 3. 세션 작업 공간 (WD14 · `analysis/41 §4`)

```
OPFS/
  sessions/{sessionId}/
      cut1.jpg … cutN.jpg     ← 촬영 컷(quality 0.95)
      final.{jpg|png}         ← 최종 합성
      timelapse.mp4           ← 타임랩스
```

| 항목 | 규격 |
|------|------|
| 생성 시점 | 촬영 화면 진입 후 **카메라 Ready 이후** |
| 폴더명 | **세션 ID**(`{yyyyMMdd}_{HHmmss}_{UUIDv4}`) — Storage 경로와 일치해 디버깅이 쉽다 |
| 정리(정상) | 세션 종료·홈 복귀·리셋 시 **해당 폴더 삭제**(`removeEntry(name, {recursive:true})`) |
| 정리(잔재) | **앱 시작 시 `sessions/` 하위 전체 삭제**(비정상 종료 대비) — 규격이며 생략하면 임시 영상이 무한 누적된다 |
| **정리 비대상** | `results/`·`frames/`·로그는 **절대 지우지 않는다** |
| 개별 삭제 실패 | 무시하고 최대한 정리 |
| 쓰기 방식 | **전용 Worker에서 `createSyncAccessHandle()`이 기본 경로**다(§3.1). `createWritable()`은 **Chromium·Firefox 전용 폴백**이며 Safari에는 없다 |

### 3.1 OPFS 쓰기 경로 — Worker가 선택이 아니라 요구사항이다

| 사실 | 결과 |
|------|------|
| `FileSystemFileHandle.createSyncAccessHandle()`은 **전용 Worker(DedicatedWorkerGlobalScope)에서만** 호출할 수 있다(전 브라우저 공통 — 메인 스레드 블로킹 방지) | 메인 스레드에서는 이 API를 쓸 수 없다 |
| **Safari/WebKit은 `createWritable()`(`FileSystemWritableFileStream`)을 지원하지 않는다** | Safari에서 OPFS에 쓰는 방법은 **Worker + `createSyncAccessHandle` 하나뿐**이다 |
| ⇒ | **모든 OPFS 쓰기(컷 JPEG·final·timelapse·프레임 PNG·결과물)를 하나의 `opfsWriter` Worker 경계 뒤로 모은다.** 메인 스레드에서 `createWritable()`을 먼저 시도하는 구조로 만들면 **iOS/iPadOS에서 전 저장 경로가 실패**한다(M6-W 파손 → E8 실패) |

| 규칙 | 내용 |
|------|------|
| 구현 | `adapters/storage/opfsWriter.worker.ts` 1개. 메시지 API: `write(path, bytes)` · `remove(path, {recursive})` · `list(dir)` · `exists(path)`. `sessionWorkspace`·`resultSaver`·`frameStore`가 이 Worker를 공유한다 |
| 읽기 | 읽기(`getFile()`)는 메인 스레드에서도 되므로 Worker를 거치지 않아도 된다 |
| 폴백 | Worker가 `createSyncAccessHandle`을 못 쓰는 환경이면 **Worker 안에서** `createWritable()`을 시도한다(Chromium·Firefox). 둘 다 없으면 **OPFS 미지원**으로 판정하고 [10 §6.2](./10-testing-and-acceptance.md)의 축소 동작(촬영 전 경고)을 따른다 |
| 해제 | `SyncAccessHandle`은 **파일당 배타 잠금**이다. 쓰기 후 반드시 `flush()` → `close()`. 닫지 않으면 같은 파일의 다음 쓰기가 `NoModificationAllowedError`로 실패한다 |

---

## 4. 로컬 프레임 저장소 (WD4)

### 4.1 저장 대상 (`analysis/41 §3.1`)

| 무엇 | 서버에 올라가나 | 웹 저장 |
|------|-----------------|---------|
| 공용 기본 프레임(서버에서 받은 것) | 서버에 원본 존재 | **캐시**(`scope: "public"`, `dbId` 보유) |
| 번들 자산 프레임 | ✕ | 앱 자산(`/frames/*`) — 저장소에 복사하지 않는다 |
| 일반·고급 유저 커스텀 프레임 | **✕ — 로컬 전용** | **IndexedDB + OPFS**(`scope: "user"`) |
| power가 새로 만든 공용 프레임 | ○(`POST /frames`) | 서버 등록 + 로컬 캐시 |

### 4.2 IndexedDB 스키마

```jsonc
// DB "mcphoto" (version 1) / store "frames" (keyPath: "key")
{
  "key": "user:devmcjo:내프레임",       // 스코프 + 소유자 + 이름 (유일 키)
  "scope": "user",                      // "public" | "user"
  "ownerId": "devmcjo",                 // scope=user일 때만
  "name": "내프레임",                    // 표시 이름 (원문 그대로 — 정규화 금지)
  "id": "local:user:devmcjo:내프레임",  // 프레임 id(출처 판정용 — §4.4)
  "dbId": null,                         // 서버 문서 id(있으면) = .slots의 #dbid
  "imageFile": "frames/9f1c….png",      // OPFS 상대 경로
  "imageSize": { "width": 1200, "height": 1600 },
  "slots": [ { "index":0, "x":80, "y":140, "width":480, "height":640 } ],
  "createdAt": "2026-07-30T05:11:00.000Z",
  "updatedAt": "2026-07-30T05:11:00.000Z"
}
// 인덱스: by_scope(scope), by_owner(ownerId), by_name(name)
```

### 4.3 공용/개인 구분 (`analysis/41 §3.2`의 의미 유지)

Windows는 파일명 접두(`{계정}_{이름}`)로 구분한다. 웹은 **명시 필드(`scope`·`ownerId`)** 로 구분한다 — `analysis/41 §3.2` 주석이 명시적으로 허용하는 대체 방식이다.

| 유지해야 하는 의미 | 웹 구현 |
|--------------------|---------|
| 공용 = 전원 노출 | `scope === "public"` |
| 개인 = 본인만 노출 | `scope === "user" && ownerId === currentUser.id` |
| 이름 충돌 판정은 **스코프 안에서** | 키가 `scope:owner:name`이므로 자연히 스코프별 |
| **프레임 이름에 `_` 금지** | **그대로 유지한다**(서버가 400으로 거부하므로 계약) |
| 파일시스템 금지문자 `\ / : * ? " < > \|` 거부 | **그대로 유지**(치환 금지, 저장 거부). 내보낸 `.slots`/PNG가 Windows에서 열려야 한다 |
| 빈 이름 저장 거부 | 유지 |

### 4.4 프레임 id 접두 규약 (`analysis/13 §6.1` — 출처 판정의 근거)

| 출처 | id 형태 | 편집 | 로컬 삭제 |
|------|---------|------|-----------|
| 본인 로컬 생성분 | `local:` 접두 | 소유자 본인(쓰기 권한 필요) | ○ |
| 서버 공용 기본 | **접두 없는 실 서버 id** + `isDefault` | power만 | power만 |
| 번들 자산 | `bundle:` 접두 | ✕ | ✕ |
| 코드 생성 fallback | `fallback` 또는 빈 id | ✕ | ✕ |

`dbId` 유무 규약(`analysis/41 §3.3`)도 그대로 지킨다.

| 저장 상황 | `dbId` | 결과 id |
|-----------|:------:|---------|
| 서버 공용 프레임 캐시 | **기록** | 서버 문서 id 그대로 |
| power가 신규 생성해 서버 등록 | **기록** | 서버 문서 id |
| 개인 스코프 저장 | 없음 | `local:…` |
| 사본(fork) 저장 | **없음** | `local:…` — **서버 문서와 연결이 끊긴다**(의도) |

### 4.5 `.slots` 텍스트 포맷 (내보내기·가져오기 호환 — 계약)

내부 저장은 IndexedDB지만, **내보내기·가져오기에서는 `analysis/41 §3.3`의 텍스트 포맷을 그대로 쓴다.** 그래야 Windows 앱과 프레임을 주고받을 수 있다.

```
#imagesize=1200,1600
#dbid=8f2c1a90-3d5e-4b17-9c22-0ab7de441f03
0,80,140,480,640
1,640,140,480,640
```

| 파싱 규칙 | 내용 |
|-----------|------|
| 인코딩·줄 종결자 | UTF-8, `\n` |
| 메타 키 비교 | **대소문자 무시** |
| 슬롯 줄 | 정확히 5개 정수(콤마 구분)일 때만 채택 |
| 형식 위반 줄·기타 `#` 줄 | **무시하고 계속**(예외 금지) |
| `#imagesize` 없음 | 크기 0(방어적 기본값) |
| 구현 위치 | `domain/frames/slotsFile.ts`(순수 함수, 단위 테스트 필수) |

### 4.6 내보내기 / 가져오기 번들

| 항목 | 규격 |
|------|------|
| 파일 | `mcphoto-frames-{YYMMDD_HHMM}.zip` — 항목마다 `{이름}.png` + `{이름}.slots`(공용) / `{계정}_{이름}.png` + `.slots`(개인) |
| 파일명 규칙 | **Windows 앱의 `Frame\` 폴더에 그대로 풀어 넣으면 인식되는 형태**로 만든다(접두 규칙 준수) |
| 가져오기 | zip 해석 → 이름·슬롯 검증 → 10개 상한 확인 → 충돌 시 사본 이름 규칙 적용 |
| zip 구현 | 순수 JS(압축 없이 `store` 방식이면 60줄로 자체 구현 가능 — 의존성 최소화) |

### 4.7 프레임 삭제 (`analysis/41 §3.4`)

```
deleteLocal(frame):
  1. OPFS 이미지 파일 존재 확인. 없으면 실패(false)
  2. IndexedDB 레코드 삭제
  3. OPFS 이미지 파일 삭제
  4. 성공 판정 = "이미지 파일이 실제로 사라졌는가" (getFileHandle이 NotFoundError)
```

| 규칙 | 내용 |
|------|------|
| 성공 판정 | **실제 부재로 확인**한다. 예외를 삼키고 성공으로 보고하지 않는다(M4) |
| 파일 잠금 | 웹에는 파일 잠금 문제가 없다(Windows의 알려진 결함이 웹에서는 성립하지 않는다). 단 **썸네일 `ImageBitmap`은 `close()`** 해 둔다 |
| 서버 삭제 | power + "서버에서도 제거" 체크 시 `DELETE /frames/{id}` → 실패 시 **이름 매칭 재시도** → 결과를 명확히 안내 |
| `{deleted:false}` | **성공이 아니다** |

### 4.8 계정당 상한

로컬 프레임 **최대 10개**(`scope:"user"`, `ownerId` 기준). 초과 시 저장 거부.

---

## 5. 결과물 로컬 보관 (WD3 · M6-W)

### 5.1 3계층 구조

```
[합성 완료]
   │
   ├─ ① OPFS results/mcphoto_YYMMDD_HHMM/ 에 기록          ← 필수 · 제스처 불요 · 업로드 이전 (M6-W)
   │      실패 → 실패 토스트 + 로그 (SaveLocalCopy 실패로 처리)
   │
   ├─ ② 폴더 핸들이 있으면 그 폴더에도 기록                  ← 데스크톱 Chromium만
   │      권한 상실 → 안내 + ①만 유지
   │
   └─ [업로드 분기]
          │
          └─ ③ 결과·QR 화면의 [기기에 저장] → 브라우저 다운로드   ← 사용자 제스처 필요
```

| 계층 | 무엇 | 어디서 되나 |
|------|------|-------------|
| ① OPFS | 앱 내부 영구 보관. 설정 화면의 [보관된 결과물] 목록에서 열람·내보내기·삭제 | **모든 대상 브라우저** |
| ② 실제 폴더 | 운영자가 1회 선택한 폴더(예: `D:\photobooth\result`)에 파일이 그대로 생긴다 = Windows와 동등 | **Chromium 데스크톱**(Windows·macOS) |
| ③ 다운로드 | 손님이 즉석에서 자기 기기에 받기 | 모든 브라우저 |

### 5.2 폴더 구조·파일명 (`analysis/41 §5` 계약)

| 항목 | 값 |
|------|-----|
| 세션 폴더명 | **`mcphoto_YYMMDD_HHMM`** (예 `mcphoto_260730_1445`) |
| 충돌 처리 | 같은 폴더가 있으면 `-2`, `-3` … 접미 |
| 파일 | `final.{jpg\|png}` + `timelapse.mp4`(있을 때만) |
| 만료 | **없다 — 영구 보관**(서버 TTL과 무관) |
| 실행 시점 | **업로드 시도 이전**(M6-W) |
| 실패 처리 | 예외가 아니라 실패 반환 + 오류 표시. **촬영 흐름을 중단하지 않는다** |

### 5.3 폴더 핸들 영속(②)

| 항목 | 규격 |
|------|------|
| 선택 | 설정 → [로컬 저장 폴더 선택] → `showDirectoryPicker({ mode: "readwrite", startIn: "documents" })` |
| 저장 | 핸들을 IndexedDB `handles` 스토어에 저장(구조화 복제 가능) — **Chromium 전용 능력**이다. `showDirectoryPicker`(File System Access API)와 **"사용자 폴더 핸들의 IndexedDB 영속"** 은 둘 다 Chromium에만 있다(Safari·Firefox는 `showDirectoryPicker` 자체가 없다). 따라서 ② 계층 전체가 Chromium 데스크톱 한정이며, **`window.showDirectoryPicker` 기능 감지 1개로 UI·저장·복원 경로를 통째로 켜고 끈다** |
| 재사용 | 앱 시작 시 `handle.queryPermission({mode:"readwrite"})` → `"granted"`면 사용 |
| `"prompt"` | **자동 요청하지 않는다**(제스처 필요) → 설정 화면에 *"저장 폴더 권한을 다시 허용해 주세요 [허용]"* 배너. 그 버튼에서 `requestPermission()` |
| `"denied"` | 핸들 폐기 + ①만 사용 + 안내 |
| 미지원 브라우저 | 버튼을 렌더하지 않고 안내: *"이 브라우저에서는 폴더 저장을 지원하지 않습니다. 결과물은 앱 내부에 보관되며 [기기에 저장]으로 내보낼 수 있습니다."* |
| `LocalSavePath` 설정 키 | 선택된 폴더 **이름**만 표시용으로 기록한다(실 경로는 브라우저가 노출하지 않는다). 값이 경로가 아님을 UI에 명시 |

### 5.4 용량 정책 (웹 전용 — Windows에 없는 것)

Windows는 무기한 영구 보관이지만(`analysis/41 §5` 개선 항목), 브라우저는 할당량이 있으므로 **정리 정책을 처음부터 둔다.**

| 규칙 | 값 |
|------|-----|
| 상한 | OPFS `results/` 총량 **2GB** 또는 **최근 200세션** 중 먼저 도달하는 것 |
| 초과 시 | **오래된 세션부터 삭제**하고 로그에 남긴다. 삭제 사실을 진단 화면에 표시 |
| 사용자 조작 | 설정 → [보관된 결과물]에서 목록·용량 확인, 개별·전체 삭제 |
| 경고 | `navigator.storage.estimate()`의 여유가 **10% 미만**이면 설정·진단에 경고 배지 |
| ②(실제 폴더) | **정리 대상이 아니다**(사용자 파일 시스템을 앱이 지우지 않는다) |

### 5.5 저장소 영속성 요청

```ts
if (navigator.storage?.persist) {
  const persisted = await navigator.storage.persisted();
  if (!persisted) await navigator.storage.persist();   // 결과를 로그·진단에 기록
}
```

| 브라우저 | 기대 결과 |
|----------|-----------|
| Chromium | 설치(PWA)·상호작용 이력에 따라 승인. 미승인도 정상 동작 |
| Firefox | 사용자 프롬프트 표시 가능 |
| Safari/WebKit | `persist()`가 없거나 `false`. **PWA 설치가 실질적 대안**(§00 §3.2) |

**결과를 진단 화면에 정직하게 표시**한다: "영속 승인됨" / "미승인 — 장기 미사용 시 삭제될 수 있음".

---

## 6. Windows 앱과의 데이터 호환

| 데이터 | 호환 방식 |
|--------|-----------|
| 설정 | JSON 내보내기 → (수동) INI로 옮기려면 키 이름이 같으므로 1:1 대응 가능. **자동 변환 도구는 범위 밖** |
| 프레임 | **zip 번들이 Windows `Frame\` 폴더 규칙과 동일**하므로 풀어 넣으면 그대로 인식된다(§4.6) |
| 결과물 | 파일명·폴더명 규칙 동일(`mcphoto_YYMMDD_HHMM/final.jpg`) |
| 서버 데이터(계정·공용 프레임·결과 세션) | **완전 공유**(같은 백엔드) |

---

## 7. 로그 (WD6 · `analysis/41 §8`)

### 7.1 스키마

```jsonc
// IndexedDB "mcphoto" / store "logs" (autoIncrement key)
{ "ts": 1785000000000, "level": "info", "msg": "홈 복귀: 사용자 취소", "ctx": { "screen": "Result" } }
// 인덱스: by_ts(ts)
```

| 항목 | 규격 |
|------|------|
| 최소 레벨 | **Information**(info / warn / error / fatal) |
| 보관 | **14일 또는 5,000건** 중 먼저 걸리는 기준으로 오래된 것부터 폐기 |
| 쓰기 | 비동기 배치(1초 또는 20건마다 flush) — 로깅이 촬영 성능을 깎지 않게 |
| 종료 시 flush | `pagehide`·`visibilitychange(hidden)`에서 강제 flush |
| 노출 | 진단 모달에서 최근 N건 조회 + **[로그 내보내기]**(`mcphoto-log-{YYMMDD_HHMM}.log`, 텍스트) |
| **금지 항목** | JWT · 배포 게이트 키 · 인가 코드 · `code_verifier` · `state` · `nonce` · **PIN** |
| 콘솔 | 개발 빌드에서만 콘솔에 미러링. 운영 빌드는 IndexedDB만 |
| 유지해야 하는 성질 | **"현장에서 운영자가 로그를 꺼낼 수 있다"**(`analysis/41 §8`) — 내보내기가 그 역할 |

### 7.2 무엇을 반드시 로깅하나

| 시점 | 항목 |
|------|------|
| 부트스트랩 | 버전·브랜딩 로드 결과·설정 로드/보정·저장소 영속 결과·세션 잔재 정리 수 |
| 카메라 | 시작 요청(장치·종횡비·거울)·실제 획득 해상도·fps·Ready 소요·실패 사유 |
| 촬영 | 세션 ID·컷 수·컷별 소요·[바로 촬영] 사용·취소 사유 |
| 타임랩스 | 선택된 인코더 경로·stride·수집 프레임 수·출력 길이·바이트·실패 사유 |
| 합성 | 프레임 id·슬롯 수·필터·소요 시간·출력 바이트 |
| 저장 | OPFS/폴더 기록 성공 여부·경로·정리로 삭제된 세션 |
| 업로드 | prepare/PUT/commit 각 단계 결과·상태코드·error.code·세션 ID(**URL·토큰은 남기지 않는다**) |
| 인증 | 로그인 시작·성공(계정 id·역할)·실패 분류(401/501/네트워크)·로그아웃·**토큰 폐기 확인** |
| 권한 | 403 발생 지점과 액션 이름 |
| 예외 | 전역 예외·거부된 화면 전이·어댑터 실패 |

---

## 8. 브랜딩·버전 (WD13 · `analysis/41 §6·§7`)

### 8.1 `/branding.json`

```jsonc
{ "AppName": "MC Photo", "Subtitle": "self custom photobooth" }
```

| 항목 | 규격 |
|------|------|
| 위치 | Hosting 루트(`webclient/public/branding.json`) — **운영자가 파일만 교체**하면 재빌드 없이 바뀐다 |
| 로드 시점 | **첫 화면 렌더 전**(부트스트랩 3단계), 타임아웃 **800ms** |
| 캐시 | `Cache-Control: no-cache`(§01 §5.1) |
| 폴백 | 부재·실패·빈 값 → `AppName = "MC Photo"`, `Subtitle = "self custom photobooth"`. **두 값은 독립적으로 폴백** |
| 실패 처리 | 어떤 실패에도 크래시 금지 |
| 적용 지점 | 문서 `<title>`, 홈 타이틀, 홈 소제목 |
| 인코딩 | UTF-8(한글 이름 대응) |

### 8.2 버전 표기 (it18 반영)

| 항목 | 규격 |
|------|------|
| 값 | 빌드 상수 `VITE_APP_VERSION`(+`VITE_BUILD_DATE`는 진단 전용) |
| 표시 문자열 | **`v{Version}`** (예 `v1.2.0`) — **배포 채널(`Site`) 표기는 폐기됐다**(it18: 운영 환경이 하나라 정보량 0. 환경이 늘면 그때 도입) |
| **BuildDate는 하단 표기 제외** | 업데이트 지연 시 오래된 앱으로 보일 위험. **진단·개발자 문의 카드에서만** 노출(`analysis/41 §7`) |
| 표시 위치 | 화면 하단 흐린 캡션, **로그인 무관 상시**, 클릭 비간섭 |
| 폴백 | 값 부재 시 `v0.0.0` |
| 진단 표시 | 버전 · Build Date · Web Deploy Date(`/health deployedAt`) · Service Worker 상태 |
| Windows 대응 | Windows도 it18부터 외부 파일(`bldinfo.ini`) 없이 **빌드 산출물 자신**(어셈블리 리소스)에서 읽는다 — 웹의 빌드 상수 방식과 방향이 같다 |

---

## 9. 이식 체크리스트

`analysis/41 §10` + 웹 항목.

- [ ] 설정 **키 이름·기본값·유효 범위**가 `analysis/41 §2.1`과 일치한다
- [ ] 범위를 벗어난 값이 **로드·저장 양쪽**에서 보정된다
- [ ] `HostingBaseUrl`(제거) / `BackendBaseUrl`(부여) 정규화가 **다르게** 구현됐다
- [ ] QR 정규화·재활성 규칙이 구현됐다
- [ ] 게스트 제한 항목이 **저장 시 기록되지 않아 기존 값이 보존**된다
- [ ] 게이트 키가 저장·표시·로그에 남지 않는다
- [ ] 설정 저장 실패가 사용자에게 표시된다(성공 오인 금지)
- [ ] `.slots` 내보내기/가져오기가 `analysis/41 §3.3` 포맷을 지킨다(손상 줄 무시 포함)
- [ ] `dbId` 유무 규약이 §4.4 표대로 결정된다(사본은 기록하지 않음)
- [ ] 공용/개인 구분과 **프레임 이름 `_` 금지**가 구현됐다
- [ ] 프레임 삭제 성공 판정이 **실제 부재 확인**이다
- [ ] **모든 OPFS 쓰기가 Worker(`createSyncAccessHandle`) 경계를 지난다**(§3.1 — Safari에서 `createWritable()`이 없다)
- [ ] `SyncAccessHandle`이 쓰기 후 `flush()` → `close()`된다(배타 잠금 해제)
- [ ] 앱 시작 시 `sessions/` 잔재를 정리하고 **`results/`·`frames/`·로그는 건드리지 않는다**
- [ ] 결과물 보관이 **업로드 이전에 완료**되고 실패해도 흐름을 막지 않는다(M6-W)
- [ ] `results/` 용량 정책이 동작하고 삭제 사실이 로그·진단에 남는다
- [ ] 로그에 시크릿·토큰·PIN이 없다
- [ ] JWT가 어떤 저장소에도 기록되지 않는다(M2 — 코드 검색으로 확인)
