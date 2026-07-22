# MC포토 — 이터레이션 7 (프레임 슬롯 버그 + QR 전송 세분화)

| 항목 | 값 |
|------|-----|
| 작성일 | 2026-07-21 |
| 범위 | WPF 앱 + 웹 다운로드 페이지 |
| 상위 | it2~it6 위. Firebase(Firestore+Storage+Hosting) 실배포 완료 상태 |

> 사용자 QR 촬영·프레임 테스트 중 발견. (웹 다중상태 노출 버그는 별도 즉시 수정 완료 — styles.css `.state[hidden]` 추가.)

---

## P1. 버그

### B9. 프레임 슬롯 개수/배치가 저장·반영되지 않음
- **증상**: 편집기에서 슬롯 개수 6을 지정했는데 **실제 저장된 문서엔 슬롯 1개**만 들어감(Firestore 실측: name='새 프레임', slots 1개 x=38 y=41 w=331 h=442). 사용자는 "슬롯 지정을 할 수도 없다"고 함.
- **진단(오케스트레이터)**: 직렬화·Save 로직은 정상(Slots.ToList() 저장, DTO 매핑 정상). 문제는 **슬롯 개수(SlotCount)가 선택값(6)으로 반영되지 않고 1로 떨어지는 것**. `SlotCount` ComboBox가 `SelectedIndex="{Binding SlotCount, Converter=SlotCountIndex}"`(TwoWay)인데, it3 커스텀 ComboBox ControlTemplate 하에서 **로드/초기화 시 SelectedIndex가 0(=1개)으로 세팅되며 SlotCount=1로 되돌려 쓰이는(clobber)** 전형적 WPF 버그로 추정. (컨버터 자체는 정상.)
- **기대**:
  - 편집기에서 선택한 **슬롯 개수(1~6)가 그대로 배치·저장**되고, 재로딩 시 동일하게 나타난다.
  - 슬롯을 **드래그로 이동·배치**할 수 있고(사용자가 실제로 조작 가능해야 함), 위치·크기·종횡비·스케일이 저장에 반영된다.
- **개발 검증**: 콤보박스 선택→SlotCount 반영을 확실히(초기화 클로버 방지: SelectedItem/SelectedValue 바인딩 전환 또는 초기값 세팅 순서 수정, 커스텀 템플릿의 SelectionBox/Popup 검증). 슬롯 개수·배치 저장을 단위/통합 테스트 또는 STA 렌더 테스트로 고정. 실제 슬롯 드래그 상호작용 육안은 사용자 확인.

---

## P2. 기능 — QR 전송 옵션 세분화

### F2. 사진/타임랩스 개별 토글 (WPF 설정 + 업로드)
- **설정 구조**:
  - `EnableQrDelivery`(기존, on/off) 하위에 **사진 전송(SendPhoto)**·**타임랩스 전송(SendTimelapse)** 두 토글 추가.
  - **QR 전송 ON → 기본값 둘 다 ON.** 하나만 끌 수 있음.
  - **둘 다 OFF → QR 전송 자동 OFF**(EnableQrDelivery=false 연동).
  - QR 전송 OFF면 하위 토글은 숨김/비활성(꺼진 상태).
  - 설정 UI: QR 전송 토글 아래 사진/타임랩스 하위 토글(QR on일 때만 노출), INI 영속.
- **업로드 로직**: 켜진 미디어만 업로드. **꺼진 미디어의 URL은 ResultSession에서 null.**
  - 사진만 on → finalImageUrl 업로드, timelapseUrl=null.
  - 타임랩스만 on → 반대.
- **계약 변경(firebase-contract)**: ResultSession에 "어떤 미디어가 의도적으로 제외됐는지"를 웹이 알 수 있게 한다. 방식: 웹이 **doc 존재 + 미만료인데 URL이 null이면 '전송 옵션 꺼짐'으로 해석**(추가 플래그 없이 추론) — 또는 명시 플래그(photoSent/timelapseSent) 추가. 설계에서 택일(추론 방식이 계약 변경 최소).

### F3. 웹 다운로드 페이지 — 미디어 부재 시 안내 문구
- 사진 URL 없음(옵션 꺼짐) → "**사진은 전송 옵션이 꺼져 있어 제공되지 않습니다**" 류 안내.
- 타임랩스 URL 없음(옵션 꺼짐) → "**타임랩스는 전송 옵션이 꺼져 있어 제공되지 않습니다**" 류 안내.
- 만료(expiresAt 지남/문서 부재)·로드 실패와는 **구분**해서 표시(만료는 기존 만료 화면 유지). 즉 성공 상태에서 개별 미디어가 '옵션 꺼짐'이면 그 영역에 안내만.

---

## 비범위
- 관리자 키 제거→인증(Auth) 전환은 **별도 이터레이션**(다지점 보안, 사용자 승인됨). 이번 범위 아님.
- Blaze/Storage는 이미 활성.
