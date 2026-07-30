# 14 · 미디어 파이프라인 규격 (플랫폼 중립)

| 항목 | 내용 |
|------|------|
| 문서 | 카메라 획득 → 가공 → 스틸/녹화 분기 → 합성 → 필터 → 타임랩스까지의 **알고리즘 규격**. 어떤 플랫폼에서 구현해도 **동일한 픽셀 결과**가 나와야 한다 |
| 범위 | 좌표계 정의, 중앙 크롭·슬롯 배치·합성 순서, 필터 파라미터, 슬롯 자동배치·스케일·좌표 변환, 녹화·타임랩스 인코딩 파라미터, 스레딩·리소스 규약 |
| 최종 업데이트 | 2026-07-30 (신규 — OpenCV/ffmpeg 구현을 플랫폼 중립 알고리즘 규격으로 추출) |
| 관련 문서 | 진입 [05](./05-cross-platform-client-guide.md) · 화면 동작 [13](./13-client-behavior-spec.md) · 프레임 파일 포맷 [41 §3](./41-local-data-and-file-formats.md) · Windows 구현 [10 §4](./10-exe-app-architecture.md) |
| 갱신 규칙 | 크롭·합성 순서, 필터 파라미터, 인코딩 파라미터, 슬롯 기하 알고리즘이 바뀌면 이 문서를 갱신한다. 변경은 **기존 프레임의 결과물 호환성에 영향**을 주므로 반드시 이유를 남긴다 |

---

## 1. 좌표계 정의 (모든 계산의 기준)

세 좌표계를 혼동하면 WYSIWYG가 깨진다.

| 좌표계 | 기호 | 정의 | 쓰이는 곳 |
|--------|------|------|-----------|
| **프레임 좌표** | F | 프레임 이미지의 **원본 픽셀** 좌표계. 원점 좌상단 | 슬롯 `x/y/width/height`, 저장, 클램프, 합성 목적지, 캡처 목표 종횡비 |
| **화면(캔버스) 좌표** | C | 편집기·미리보기에서 프레임을 표시하는 뷰 좌표계 | 슬롯 사각형 그리기, 드래그 입력 |
| **카메라 좌표** | S | 카메라가 내보내는 원본 프레임 픽셀 좌표계 | 거울 반전, 중앙 크롭 ROI |

**진실의 좌표계는 F다.** 저장되는 모든 슬롯 값은 F이며, C↔F 변환은 §4.5의 단일 변환만 사용한다.

### 1.1 종횡비 정의

이 문서의 모든 `aspect`는 **가로/세로(width/height)** 다.

| 이름 | 값 |
|------|-----|
| 4:3 | `4/3 ≈ 1.3333` |
| **3:4** (기본) | `3/4 = 0.75` |
| 1:1 | `1.0` |

---

## 2. 카메라 획득 계약

### 2.1 요구 사항

| 항목 | 규격 |
|------|------|
| 요청 해상도 | 1080p 목표(획득 가능한 최대 근사치 허용) |
| 요청 프레임레이트 | **30fps** |
| 픽셀 포맷 | 3채널 8bit(BGR 또는 RGB). **채널 순서를 파이프라인 전체에서 일관되게 유지**할 것 |
| 프레임 획득 | 전용 백그라운드 스레드/큐. **UI 스레드에서 획득하지 않는다** |
| 프레임 표현 | 픽셀 버퍼 + `width` + `height` + `stride`(행 바이트 수) |
| 장치 열기 실패 | **예외가 아니라 실패 반환**(false/null). 상위가 `Failed` 상태로 안내 |
| 시작 멱등성 | 이미 실행 중이면 무시하고 성공 반환. 다른 장치로 바꿀 때는 **정지 후 재시작** |
| 단일 소유 | 카메라 핸들·스레드는 앱 전체에서 **하나만** 소유한다(실촬영·라이브 프리뷰·테스트 모달이 같은 인스턴스를 공유) |

### 2.2 단일 스트림 → 3분기 (핵심 설계)

```
카메라 원본 프레임 1장
      │
      ├─ ① 거울 반전(설정 on일 때)      ← 프레임당 1회만
      └─ ② 중앙 크롭(목표 종횡비)        ← 프레임당 1회만
              │
      ┌───────┼───────────────┬─────────────────┐
      ▼       ▼               ▼                 ▼
   프리뷰   녹화(인코더)     스틸 캡처         (fps 계산)
```

**규격**
- 가공(①②)은 **프레임당 1회**만 수행하고 결과를 3소비자가 공유한다. 소비자별로 따로 가공하면 성능 손실 + 불일치가 생긴다.
- **프리뷰 = 스틸 = 녹화가 동일 픽셀 가공을 거친다**(WYSIWYG). 손님이 화면에서 본 구도가 그대로 저장돼야 한다.
- **스틸 캡처**: 요청이 대기 중이면 다음 프레임에서 버퍼를 **복제**해 완성한다(단일 프레임 원자적 완료). 원본 버퍼를 그대로 넘기면 다음 프레임이 덮어쓴다.
- **녹화 백프레셔**: 인코더가 밀리면 **프레임을 드롭**한다(프리뷰 우선). 녹화 때문에 프리뷰가 끊기면 안 된다.
- **프리뷰 렌더 프레임 스킵**: 이전 렌더가 끝나지 않았으면 새 프레임으로 **덮어쓰고 최신 것만** 그린다. 큐를 쌓지 않는다.
- 프리뷰 렌더는 **버퍼 재사용**이 원칙이다(매 프레임 새 이미지 객체를 만들면 GC 압력이 커진다). 크기가 변할 때만 새 버퍼를 만든다.

### 2.3 Ready 게이트 (안정적 프리뷰 판정)

첫 프레임 1장만으로 촬영을 시작하면 안 된다. 아래 3조건을 **모두** 만족해야 Ready다.

```
ready = (누적 프레임 수 >= 8) AND (대기 시작 후 경과 >= 500ms) AND (현재 fps > 0)
```

- 전이 시 **1회만** Ready 신호를 낸다(중복 방지).
- 상위는 **8000ms 타임아웃**을 걸고, 초과 시 `Failed`로 전환한다(무한 로딩 금지).
- 촬영 시퀀스(카운트다운)는 **Ready 이후에만** 시작한다.
- 카메라 테스트 모달도 **동일 규칙**을 쓴다.

### 2.4 거울 반전

- 좌우 반전(수평 플립). 기본 **on**.
- 런타임 토글이 가능해야 한다(설정 저장 시 즉시 반영).
- **프리뷰만 CSS/뷰 변환으로 반전하고 저장 픽셀을 반전하지 않는 구현은 규격 위반**이다(§2.2). 프리뷰를 뷰 변환으로 처리하려면 스틸·녹화 픽셀에도 같은 반전을 적용해야 한다.

---

## 3. 중앙 크롭 (카메라 → 목표 종횡비)

카메라 원본을 **대표 슬롯의 종횡비**에 맞춰 왜곡 없이 잘라낸다.

```
centerCrop(srcW, srcH, targetAspect) -> (x, y, w, h):
  if srcW <= 0 or srcH <= 0:  return (0, 0, max(0,srcW), max(0,srcH))
  if targetAspect <= 0:       return (0, 0, srcW, srcH)          # 크롭 없음

  srcAspect = srcW / srcH

  if srcAspect > targetAspect:          # 원본이 더 넓음 → 좌우를 잘라낸다
      cropH = srcH
      cropW = round(srcH * targetAspect)
  else:                                 # 원본이 더 좁음/길음 → 상하를 잘라낸다
      cropW = srcW
      cropH = round(srcW / targetAspect)

  cropW = clamp(cropW, 1, srcW)          # 반올림 오차로 원본 초과 방지
  cropH = clamp(cropH, 1, srcH)

  x = (srcW - cropW) / 2                 # 정수 나눗셈(내림)
  y = (srcH - cropH) / 2
  return (x, y, cropW, cropH)
```

- `targetAspect`는 런타임에 변경 가능해야 한다(프레임 선택 시 대표 슬롯 비율로 설정).
- **대표 슬롯**은 프레임의 슬롯 중 기준이 되는 하나다(현행: 첫 슬롯 기준. 슬롯들이 서로 다른 비율이면 §5의 슬롯별 보정이 차이를 흡수한다).

---

## 4. 슬롯 기하 알고리즘 (P3 저작)

### 4.1 자동 배치

프레임 크기와 슬롯 개수로 초기 배치를 계산한다.

```
autoArrange(slotCount, frameW, frameH, targetAspect?) -> Slot[]:
  slotCount   = clamp(slotCount, 1, 6)
  frameAspect = frameW / frameH

  # 세로로 긴 스트립(1:3, 1:4 등)은 1열로 세운다
  verticalStrip = (frameAspect < 0.6)

  if verticalStrip:
      cols = 1;  rows = slotCount
  else:
      cols = { 1:1, 2:2, 3:3, 4:2, 5:3, 6:3 }[slotCount]   # 그 외 → 2
      rows = ceil(slotCount / cols)

  marginX = max(20, frameW / 20)      # 정수 나눗셈
  marginY = max(20, frameH / 20)
  gapX    = max(12, frameW / 40)
  gapY    = max(12, frameH / 40)

  cellW = (frameW - marginX*2 - gapX*(cols-1)) / cols     # 정수 나눗셈
  cellH = (frameH - marginY*2 - gapY*(rows-1)) / rows

  for i in 0 .. slotCount-1:
      r = i / cols   (정수 나눗셈);  c = i % cols
      cellX = marginX + c * (cellW + gapX)
      cellY = marginY + r * (cellH + gapY)
      (w, h, offX, offY) = fitInCell(cellW, cellH, targetAspect)
      emit Slot{ index: i, x: cellX + offX, y: cellY + offY, width: w, height: h }
```

```
fitInCell(cellW, cellH, targetAspect) -> (w, h, offX, offY):
  if targetAspect is null or targetAspect <= 0:
      return (cellW, cellH, 0, 0)                      # 셀 크기 그대로

  cellAspect = cellW / cellH
  if cellAspect > targetAspect:      # 셀이 목표보다 넓음 → 높이를 셀에 맞춘다
      h = cellH;  w = round(h * targetAspect)
  else:                              # 셀이 목표보다 김 → 폭을 셀에 맞춘다
      w = cellW;  h = round(w / targetAspect)

  w = clamp(w, 1, cellW);  h = clamp(h, 1, cellH)
  offX = (cellW - w) / 2;  offY = (cellH - h) / 2       # 정수 나눗셈 → 셀 중앙
  return (w, h, offX, offY)
```

> ⚠️ **정수 나눗셈 주의**: 위 `/`는 모두 **정수 나눗셈(내림)** 이다. 부동소수 나눗셈으로 구현하면 Windows 클라이언트와 픽셀 단위로 결과가 달라진다.

### 4.2 일괄 스케일 (70~130%)

```
scaleSlots(baseSlots, factor, frameW, frameH) -> Slot[]:
  for s in baseSlots:
      newW = max(1, round(s.width  * factor))
      newH = max(1, round(s.height * factor))
      cx = s.x + s.width  / 2.0        # 부동소수 중심
      cy = s.y + s.height / 2.0
      newX = round(cx - newW / 2.0)
      newY = round(cy - newH / 2.0)
      emit clampToFrame(Slot{s.index, newX, newY, newW, newH}, frameW, frameH)
```

- **반드시 `baseSlots`(사용자가 스케일을 건드리기 전의 원본)에서 계산한다.** 현재 슬롯에서 반복 스케일하면 누적 반올림 오차가 커진다.
- 중심 유지 + 종횡비 유지(w·h 동일 배율).

### 4.3 경계 클램프

```
clampToFrame(slot, frameW, frameH) -> Slot:
  w = clamp(slot.width,  1, frameW)
  h = clamp(slot.height, 1, frameH)
  x = clamp(slot.x, 0, frameW - w)
  y = clamp(slot.y, 0, frameH - h)
  return Slot{slot.index, x, y, w, h}
```

### 4.4 겹침·유효성 검사

```
overlaps(a, b):      # 경계 접촉은 겹침이 아니다
  a.x < b.x + b.width  AND  a.x + a.width  > b.x AND
  a.y < b.y + b.height AND  a.y + a.height > b.y

isValid(slots, frameW, frameH):
  1 <= count(slots) <= 6
  AND 모든 s: s.x >= 0 AND s.y >= 0
           AND s.x + s.width  <= frameW
           AND s.y + s.height <= frameH
           AND s.width >= 1 AND s.height >= 1
  AND 어떤 두 슬롯도 겹치지 않음
```

저장은 `isValid`가 true일 때만 허용한다. 서버도 `slots` 개수·부호를 재검증한다([31 §8](./31-backend-api-reference.md)).

### 4.5 화면↔프레임 좌표 변환 (WYSIWYG의 핵심)

표시·드래그·클램프가 **모두 같은 변환**을 써야 한다. Uniform 스케일 + 중앙 레터박스.

```
compute(canvasW, canvasH, frameW, frameH):
  if any <= 0: return invalid (scale = 0)
  scale   = min(canvasW / frameW, canvasH / frameH)      # 부동소수
  dispW   = frameW * scale
  dispH   = frameH * scale
  originX = (canvasW - dispW) / 2
  originY = (canvasH - dispH) / 2

frameToCanvas(fx, fy) = (originX + fx*scale, originY + fy*scale)
canvasToFrame(cx, cy) = scale <= 0 ? (0,0) : ((cx - originX)/scale, (cy - originY)/scale)
```

- 드래그는 **그랩 오프셋 기반 절대 위치 이동**으로 구현한다(포인터 이동 델타 누적이 아니라, 잡은 지점을 기준으로 목표 위치를 계산). 델타 누적은 오차가 쌓인다.
- 캔버스 크기는 **실제 렌더된 크기**를 쓴다(선언된 크기가 아니라).

### 4.6 프레임 이미지 검증·정규화

| 항목 | 규격 |
|------|------|
| 허용 확장자 | `.png` / `.jpg` / `.jpeg` |
| 최대 용량 | **10MB**(10 × 1024 × 1024 바이트) |
| 최대 장변 | **4000px**. 초과 시 축소 배율 = `4000 / max(width, height)`, 새 크기 = `round(w*f)`, `round(h*f)` |
| 정규화 | 검증·축소 후 **PNG로 재인코딩**해 저장한다(저장 포맷은 항상 PNG) |

### 4.7 fallback 프레임 스펙 (프레임이 하나도 없을 때)

| 항목 | 값 |
|------|-----|
| 크기 | **1200 × 1600** (3:4) |
| 배경 | 하양 |
| 슬롯 | **4개, 2×2 격자** |
| 슬롯 종횡비 | 3:4 유지 |
| 여백/간격 | margin 80, gap 60 |
| 셀 크기 | `cellW = (1200 - 80*2 - 60) / 2`, `cellH = cellW * 4/3` |
| 배치 | 수평은 margin 기준, 수직은 **중앙 정렬**(`top = (1600 - (cellH*2 + gap)) / 2`) |
| id | `"fallback"` |
| 이름 | `"기본 프레임"` |

---

## 5. 합성 (결과물 생성)

### 5.1 절차 (순서가 규격)

```
compose(frame, cuts[], filter, outputPath):
  1. assert count(cuts) == count(frame.slots)      # 다르면 오류 (M12)
  2. background = frame 이미지 로드                 # 출력 해상도 = 프레임 원본 해상도
     frameW, frameH = background 크기
  3. orderedSlots = frame.slots 를 index 오름차순 정렬
  4. for i in 0 .. orderedSlots.count-1:
       slot     = orderedSlots[i]
       cut      = cuts[i]                          # 선택 순서 = 슬롯 순서
       slotRect = clampSlotToFrame(slot, frameW, frameH)
       filtered = applyFilter(cut, filter)          # 컷 전체 일괄
       srcCrop  = centerCrop(filtered.w, filtered.h, slotRect.w / slotRect.h)   # cover
       scaled   = resize(filtered[srcCrop], slotRect.w × slotRect.h, AREA 보간)
       background[slotRect] = scaled                # 덮어쓰기(알파 블렌딩 아님)
  5. 출력 디렉터리 생성 후 background 를 outputPath 에 기록
     (포맷은 outputPath 확장자로 결정: .jpg / .png)
```

### 5.2 목적지 클램프

```
clampSlotToFrame(slot, frameW, frameH) -> (x, y, w, h):
  x = clamp(slot.x, 0, max(0, frameW - 1))
  y = clamp(slot.y, 0, max(0, frameH - 1))
  w = clamp(slot.width,  1, frameW - x)
  h = clamp(slot.height, 1, frameH - y)
```

> §4.3의 편집기용 클램프와 **식이 다르다**(편집기는 크기를 유지하려 위치를 당기고, 합성은 위치를 유지하려 크기를 줄인다). 합성은 이상 데이터 방어가 목적이므로 이 형태를 유지한다.

### 5.3 주의점

- **프레임 이미지는 배경 레이어**다. 컷을 슬롯 영역에 **덮어쓴다**(프레임이 위로 올라오는 오버레이가 아니다). 따라서 프레임 PNG의 슬롯 영역은 비어 있어야 하고, 프레임 장식이 슬롯 위로 겹치는 디자인은 현재 파이프라인으로 표현되지 않는다.
- 캡처가 이미 슬롯 종횡비로 크롭돼 있으면 §5.1의 `srcCrop`은 사실상 전체가 되어 추가 손실이 없다. 슬롯 비율이 서로 다른 프레임에서만 슬롯별 보정이 실제로 작동한다.
- 리사이즈 보간은 **축소에 강한 방식**(area/box 평균)을 쓴다. bilinear/nearest는 축소 시 계단·모아레가 생긴다.
- 프레임 이미지를 찾을 수 없으면 **명확한 오류**로 실패한다(빈 배경으로 조용히 진행하지 않는다).

---

## 6. 필터 규격

입력·출력 모두 3채널 8bit. **원본을 변형하지 않고 새 버퍼를 반환**한다. 컷 **전체 일괄** 적용이며 얼굴 인식·부분 영역 적용은 없다.

| 필터 | 알고리즘 | 파라미터 |
|------|----------|----------|
| **원본**(None) | 복사만 | — |
| **흑백**(Grayscale) | 3채널 → 그레이스케일 → **다시 3채널로 복원**(합성 채널 일관성) | 표준 휘도 변환(BT.601 계열: `0.299R + 0.587G + 0.114B`) |
| **밝게**(Brightness) | `dst = saturate(src * alpha + beta)` | **alpha = 1.1, beta = 20** |
| **뷰티**(Beauty) | ① 엣지 보존 스무딩(bilateral) → ② 원본과 블렌드 → ③ 약한 톤 보정 | ① `d=7, sigmaColor=40, sigmaSpace=7` ② `smooth*0.6 + src*0.4` ③ `alpha=1.03, beta=6` |

- 결과값은 **채널당 0~255로 포화(saturate)** 처리한다(랩어라운드 금지).
- 플랫폼 대체: bilateral이 없으면 **가이디드 필터 / 엣지 보존 블러**로 근사한다. 정확한 동일 픽셀이 불가하면 **파라미터 의도**(과하지 않은 소프트닝 60% 블렌드 + 미세 밝기 상승)를 유지한다.
- 설정 토글은 **노출 여부**만 제어한다. 실제 적용은 결과 화면에서 사용자가 고른다. "원본"은 항상 목록에 있고 끌 수 없다.

---

## 7. 세션 녹화 · 타임랩스

### 7.1 세션 녹화

| 항목 | 규격 |
|------|------|
| 시작 시점 | 촬영 시퀀스 시작 직전(Ready 이후) |
| 종료 시점 | 마지막 컷 촬영 후 |
| 입력 | 가공된 프레임 스트림(§2.2) — **프리뷰와 동일 픽셀** |
| 프레임레이트 | **30fps** |
| 코덱 | **H.264** |
| 픽셀 포맷 | **yuv420p** (호환성) |
| 품질 | CRF **20** 상당(플랫폼 인코더는 동등 비트레이트/품질로 근사) |
| 오디오 | **없음** |
| 컨테이너 | mp4. 종료 시 **moov atom이 완성되도록 정상 종료**를 보장해야 한다 |
| 백프레셔 | 프레임 드롭 허용(§2.2) |
| 정지 실패 | 타임아웃 후 강제 종료. **예외를 상위로 전파하지 않는다** |
| 인코더 부재 | 녹화를 건너뛰고 경고 로그. 촬영 자체는 계속(타임랩스만 없어진다) |

> Windows 구현의 등가 인자: `-f rawvideo -pixel_format bgr24 -video_size {W}x{H} -framerate 30 -i - -c:v libx264 -crf 20 -preset veryfast -pix_fmt yuv420p out.mp4`

### 7.2 타임랩스 배속 산출

```
computeSpeedFactor(sessionSeconds) -> N:
  TARGET_MIN = 10.0
  TARGET_MAX = 15.0
  if sessionSeconds <= TARGET_MAX:   return 1.0        # 이미 짧으면 그대로
  targetMid = (TARGET_MIN + TARGET_MAX) / 2 = 12.5
  return max(1.0, sessionSeconds / targetMid)

expectedOutputSeconds = sessionSeconds / N
```

| 예시 세션 길이 | N | 결과 길이 |
|---------------|---|-----------|
| 12초 | 1.0 | 12초 |
| 15초 | 1.0 | 15초 |
| 30초 | 2.4 | 12.5초 |
| 60초 | 4.8 | 12.5초 |
| 120초 | 9.6 | 12.5초 |

- 세션 길이는 **실제 녹화 길이**를 쓴다(카메라 서비스가 측정한 값을 인코더 단계로 전달).

### 7.3 타임랩스 인코딩

| 항목 | 규격 |
|------|------|
| 입력 | 세션 녹화본 |
| 변환 | 프레젠테이션 타임스탬프를 `1/N` 배로 축소(= N배속) 후 **30fps로 재샘플** |
| 오디오 | **제거** |
| 코덱·품질·픽셀 포맷 | H.264 / CRF 20 상당 / yuv420p |
| 출력 | `timelapse.mp4` |
| 실패·인코더 부재 | **null 반환**(예외 아님). 상위는 타임랩스 없이 계속 진행 |

> Windows 구현의 등가 인자: `-i session.mp4 -vf "setpts={1/N}*PTS,fps=30" -an -c:v libx264 -crf 20 -pix_fmt yuv420p out.mp4`

**플랫폼 대체**

| 플랫폼 | 방법 |
|--------|------|
| macOS / iOS / iPadOS | `AVMutableComposition` + `scaleTimeRange`(구간 스케일) → `AVAssetExportSession` |
| Android | `MediaCodec` 재인코딩 시 타임스탬프 재계산, 또는 프레임 샘플링으로 근사 |
| 웹 | ~~배속 변환 수단이 사실상 없다 → 타임랩스 미제공으로 축소하거나 서버 변환 도입~~ **⚠️ 2026-07-30 대체**: 웹 확정 설계는 **촬영 중 OPFS 스풀(≤15fps) → 종료 시 실경과 선별 → WebCodecs H.264/mp4 직접 인코딩**이다(서버 변환 불요, [`docs/web-client/04 §7`](../web-client/04-media-pipeline-web.md)). 미지원 브라우저만 `timelapseUrl=null` 축소 |

**타임랩스를 제공하지 않을 때의 계약**: `timelapseUrl`을 null로 두고 commit하면 된다. 서버·웹 모두 null을 "전송 옵션 꺼짐"으로 정상 처리한다. 단 사진도 없으면 최소 1개 불변식 위반이므로 400이다([31 §5.3](./31-backend-api-reference.md)).

---

## 8. 출력 산출물 규약

| 산출물 | 파일명 | 포맷 | 비고 |
|--------|--------|------|------|
| 최종 합성 이미지 | `final.{jpg\|png}` | 설정 `outputFormat` | 해상도 = 프레임 원본 |
| 세션 녹화본 | `session.mp4` | H.264 무음 | **업로드하지 않는다**(타임랩스 원본). ⚠️ **웹 클라이언트는 이 산출물을 만들지 않는다**(스풀 방식 — [`docs/web-client/12 §C2`](../web-client/12-web-vs-windows-differences.md)) |
| 타임랩스 | `timelapse.mp4` | H.264 무음 | 업로드 대상 |

- 세션 작업 폴더 구조와 정리 규칙은 [41 §4](./41-local-data-and-file-formats.md).
- Storage 업로드 경로는 [31 §7](./31-backend-api-reference.md).

---

## 9. 스레딩·리소스 규약

| 대상 | 모델 | 해제 |
|------|------|------|
| 프레임 획득·가공·분기 | 전용 백그라운드 스레드/큐 **1개** | 정지 시 스레드 종료를 **타임아웃과 함께 대기** |
| 프리뷰 렌더 | UI 스레드. 렌더 대기 중 새 프레임은 **덮어쓰기**(스킵) | 뷰 해제 시 구독 해제 |
| 녹화 입력 | 획득 스레드에서 단일 라이터 → 락 불필요 | flush + 정상 종료, 실패 시 강제 종료 |
| 합성·타임랩스 | 워커(비동기). UI 블로킹 금지 | 중간 버퍼를 스코프 종료 시 해제 |
| 런타임 설정(거울·목표 종횡비·실행 여부) | 스레드 간 가시성 보장 필요 | — |
| 스틸/녹화 상태 전이 | 락 보호 | — |

**화면 이탈 시(`Capture` → 다른 화면)**
```
1. 촬영 시퀀스·카운트다운 취소
2. 녹화 정지 (예외 무시)
3. 카메라 정지
```
이 순서를 지켜야 파일이 손상되지 않고 장치가 확실히 풀린다.

**진행률 보고**: 업로드 진행률 콜백을 UI 스레드로 마샬링하는 경우, **콜백 도착 순서가 보장되지 않는다**는 점을 전제로 UI를 설계한다(단계 라벨을 순서 의존적으로 단언하지 않는다). Windows 구현에서 이 성질이 테스트 flakiness의 원인이 된 이력이 있다([90 §1](./90-roadmap-and-future-work.md)).

---

## 10. 플랫폼별 이식 체크리스트

- [ ] 카메라 프레임을 UI 스레드가 아닌 곳에서 획득한다
- [ ] 거울 반전과 중앙 크롭이 **프레임당 1회**만 수행되고, 프리뷰·스틸·녹화가 그 결과를 공유한다
- [ ] 스틸 캡처가 버퍼를 **복제**한다(다음 프레임에 덮어쓰이지 않는다)
- [ ] Ready 게이트 3조건 + 8초 타임아웃이 구현됐다
- [ ] `centerCrop`이 §3의 식과 **정수 나눗셈까지** 일치한다
- [ ] `autoArrange`·`fitInCell`의 정수 나눗셈이 §4.1과 일치한다
- [ ] 슬롯 스케일이 **원본 기준**으로 계산된다(누적 오차 없음)
- [ ] 편집기 표시·드래그·클램프가 **하나의 좌표 변환**을 공유한다
- [ ] 합성이 슬롯 `index` 오름차순으로 배치하고 컷/슬롯 개수 불일치를 오류로 처리한다
- [ ] 리사이즈 보간이 축소에 적합한 방식이다
- [ ] 필터 파라미터가 §6과 일치한다(alpha/beta/블렌드 비율)
- [ ] 녹화가 H.264 / yuv420p / 30fps / 무음이며 컨테이너가 정상 종료된다
- [ ] 타임랩스 배속이 §7.2 식과 일치한다(또는 미제공으로 명시 축소)
- [ ] 인코더 부재·실패가 예외가 아니라 null/스킵으로 처리된다
- [ ] 화면 이탈 시 시퀀스 취소 → 녹화 정지 → 카메라 정지 순서를 지킨다
