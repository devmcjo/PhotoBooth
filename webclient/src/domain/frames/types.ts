/**
 * 프레임·슬롯 값 타입 — Windows `Models/Slot.cs`·`Models/FrameTemplate.cs` 이식
 *
 * 좌표계는 **프레임 원본 픽셀**이다(화면 좌표가 아니다 — 편집기 변환은 `editorTransform.ts`).
 */

/** 프레임 내 사진 슬롯(칸). */
export interface Slot {
  readonly index: number;
  readonly x: number;
  readonly y: number;
  readonly width: number;
  readonly height: number;
}

/** 이미지 픽셀 크기. */
export interface ImageSize {
  readonly width: number;
  readonly height: number;
}

/** 슬롯 종횡비(가로/세로). 캡처 중앙 크롭 ROI 계산 기준. 높이 0이면 0. */
export function slotAspectRatio(slot: Slot): number {
  return slot.height === 0 ? 0 : slot.width / slot.height;
}

/**
 * 프레임 템플릿(배경 레이어 + 슬롯 배치).
 *
 * `imageUrl`은 출처에 따라 다르다: 서버 프레임 = https URL, OPFS 캐시·번들 = 앱 내부 경로,
 * fallback = 코드 생성. 출처 판정은 **`id` 접두**로 한다(`frameOrigin.ts`).
 */
export interface FrameTemplate {
  readonly id: string;
  /** 소유 계정 id. 공용 기본 프레임은 null. */
  readonly userId: string | null;
  /** 공용 기본 프레임 여부(게스트에게도 노출). */
  readonly isDefault: boolean;
  readonly name: string;
  readonly imageUrl: string;
  readonly imageSize: ImageSize;
  /** 슬롯 1~6개. */
  readonly slots: readonly Slot[];
  /** ISO 8601 문자열. 도메인은 `Date`를 만들지 않는다(주입 원칙). */
  readonly createdAt: string;
}
