import type { FrameTemplate, Slot } from "@domain/frames/types";
import { getBackendClient, type BackendClient } from "./backendClient";

/**
 * 프레임 API — analysis/31 §4.10~4.13 · 06 §2
 *
 * ⚠️ **`PUT /frames/{id}` 함수를 만들지 않는다.** 프레임 편집은 로컬 전용 정책이며(analysis/13 §6.4)
 *    함수가 존재하는 것만으로도 나중에 누군가 호출하는 경로가 생긴다. 없는 것이 방어다.
 */

/** 서버 프레임 DTO(응답). */
interface FrameDto {
  readonly id?: unknown;
  readonly userId?: unknown;
  readonly isDefault?: unknown;
  readonly name?: unknown;
  readonly imageUrl?: unknown;
  readonly imageSize?: unknown;
  readonly slots?: unknown;
  readonly createdAt?: unknown;
}

function parseSlots(raw: unknown): Slot[] {
  if (!Array.isArray(raw)) return [];
  const slots: Slot[] = [];
  for (const item of raw) {
    if (typeof item !== "object" || item === null) continue;
    const record = item as Record<string, unknown>;
    const values = ["index", "x", "y", "width", "height"].map((key) =>
      typeof record[key] === "number" ? (record[key] as number) : null,
    );
    if (values.some((v) => v === null)) continue;
    const [index, x, y, width, height] = values as number[];
    slots.push({ index: index!, x: x!, y: y!, width: width!, height: height! });
  }
  return slots;
}

export function parseFrame(raw: unknown): FrameTemplate | null {
  if (typeof raw !== "object" || raw === null) return null;
  const dto = raw as FrameDto;
  if (typeof dto.id !== "string" || dto.id.length === 0) return null;
  if (typeof dto.name !== "string") return null;

  const size = (dto.imageSize ?? {}) as Record<string, unknown>;
  return {
    id: dto.id,
    userId: typeof dto.userId === "string" ? dto.userId : null,
    isDefault: dto.isDefault === true,
    name: dto.name,
    imageUrl: typeof dto.imageUrl === "string" ? dto.imageUrl : "",
    imageSize: {
      width: typeof size.width === "number" ? size.width : 0,
      height: typeof size.height === "number" ? size.height : 0,
    },
    slots: parseSlots(dto.slots),
    createdAt: typeof dto.createdAt === "string" ? dto.createdAt : "",
  };
}

/** `POST /frames` 요청 — 이미지는 별 서명 PUT으로 올린다(Step 15). */
export interface CreateFrameRequest {
  readonly name: string;
  readonly imageSize: { readonly width: number; readonly height: number };
  readonly slots: readonly Slot[];
  readonly ext: "png" | "jpg";
  readonly contentType: string;
}

export interface CreateFrameResponse {
  readonly frame: FrameTemplate | null;
  /** 이미지 업로드용 서명 PUT URL(있을 때). */
  readonly putUrl: string | null;
  readonly requiredHeaders: Readonly<Record<string, string>>;
}

export interface FrameRepository {
  /** 공용 기본 프레임 목록(게이트 키만 — 게스트도 조회 가능). */
  getDefaultFrames(): Promise<FrameTemplate[]>;
  /** 서버 개인 프레임(레거시 — 보통 빈 배열). */
  getUserFrames(userId: string): Promise<FrameTemplate[]>;
  /** power 전용 공용 프레임 등록. */
  createFrame(request: CreateFrameRequest): Promise<CreateFrameResponse>;
  /** power 전용 서버 삭제. */
  deleteFrame(id: string): Promise<void>;
}

export function createFrameRepository(
  client: BackendClient = getBackendClient(),
): FrameRepository {
  function parseList(raw: unknown): FrameTemplate[] {
    const items = Array.isArray(raw)
      ? raw
      : typeof raw === "object" && raw !== null && Array.isArray((raw as { frames?: unknown }).frames)
        ? ((raw as { frames: unknown[] }).frames)
        : [];
    return items.map(parseFrame).filter((f): f is FrameTemplate => f !== null);
  }

  return {
    async getDefaultFrames() {
      return parseList(await client.request<unknown>({ path: "frames/default" }));
    },

    async getUserFrames(userId) {
      return parseList(
        await client.request<unknown>({
          path: "frames",
          query: { userId },
          auth: "required",
        }),
      );
    },

    async createFrame(request) {
      const raw = await client.request<unknown>({
        method: "POST",
        path: "frames",
        body: request,
        auth: "required",
      });
      const record = (typeof raw === "object" && raw !== null ? raw : {}) as Record<string, unknown>;
      const headers = record.requiredHeaders;
      return {
        frame: parseFrame(record.frame ?? raw),
        putUrl: typeof record.putUrl === "string" ? record.putUrl : null,
        requiredHeaders:
          typeof headers === "object" && headers !== null
            ? (headers as Record<string, string>)
            : {},
      };
    },

    async deleteFrame(id) {
      await client.request<unknown>({
        method: "DELETE",
        path: `frames/${encodeURIComponent(id)}`,
        auth: "required",
      });
    },
  };
}
