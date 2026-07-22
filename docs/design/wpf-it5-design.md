# MC포토 — 이터레이션 5 설계 (계정 구조·WYSIWYG 프리뷰·QR·설정 레이아웃)

| 항목 | 값 |
|------|-----|
| 문서 | WPF 이터레이션 5 설계 본문 |
| 작성일 | 2026-07-21 |
| 상태 | 초안 v1 (구현 착수 전) |
| 1차 준거 | `docs/prd/iteration-5-account-preview-qr.md` |
| 참고 이미지 | `E:\Study\photobooth\example\setting_ng.png`(설정 라벨 잘림) |
| 상위 준거 | it2(라이트 A)·it3(세션 단일소스·QR/Save)·it4(편집기 좌표·종횡비·유휴), PRD v2.7 §9 |
| 구현 WBS | `docs/design/wpf-it5-wbs.md` |
| 코드 베이스 | `E:\Study\photobooth\src\` (it2~it4 구현 반영 완료) |

> 이터레이션 5는 사용자가 it4 빌드 테스트 + 스크린샷으로 준 피드백을 다룬다. **P1 버그(B6 QR on 업로드 실패의 우아한 처리, B7 프리뷰 WYSIWYG)**, **P2 설계 변경(B8 촬영후 로그인 유지)**, **P3 계정/설정 구조 개편(C1·C2)**, **P4 UI/UX(U7 설정 레이아웃, U8 로그인 포커스, F1 슬롯 크기 슬라이더)**. 신규 촬영 기능 없음.
> ⚠️ **B6 정정(2026-07-21)**: 초안 v1은 "QR off 미존중"으로 봤으나, 오케스트레이터 서비스계정 진단 결과 **QR은 ON이고 Storage 버킷 부재(404)로 업로드가 실제 실패**하는 것이 실체다. B6은 "실패 우아 처리 + 버킷 설정 경로 + Blaze 외부 전제"로 재정의됐다(§2).

---

## 0. 검증된 사실 / 미검증 가정

### 검증된 사실 (코드 인스펙션으로 직접 확인)

- **VF-1. `ResultViewModel.Next`는 `EnableQrDelivery`를 정확히 체크한다**: `if (settings.EnableQrDelivery) Navigate(Qr) else Navigate(Done)`(`ResultViewModel.cs:126-129`). off면 Qr로 안 감. 로컬 저장은 **QR 분기 이전에** 수행(`:116-123`, saveLocalCopy on일 때). → QR 분기 로직은 정상. (근거: `ResultViewModel.cs`)
- **VF-2. 실패 팝업 문구는 `QrPopupViewModel.OnEnterAsync`에서만 나온다**: "전송에 실패했습니다. 네트워크 또는 Firebase 설정을 확인해 주세요."(`QrPopupViewModel.cs:73`). 이 화면은 진입 즉시 무조건 `_upload.UploadResultAsync`를 시도(`:54`)하고 실패 시 팝업 + 화면에 머무름(흐름 차단). **QR on일 때 업로드가 실제 실패하면 이 위협적 문구가 뜨고 완료로 진행 못 한다.** (근거: `QrPopupViewModel.cs:39-88`)
- **VF-3. 로컬 저장은 QR 성공/실패와 독립·선행이다**: `ResultViewModel.Next`가 타임랩스 생성 → **로컬 저장(saveLocalCopy on)** → QR 분기 순으로 진행(`:104-129`). 즉 **saveLocalCopy on이면 업로드가 실패해도 결과물은 이미 로컬에 보존**(손실 0). (근거: `ResultViewModel.cs:104-129`, `ILocalSaveService`)
- **VF-4. 캡처 스틸은 이미 슬롯 종횡비로 중앙 크롭돼 저장된다**: `CaptureViewModel.OnEnterAsync`가 `aspect = frame.Slots[0].AspectRatio`로 `_camera.StartAsync(deviceIndex, aspect, mirror)` 호출(`CaptureViewModel.cs:50-52`), 카메라 파이프라인이 `CropCalculator.CenterCrop`로 프레임당 크롭(architecture §2.2). → **컷 원본(`CapturedStill`)은 슬롯 종횡비**다. (근거: `CaptureViewModel.cs:50-52`, `CropCalculator.cs`)
- **VF-5. 컷 선택 썸네일이 고정 4:3 컨테이너 + UniformToFill이다**: `CutSelectView.xaml`의 썸네일 `Border Width=220 Height=165`(=4:3 고정) + `Image Stretch=UniformToFill`(`:33,38`). 컷이 3:4(세로 슬롯)여도 4:3 컨테이너에 채워져 **위아래가 잘려 슬롯과 다른 모양**으로 보인다. → **B7의 직접 원인**: 썸네일 컨테이너가 슬롯 종횡비를 안 따르고 UniformToFill로 재크롭. (근거: `CutSelectView.xaml:33-38`)
- **VF-6. `CropCalculator.CenterCrop`는 순수 함수로 정확**: targetAspect 기준 중앙 크롭 Rect 산출, 왜곡 없음(`CropCalculator.cs`). 크롭 로직 자체는 문제 없음 — 표시 컨테이너 비율 불일치가 B7. (근거: `CropCalculator.cs`)
- **VF-7. B8: 세션 완료·유휴가 `clearUser:true`(로그아웃)다**: `DoneViewModel`의 자동 복귀(`:21`)·GoHome(`:35`)이 `ReturnHome(..., clearUser:true)`, 유휴 타임아웃도 `clearUser:true`(`AppShellViewModel.cs:192`). `ReturnHome(reason, clearUser=false)` 기본값(`:171`). → B8은 **Done의 두 곳을 false로**, 유휴는 true 유지. (근거: `DoneViewModel.cs:21,35`, `AppShellViewModel.cs:171,192`)
- **VF-8. 계정 기능이 설정 페이지에 있다(C1 대상)**: `SettingsView.xaml`의 [계정] 섹션(비번 변경, `:117-130`)·[관리자] 섹션(계정 생성·사용자 관리·앱 종료, `:132-159`). `SettingsViewModel`에 `ChangePassword`·`CreateAccount`·`CreatableRoles`·`OpenUserManagement` 커맨드. → C1은 이 섹션들을 설정에서 제거, C2는 계정 전용 페이지로 이전. (근거: `SettingsView.xaml`, `SettingsViewModel.cs`)
- **VF-9. 계정 메뉴는 팝오버(`MainWindow`)로 존재**: 상단 바 계정 팝오버에 [비밀번호 변경]·[관리자 설정]·[로그아웃](`MainWindow.xaml`). [비밀번호 변경]·[관리자 설정]이 현재 `OpenAccountSettingsCommand`로 **설정 페이지**로 보낸다(`AppShellViewModel.cs:221-224`). → C2는 이들을 **계정 전용 페이지**로 보내도록 변경. (근거: `MainWindow.xaml`, `AppShellViewModel.cs:214-232`)
- **VF-10. 설정 라벨 잘림 원인**: `SettingsView.xaml`의 `RowLabel` 스타일 `Width=240` 고정(it4 §5는 200 근처 권장했으나 반영 안 됨) + 카드 `MaxWidth=720` 중앙 스택. 스크린샷에서 라벨("촬영 컷 =", "컷당 카ᶕ", "카메라 ᶕ", "표시 ᶕ")이 값 컨트롤에 눌려 잘림 — 라벨폭+컨트롤폭이 카드 폭을 초과하거나 라벨 `TextTrimming`. → U7. (근거: `setting_ng.png`, `SettingsView.xaml` `RowLabel`)
- **VF-11. 로그인 화면 자동 포커스 없음**: `LoginGuestView.xaml.cs`는 생성자·PasswordChanged·KeyDown만, 진입 시 아이디 TextBox 포커스 코드 없음(`LoginGuestView.xaml.cs`). → U8. (근거: `LoginGuestView.xaml.cs`)
- **VF-12. it4 편집기 자산 반영됨**: `EditorTransform`(Core, F↔C 변환 순수함수) 존재, `FrameEditorViewModel`에 `SlotAspect`(4:3/3:4/1:1)·`AutoArrange(targetAspect)` 반영(`FrameEditorViewModel.cs:35-39,102-108`). 슬롯 크기 조절 슬라이더는 없음(F1 신규). (근거: `EditorTransform.cs`, `FrameEditorViewModel.cs`, `SlotLayout.cs:15-23`)
- **VF-13. 기존 테스트 자산**: `SettingsTests`·`AppStateTests`·`SlotLayoutTests`·`EditorTransformTests`·`CropCalculatorTests` 존재. (근거: `tests/MCPhoto.Tests/`)
- **VF-14. B6 실체 = QR on인데 Storage 버킷이 없어 업로드가 실제 실패한다**: 오케스트레이터가 서비스 계정으로 진단 — 프로젝트에 GCS/Storage 버킷이 **하나도 없음**(버킷 목록 0), `mcphoto-955fb.appspot.com`·`mcphoto-955fb.firebasestorage.app` 둘 다 **404**. Storage 미프로비저닝이라 `FirebaseClient.UploadFileAsync`(GCS `UploadObjectAsync`)가 404로 실패 → `QrPopupViewModel`에서 예외 catch → 위협적 팝업. **"off 무시"가 아니라 "on인데 실패"가 실체.** 버킷 생성은 Blaze(결제 계정) 필요(2026-02 정책) — **코드로 해결 불가, 사용자 결정 대기(외부 전제)**. (근거: 오케스트레이터 진단, `FirebaseClient.cs:106-124`)
- **VF-15. 버킷 주입 경로는 이미 존재한다**: `ServiceRegistration`이 `AppSettings.StorageBucket`을 `FirebaseClient(bucket:)`로 주입(it3), 미지정 시 `FirebaseClient`가 `{project}.appspot.com` 레거시 규약으로 유도 + 경고 로그(`FirebaseClient.cs:64-76`). 신규 프로젝트는 `*.firebasestorage.app`이라 미지정 시 불일치. → B6 수정은 이 경로를 **재확인·문서화**(신규 규약 지정 가능하게)하면 됨(신규 코드 최소). (근거: `FirebaseClient.cs:38-78`, `ServiceRegistration.cs`)

### 미검증 가정 (구현 시 검증 — WBS Step 매핑)

- **OA-1. QR on 업로드 실패를 우아하게 처리하면(로컬 보존+정상 완료+비위협 안내) 세션이 팝업에 막히지 않고, saveLocalCopy on일 때 결과물 손실이 0이다** → 실패 처리 로직 + saveLocalCopy 선행(VF-3) 확인 → **검증: WBS Step 1**(실패 경로 단위 테스트 + 사용자 재현). ⚠️ 실제 QR 전송 성공은 **Blaze 전환 + 버킷 생성(외부 전제)** 후에만 가능 — 이번 이터레이션 코드 범위 밖.
- **OA-2. 썸네일 컨테이너를 슬롯 종횡비로 맞추면 컷 원본(이미 슬롯 비율, VF-4)이 왜곡 없이 표시된다** → **검증: Step 2**(빌드 + 사용자 육안).
- **OA-3. Done clearUser:false 전환이 유휴 로그아웃·다음 손님 흐름을 깨지 않는다** → **검증: Step 3**(정책 테스트).
- **OA-4. 계정 전용 페이지(신규 상태) 추가가 상태머신·오버레이 네비와 정합한다** → **검증: Step 4**(AppStateTests).
- **OA-5. 슬롯 일괄 스케일(70~130%)이 중심 유지·경계 클램프·종횡비 유지로 동작한다** → **검증: Step 7**(SlotLayout 스케일 단위 테스트).

---

## 1. 요구 → 설계 매핑 (한눈에)

| 요구 | 근본 원인/현황(VF) | 설계 조치 | WBS Step |
|---|---|---|---|
| **B6** QR on 업로드 실패(Storage 버킷 부재) | QR **on**이 정상, Storage 미프로비저닝으로 업로드가 실제 실패(VF-14). 팝업이 흐름 차단·문구 위협적(VF-2) | 실패 우아 처리(로컬 보존+정상 완료+비위협 안내+재시도), 버킷 설정 경로 재확인, Blaze 외부 전제 명시 | §2, Step 1 |
| **B7** 프리뷰 WYSIWYG | 컷 원본은 슬롯비율(VF-4)이나 썸네일이 4:3 고정+UniformToFill(VF-5) | 썸네일·프리뷰 컨테이너를 슬롯 종횡비로, Uniform 표시 | §3, Step 2 |
| **B8** 촬영후 로그인 유지 | Done·유휴가 clearUser:true(VF-7) | Done 두 곳 clearUser:false, 유휴만 true 유지 | §4, Step 3 |
| **C1** 설정에서 계정 제거 | 설정에 계정/관리자 섹션(VF-8) | SettingsView/VM에서 계정·관리자 섹션 제거(앱설정만) | §5, Step 4 |
| **C2** 계정 전용 페이지 | 팝오버가 설정으로 보냄(VF-9) | 계정 항목별 전용 페이지(신규 상태 Account) + 역할 규칙 | §5, Step 4 |
| **U7** 설정 라벨 잘림·PC화 | RowLabel 240·MaxWidth720(VF-10) | 라벨 잘림 해결(폭·정렬·TextWrapping) + PC 밀도 | §6, Step 5 |
| **U8** 로그인 포커스 | 자동 포커스 없음(VF-11) | 진입 시 아이디 TextBox 포커스(Loaded/FocusManager) | §7, Step 6 |
| **F1** 슬롯 크기 슬라이더 | 슬라이더 없음(VF-12) | 70~130% 일괄 스케일 슬라이더+% 표시, 중심 유지·클램프 | §8, Step 7 |

---

## 2. B6 — QR on 업로드 실패의 우아한 처리 (정정: off 무시 아님)

> **정정(2026-07-21, 오케스트레이터 진단)**: 초안 v1은 "QR off 미존중"으로 봤으나 **QR은 ON이고 업로드가 실제로 실패**하는 것이 실체다(VF-14). 원인은 **프로젝트에 Storage 버킷이 없음**(버킷 목록 0, `.appspot.com`·`.firebasestorage.app` 둘 다 404). Storage 미프로비저닝이라 업로드가 404로 실패. 버킷 생성은 **Blaze(결제) 필요**로 코드 범위 밖(사용자 결정 대기). 따라서 B6은 "off 존중"이 아니라 **실패를 우아하게 처리(흐름 차단·위협 문구 제거)** + 버킷 설정 경로 재확인 + Blaze 외부 전제 명시로 재정의한다.

### 2.1 근본 원인

- **업로드 실패의 물리적 원인**: `FirebaseClient.UploadFileAsync`가 `_storage.UploadObjectAsync(bucket, ...)`를 호출하는데(`FirebaseClient.cs:106-124`), 대상 버킷이 존재하지 않아(404) 예외. `QrPopupViewModel.OnEnterAsync`가 이 예외를 catch해 위협적 문구("전송에 실패했습니다. 네트워크 또는 Firebase 설정을 확인해 주세요.")를 띄우고 **화면에 머물러 흐름을 막는다**(VF-2). 재시도해도 버킷이 없으니 계속 실패.
- **흐름 차단이 핵심 문제**: 촬영은 정상 끝났고 로컬 저장(saveLocalCopy on)도 이미 됐는데(VF-3, QR 분기 이전 수행), QR 실패 팝업이 세션을 완료로 못 넘어가게 한다. 사용자 경험상 "촬영이 실패한 것처럼" 보인다.

### 2.2 수정 설계 — 실패 우아 처리 (핵심, Blaze 무관하게 지금 반영)

1. **결과물 로컬 보존(손실 0)**: 로컬 저장은 이미 QR 분기 이전에 수행되므로(VF-3, `ResultViewModel.Next:116-123`), **saveLocalCopy on이면 업로드 실패해도 최종 이미지·타임랩스가 로컬에 남는다**. 이 순서를 계약으로 명문화(변경 불필요, 회귀 방지). saveLocalCopy off인데 QR도 실패하면 결과물이 사라지므로, **QR on일 때 saveLocalCopy 권장 안내**(설정 또는 문서) — 강제는 아님(사용자 선택).
2. **세션 정상 완료(팝업이 흐름 차단 금지)**: `QrPopupViewModel`이 업로드 실패 시 **비차단(non-blocking)**으로 처리 — 실패해도 **완료(Done)로 진행 가능**하게. 옵션:
   - **(A) 실패 시 자동 Done 진행 + 상단 토스트**: 업로드 실패 시 QR을 못 만들므로 QR 화면에 머물 이유가 없다. 실패를 짧게 안내(토스트/인라인) 후 [완료] 버튼 활성 또는 자동 Done. **재시도 버튼**은 유지(네트워크 일시 문제 대비)하되 실패가 완료를 막지 않음.
   - **(B) QR 화면 자체를 실패 상태 UI로**: 업로드 중/성공(QR)/실패(안내+재시도+완료) 3상태를 명확히. `UploadSucceeded`(기존)·`IsUploading`(기존) + 신규 `UploadFailed`로 분기. 실패 상태에서 [완료]·[재시도] 노출.
   - **채택: (B)** — 기존 `QrPopupViewModel`이 이미 `IsUploading`/`UploadSucceeded`/`StatusMessage`를 가지므로, `UploadFailed` 상태 + [완료]·[재시도] 버튼으로 확장. QrPopupView가 3상태를 표시.
3. **비위협·명확한 메시지**: 실패 문구를 "전송 실패 — 사진은 기기에 저장되었습니다"(saveLocalCopy on) 또는 "전송에 실패했습니다. 로컬 저장을 켜면 기기에 보관됩니다"(off)로. 위협적("네트워크 또는 Firebase 설정을 확인") 대신 **결과물 안전을 알리고 재시도 옵션 제공**. QR 성공 시에만 QR 노출(PRD §10 유지).
4. **QR 성공 시에만 QR 이미지**: 기존대로 업로드 성공 후에만 QR 생성·표시(§10). 실패 시 QR 없음 + 위 안내.

### 2.3 수정 설계 — 버킷 설정 경로 (Blaze 준비되면 동작)

- **`AppSettings.StorageBucket` 지정 시 그 값 사용**: `FirebaseClient`가 이미 `bucket` 인자를 받고 `ServiceRegistration`이 `AppSettings.StorageBucket`을 주입(VF-15). 미지정 시 `{project}.appspot.com` 레거시 유도 + 경고. → **신규 규약 `{project}.firebasestorage.app`을 `StorageBucket`에 넣으면 동작**하도록 경로가 이미 준비됨. 이번엔 이 경로를 **재확인·문서화**(설정 페이지 StorageBucket 항목 U7에 유지, 힌트 텍스트로 "예: mcphoto-955fb.firebasestorage.app").
- **미지정 시 경고 로그 유지**(기존). 버킷 부재/미지정을 진단 가능하게.
- 신규 코드 최소 — 주로 문서화·안내 + 실패 처리(2.2).

### 2.4 외부 전제 (설계 문서 명시)

- **실제 QR 전송이 되려면**: 사용자가 (1) Firebase 프로젝트를 **Blaze(종량제)로 전환**, (2) **Storage 버킷 생성**(신규 규약 `*.firebasestorage.app`), (3) `AppSettings.StorageBucket`에 그 버킷명 지정 — 3가지가 선행돼야 한다. 이는 **코드 범위 밖 외부 전제**(사용자 결제·콘솔 작업). 이번 이터레이션은 그 전까지도 **앱이 우아하게 동작**(실패해도 로컬 보존·완료 진행)하게 만드는 것이 목표.

### 2.5 검증 포인트 (headless)

- 단위(`QrPopupViewModel` 로직): 업로드 실패(목 `IUploadService`가 예외) 시 `UploadFailed==true`·`UploadSucceeded==false`, [완료] 진행 가능(Done 네비 호출), QR 이미지 없음. 성공 시 QR 생성·`UploadSucceeded==true`.
- `SettingsTests`: `StorageBucket` 저장/로드 라운드트립(신규 규약 문자열 보존).
- 로컬 보존: `ResultViewModel.Next`가 QR 분기 전에 로컬 저장 호출함을 확인(순서 계약 — 코드 리뷰/기존 `LocalSaveTests` 유지).
- 사용자 확인(육안): QR on + 버킷 없음(현 상태) → 위협 팝업 대신 "로컬 저장됨" 안내 + 완료 진행. saveLocalCopy on이면 결과물 로컬에 있음. (Blaze+버킷 후엔 QR 성공 — 외부 전제.)

---

## 3. B7 — 프리뷰/썸네일 WYSIWYG 크롭 (근본 원인 + 수정 설계)

### 3.1 근본 원인

라이브 프리뷰·촬영·합성은 카메라 파이프라인이 슬롯 종횡비로 중앙 크롭하므로 일관된다(VF-4). 문제는 **컷 선택 썸네일**: 컨테이너가 `220×165`(4:3) 고정이고 `Image Stretch=UniformToFill`이라, 컷 원본(이미 슬롯 비율)이 4:3 틀에 다시 채워지며 재크롭돼 슬롯과 다르게 보인다(VF-5). 슬롯이 3:4면 세로가 잘려 가로로 뭉개진다.

### 3.2 수정 설계

- **썸네일 컨테이너를 슬롯 종횡비로**: `CutSelectViewModel`에 `SlotAspectRatio`(= `Frame.Slots[0].AspectRatio` 또는 대표 슬롯 비율) 노출. `CutSelectView`의 썸네일 `Border`를 고정 크기 대신 **고정 폭 + 종횡비 유지 높이**(예: 폭 200, 높이 = 200/aspect). `Viewbox` 또는 `Grid`+비율 바인딩, 혹은 컨테이너에 종횡비 적용 컨버터.
- **`Stretch=Uniform`으로**: 컷 원본이 이미 슬롯 비율이므로 `UniformToFill`(재크롭) 대신 **`Uniform`**(왜곡·잘림 없이 전체 표시). 컨테이너 비율=컷 비율이면 여백도 안 생긴다.
- **일관 파이프라인 명문화**: "카메라 프리뷰·스틸·썸네일·합성이 모두 동일 `targetAspect`(대표 슬롯)로 중앙 크롭"을 계약으로 문서화. 프리뷰(`CameraFramePresenter`)와 썸네일이 같은 비율을 쓰도록. it4 B4의 슬롯 종횡비 선택이 이 `targetAspect`에 반영됨(VF-4 경로 그대로).
- **여러 슬롯 비율이 다를 때**: 현재 대표 슬롯(`Slots[0]`) 비율로 크롭(architecture §2.2). it4에서 편집기 전역 1개 비율이라 슬롯 비율이 균일 → 대표 슬롯으로 충분. (슬롯별 상이 비율은 확장 가정.)

### 3.3 검증 포인트 (headless)

- `CropCalculatorTests`(기존)로 크롭 Rect 정확성 유지 확인(회귀 없음).
- 썸네일 비율은 XAML 바인딩이라 육안 확인 항목. 단위 테스트는 `CutSelectViewModel.SlotAspectRatio`가 대표 슬롯 비율을 정확히 노출하는지(VM 로직).
- 사용자 확인(육안): 컷 선택 썸네일이 슬롯과 동일 종횡비·모양(잘림/늘임 없음), 라이브 프리뷰와 일치.

---

## 4. B8 — 촬영 종료 후 로그인 유지 (설계 변경)

### 4.1 변경

it3에서 "세션 완료(Done→Home) = 다음 손님 위해 로그아웃(clearUser:true)"으로 설계했으나, 사용자가 **촬영 후에도 로그인 유지**로 확정. 로그아웃은 계정 메뉴 수동 또는 유휴 타임아웃만.

- `DoneViewModel`: 자동 복귀(`:21`)·`GoHome`(`:35`)의 `ReturnHome(..., clearUser: true)` → **`clearUser: false`**. 세션 촬영 데이터(`Reset`의 프레임·컷·결과)는 **여전히 초기화**(clearUser만 false, `SessionContext.Reset(clearUser)`가 촬영 데이터는 항상 폐기, it3 §2.2).
- 유휴 타임아웃(`AppShellViewModel.cs:192`)은 `clearUser: true` **유지**(무인 보호 — 손님 이탈 시 다음 손님 위해 로그아웃).
- 사용자 취소(`GoHome` 커맨드, `:197`)는 기본 `clearUser:false`(이미 로그인 보존).

### 4.2 PRD 정합

- PRD §F8/§10·결정 #16/#30의 "세션 종료 후 게스트 복귀" 원안을 이 결정으로 **갱신**(요구 문서가 명시). 설계 문서에 갱신 사실 기록. 유휴 로그아웃은 무인 보호로 유지되므로 키오스크 안전성은 보존.

### 4.3 검증 포인트 (headless)

- 단위(기존 `SessionServiceTests`/신규): `Reset(clearUser:false)` 후 `CurrentUser` 유지 + 촬영 데이터(SelectedFrame·Cuts) null. `Reset(clearUser:true)` 후 `CurrentUser` null.
- 사용자 확인(육안): 로그인→촬영 완료→홈 복귀 시 로그인 유지(상단 바 계정 라벨). 유휴 만료 시에는 로그아웃.

---

## 5. C1·C2 — 계정/설정 구조 개편

### 5.1 C1 — 설정 페이지에서 계정 기능 제거

- `SettingsView.xaml`: [계정] 섹션(비번 변경)·[관리자] 섹션(계정 생성·사용자 관리·앱 종료) **제거**. 설정 = [앱 설정](AppSettings) 카드만.
  - **앱 종료** 버튼은 관리자 섹션에 있었으므로, 계정 전용 관리자 페이지(C2)로 이전하거나 설정 하단에 별도 유지(결정: **관리자 페이지로 이전** — 계정=관리 기능 일원화). 
- `SettingsViewModel`: 계정 관련 커맨드·필드(`ChangePassword`·`CreateAccount`·`CreatableRoles`·`SelectedNewRole`·`OpenUserManagement`·`AccountMessage`·`AdminMessage`·`NewPassword`·`ConfirmPassword`·`NewAccountId`·`NewAccountPassword`)를 **계정 VM으로 이전**(§5.2). SettingsViewModel은 AppSettings 편집만 남김.

### 5.2 C2 — 계정 기능 전용 페이지

계정 팝오버(상단 바)의 각 항목이 **자기 전용 페이지만** 노출하도록 한다.

- **상태머신 설계**: 계정 기능이 여러 개(로그아웃·비번 변경·사용자 관리·계정 생성)이므로 옵션 2가지:
  - **(A) 단일 `Account` 상태 + 섹션 선택**: `AppState.Account` 신규 + `AccountViewModel`이 서브 모드(PasswordChange/UserMgmt/AccountCreate)를 파라미터로 받아 해당 UI만 표시. 팝오버 항목이 진입 모드를 지정.
  - **(B) 상태 분리**: `AccountPassword`·`AccountCreate` 등 상태를 각각 추가(+ 기존 `UserMgmt` 재사용).
  - **채택: (A) 단일 `AppState.Account` + 진입 파라미터**(상태 폭증 방지, 오버레이 네비 일관). `NavigateToOverlayAsync(Account)` 시 모드 전달(`AppShellViewModel`에 `AccountEntryMode` 필드 또는 `NavigateToAccountAsync(mode)`).
- **팝오버 항목 → 전용 페이지**:
  - **로그아웃**: 페이지 없이 즉시 `_session.Logout()`(현행 유지, `AppShellViewModel.Logout`).
  - **비밀번호 변경**: `Account`(mode=PasswordChange) → 신 비번 2회 확인 UI만. `AccountViewModel.ChangePassword`(기존 SettingsViewModel 로직 이전).
  - **(power) 사용자 관리**: `Account`(mode=UserMgmt) 또는 기존 `AppState.UserMgmt` 재사용(이미 있음). 계정 생성과 통합 페이지로 둘 수도.
  - **(power) 계정 생성**: `Account`(mode=AccountCreate) → 역할 규칙(manager→user, admin→user·manager) UI. `AccountViewModel.CreateAccount`(기존 로직 이전, actingRole 게이트 유지 it2 §7).
- **네비/복귀**: 계정 페이지는 오버레이(설정처럼) — 진입 전 상태 저장, [뒤로/닫기]로 복귀(`ReturnFromOverlay`). `CanTransition` 특례에 `Account` 추가 또는 오버레이 진입으로 처리.
- **팝오버 표시 조건**: 비번 변경=로그인, 사용자 관리·계정 생성=power(IsPower). 로그아웃=로그인. (기존 팝오버 Visibility 유지·확장.)

### 5.3 재사용/이전

| 기존 자산 | 처리 |
|---|---|
| `SettingsViewModel`의 계정/관리자 로직 | **`AccountViewModel`(신규)로 이전**. SettingsVM은 AppSettings만. |
| `SettingsView`의 [계정]·[관리자] 섹션 | 제거 → `AccountView`(신규, mode별 UI)로 이전. |
| `UserMgmtViewModel`/`View` | **재사용**(사용자 관리 mode 또는 기존 UserMgmt 상태). |
| 팝오버(`MainWindow`) | 항목별 진입 대상을 설정→계정 페이지로 변경. |
| `IAccountService`(ChangePassword/CreateAsync actingRole 게이트) | 그대로 사용. |

---

## 6. U7 — 설정 레이아웃 재수정 (라벨 잘림 + PC화)

### 6.1 근본 원인

`RowLabel Width=240` 고정 + 카드 `MaxWidth=720` + 컨트롤 우측 정렬이라, 긴 라벨("컷당 카운트다운(초)", "카메라 장치 인덱스", "표시 모드")이 240px에 안 들어가 잘린다(VF-10, 스크린샷). it4 §5의 PC 밀도(라벨폭 200, MaxWidth 1040)가 아직 반영 안 됨.

### 6.2 수정 설계

- **라벨 잘림 해결**: 라벨을 고정폭 대신 **`Auto` 폭 + `TextWrapping=NoWrap`이되 충분한 컬럼**(2열 `Grid` `Auto,*` — 라벨 Auto가 내용만큼, 컨트롤 `*` 나머지). 라벨이 잘리지 않게 `TextTrimming=None`. 또는 라벨폭을 넉넉히(280+) + 카드 폭 확대.
- **PC 밀도(it4 §5 계승·완성)**: 카드 `MaxWidth` 720→**960~1040**, 항목을 **2열 그리드**(짧은 항목 좌/우 분산), 조밀 행 간격(`Space.S`~`Space.M`), 컨트롤 표준 크기(콤보/텍스트 높이 36, 토글 시각 폭 44). **최소 히트 영역 40 유지**(키오스크 터치).
- **정렬**: 라벨-컨트롤 baseline/center 정렬 통일. 그룹 소제목·구분선 유지(it3 그룹). 좁은 폭(세로 창)에서 1열 폴백.
- **라이트 토큰만**: 색·그림자·라운드 it2 토큰 그대로, 밀도·폭만 조정.

### 6.3 검증 포인트 (headless)

- 빌드 통과 + `grep`: 하드코딩 색 0, `RowLabel` 고정폭 240 제거(Auto/넉넉폭), 2열 그리드, 전 설정 항목 바인딩 유지(C1로 계정 섹션 제거된 것 외 앱설정 전 항목).
- 사용자 확인(육안): 라벨 안 잘림, PC 밀도·정렬 자연스러움, 터치 가능.

---

## 7. U8 — 로그인 페이지 아이디 자동 포커스

### 7.1 설계

- **진입 시 아이디 TextBox 포커스**: MVVM 유지하며 View 책임으로 처리. `LoginGuestView`의 아이디 `TextBox`에 `x:Name` 부여 후 **`Loaded` 이벤트(또는 `IsVisibleChanged`)에서 `Focus()` + `Keyboard.Focus`**. 오버레이 진입이라 `Loaded`가 매 진입 발생하는지 확인(UserControl 재생성/DataTemplate 스왑 시 Loaded 발생) — 발생하면 `Loaded`로 충분.
  - 대안: `FocusManager.FocusedElement`를 XAML에 설정(`FocusManager.FocusedElement="{Binding ElementName=IdTextBox}"`) — 선언적, code-behind 최소.
  - **채택**: XAML `FocusManager.FocusedElement`(선언적) 우선, 오버레이 재진입에 안 잡히면 `Loaded`에서 `Dispatcher.BeginInvoke(() => IdTextBox.Focus())` 보강.
- code-behind는 포커스 지정만(로직 없음) — MVVM 순수성 유지.

### 7.2 검증 포인트

- 빌드 통과 + `grep`: `FocusManager.FocusedElement` 또는 `Loaded`+`Focus`.
- 사용자 확인(육안): 로그인 페이지 진입 즉시 아이디 입력창에 커서.

---

## 8. F1 — 프레임 편집기 슬롯 크기 슬라이더 (70~130% 일괄 스케일)

### 8.1 설계

- **일괄 스케일**: 배치된 **모든 슬롯을 동일 배율로** 크기 조정. 기본 100%, 범위 70~130%. 각 슬롯의 **중심 유지**(스케일 후 중심이 원래 중심과 같게 위치 재계산), 종횡비 유지(it4 B4), 경계 클램프(it4 EditorTransform/`ClampToFrame`).
- **순수 함수**: `SlotLayout.ScaleSlots(IReadOnlyList<Slot> slots, double factor, int frameW, int frameH)` → 각 슬롯 `newW=round(w*factor)`, `newH=round(h*factor)`, 중심 유지(`newX=cx-newW/2`), `ClampToFrame`. 반환은 새 리스트. **UI 비의존 → 단위 테스트**.
  - 기준 크기: 스케일은 **100% 기준(자동 배치 원본 크기)에 대한 배율**이어야 누적 오차가 없다. VM이 자동 배치 시 원본 슬롯을 보관(`_baseSlots`)하고, 슬라이더 값(factor)으로 매번 `_baseSlots`에서 스케일 → `Slots`에 반영. (현재 슬롯을 반복 스케일하면 부동 누적.)
- **VM**: `FrameEditorViewModel`에 `[ObservableProperty] double _slotScalePercent = 100`(70~130) + `SlotScaleFactor => _slotScalePercent/100`. `OnSlotScalePercentChanged` → `_baseSlots`에서 `ScaleSlots(factor)` → `Slots` 갱신 → `UpdateCanSave`. 자동 배치(`ArrangeSlots`)·종횡비 변경 시 `_baseSlots` 재설정 + 현재 factor 재적용.
- **UI**: 편집기 컨트롤 패널에 `Slider`(Min 70, Max 130, 기본 100) + **% 표시 TextBlock**(`{SlotScalePercent}%`). 라이트 토큰. Slider 스타일 필요 시 Controls.xaml에 추가(선택).
- **드래그와 공존**: 개별 드래그(위치)는 it4 절대 위치 방식 유지. 스케일은 크기만 일괄 — 드래그 후 스케일하면 현재 위치 중심 유지. 드래그가 `_baseSlots`를 갱신해야 스케일 기준이 맞음(드래그 종료 시 `_baseSlots[i]` 위치 갱신).
- **정합**: 스케일 후 겹침 가능 → `IsValid` 게이트로 저장 차단·안내(기존). 경계 클램프는 `ClampToFrame`.

### 8.2 검증 포인트 (headless)

- 단위(`SlotLayoutTests` 확장): `ScaleSlots(slots, 1.3, W, H)` → 각 슬롯 크기 1.3배(±1px)·중심 유지·경계 내. `0.7` → 0.7배. 경계 초과 시 클램프. 종횡비 유지(scale은 w·h 동일 배율).
- 사용자 확인(육안): 슬라이더로 70~130% 조절 시 전 슬롯 동일 크기 변경·% 표시·비율 유지·경계 안 넘음.

---

## 9. 파일 변경 요약

| 파일 | 변경 | 요구 |
|---|---|---|
| `src/MCPhoto.App/ViewModels/QrPopupViewModel.cs` | 업로드 실패 우아 처리(`UploadFailed` 상태 + [완료]·[재시도], 비위협 안내, 흐름 비차단) | B6 |
| `src/MCPhoto.App/Views/QrPopupView.xaml` | 업로드중/성공(QR)/실패(안내+완료+재시도) 3상태 UI | B6 |
| `src/MCPhoto.App/ViewModels/SettingsViewModel.cs`·`SettingsView.xaml` | StorageBucket 힌트 텍스트(예 `*.firebasestorage.app`), 버킷 경로 문서화 | B6 |
| `src/MCPhoto.App/Views/CutSelectView.xaml` | 썸네일 컨테이너 슬롯 종횡비 + `Stretch=Uniform` | B7 |
| `src/MCPhoto.App/ViewModels/CutSelectViewModel.cs` | `SlotAspectRatio` 노출 | B7 |
| `src/MCPhoto.App/ViewModels/DoneViewModel.cs` | `clearUser: true`→`false`(2곳) | B8 |
| `src/MCPhoto.App/Views/SettingsView.xaml`·`SettingsViewModel.cs` | 계정·관리자 섹션 제거(앱설정만) | C1 |
| `src/MCPhoto.App/ViewModels/AccountViewModel.cs`·`Views/AccountView.xaml`(신규) | 계정 전용 페이지(mode별: 비번변경·계정생성) | C2 |
| `src/MCPhoto.Core/Navigation/AppState.cs`·`SessionStateMachine.cs` | `AppState.Account` 신규 + 오버레이 특례 | C2 |
| `src/MCPhoto.App/AppShellViewModel.cs`·`MainWindow.xaml` | 팝오버 항목→계정 페이지 네비(모드 전달), 앱종료 이전 | C1·C2 |
| `src/MCPhoto.App/Views/SettingsView.xaml` | 라벨 잘림 해결·2열 PC 밀도(라벨 Auto·MaxWidth↑) | U7 |
| `src/MCPhoto.App/Views/LoginGuestView.xaml`(+`.cs`) | 아이디 자동 포커스(FocusManager/Loaded) | U8 |
| `src/MCPhoto.Core/Frames/SlotLayout.cs` | `ScaleSlots`(일괄 스케일·중심 유지·클램프) | F1 |
| `src/MCPhoto.App/ViewModels/FrameEditorViewModel.cs` | `SlotScalePercent`·`_baseSlots`·스케일 적용 | F1 |
| `src/MCPhoto.App/Views/FrameEditorView.xaml` | 크기 슬라이더 + % 표시 | F1 |
| `tests/MCPhoto.Tests/` | `QrPopupViewModel` 실패 처리(목 upload)·`SlotLayoutTests`(ScaleSlots)·`SettingsTests`(StorageBucket 라운드트립)·`AppStateTests`(Account) | B6·B8·C2·F1 |

---

## 10. 리스크 & 완화

| # | 리스크 | 영향 | 완화 | 검증 |
|---|---|---|---|---|
| R1 | 실제 QR 전송은 Blaze+버킷(외부 전제) 전엔 불가 | 이번 이터로 QR 성공은 안 됨 | 이번 목표는 **실패 우아 처리**(로컬 보존·완료 진행·비위협 안내) — QR 성공은 외부 전제 충족 후. saveLocalCopy off+QR실패 시 손실 → 안내로 완화 | Step 1 |
| R2 | 썸네일 종횡비 바인딩이 여러 슬롯 상이 비율에서 애매 | 표시 불일치 | 대표 슬롯 비율(균일 전제, it4 전역 비율) 사용, 상이 비율은 확장 가정 | Step 2 |
| R3 | Done clearUser:false가 "다음 손님에 이전 로그인 잔존" | 계정 오용 | 유휴 타임아웃 clearUser:true 유지(무인 이탈 보호), 명시 로그아웃 제공 | Step 3 |
| R4 | 계정 페이지 신규 상태가 상태머신/네비 복잡화 | 전이 버그 | 단일 Account 상태+모드 파라미터(상태 폭증 방지), 오버레이 특례, AppStateTests | Step 4 |
| R5 | U7 2열이 세로 창(키오스크)에서 붕괴 | 레이아웃 깨짐 | 좁은 폭 1열 폴백(반응형) | Step 5 육안 |
| R6 | F1 스케일 누적 부동오차·겹침 | 배치 오류 | `_baseSlots` 기준 스케일(누적 방지), IsValid 게이트, 클램프 | Step 7 |
| R7 | 슬롯 스케일 후 겹침으로 저장 불가 빈발 | UX 저하 | 스케일 상한 130%+겹침 시 안내, 자동 배치가 간격 확보 | Step 7 육안 |

---

## 11. 사용자 확인 필요 목록 (UI 육안 — headless 불가)

> WBS 완료 기준은 전부 headless(build/test/grep). 아래는 구현 후 사용자 육안 확인(각 Step trigger/non-goal로 분리).

1. **B6**: QR on + 버킷 없음(현 상태) → 위협적 팝업 대신 "전송 실패 — 기기에 저장됨"(saveLocalCopy on) 안내 + [완료]로 정상 진행 + [재시도] 제공. 결과물이 로컬에 보존됨. (Blaze 전환 + 버킷 생성 + StorageBucket 지정 후엔 QR 성공 — 외부 전제.)
2. **B7**: 컷 선택 썸네일이 슬롯과 동일 종횡비·모양(늘임/잘림 없음), 라이브 프리뷰·합성과 일치. 4:3/3:4/1:1 선택 반영.
3. **B8**: 로그인→촬영 완료→홈 복귀 시 로그인 유지. 유휴 만료 시에는 로그아웃.
4. **C1/C2**: 설정 페이지에 계정/관리자 섹션 없음(앱설정만). 계정 버튼 메뉴에서 비번 변경/사용자 관리/계정 생성 선택 시 각 전용 페이지만 노출. 역할 규칙(manager→user, admin→manager) 동작.
5. **U7**: 설정 라벨 안 잘림, PC 밀도·정렬 자연스러움, 키오스크 터치 가능.
6. **U8**: 로그인 페이지 진입 즉시 아이디 입력창 포커스.
7. **F1**: 편집기 슬롯 크기 슬라이더 70~130%·% 표시, 전 슬롯 동일 크기 일괄 조정·비율 유지·경계 안 넘음.

## 부록. 참고

- it2 토큰·라이트 팔레트: `docs/design/wpf-it2-design.md` §2
- it3 세션 단일소스·Save 신뢰성·유휴 Reset(clearUser): `docs/design/wpf-it3-design.md` §2·§3
- it4 편집기 좌표(EditorTransform)·종횡비(SlotAspect)·유휴 제외: `docs/design/wpf-it4-design.md` §2·§3·§4
- 크롭 파이프라인: `CropCalculator.cs`, `CaptureViewModel.cs:50`, PRD §F1/§F36
