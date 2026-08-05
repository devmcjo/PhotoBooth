import { useCallback, useEffect, useRef, useState } from "react";
import {
  readCameraPermission,
  watchCameraPermission,
  type CameraPermission,
} from "@adapters/camera/cameraPermission";

/**
 * 카메라 권한 상태 구독 훅 — 03 §2·§5 · 07 §3
 *
 * **조회만** 한다. 프롬프트는 뜨지 않는다 — 그것은 사용자 제스처 안의
 * `requestCameraPermission()`뿐이다. 화면에 들어가는 것만으로 LED가 켜지면 안 된다.
 *
 * ⚠️ `<StrictMode>`는 effect를 mount → cleanup → mount 로 2회 돌린다. 구독은 cleanup에서
 *    반드시 해제되므로 **동시에 두 개가 붙지 않는다**. 조회는 멱등이라 2회째가 1회째를
 *    무효화하지 않는다(Step 12·13에서 회귀를 낸 "cleanup이 1회성 소비를 취소하는" 형태가 아니다).
 * ⚠️ 언마운트 후 `setState`를 막기 위한 가드는 **표시 갱신만** 막는다 — 진행 중인 조회 자체를
 *    중단하지는 않는다.
 */
export interface CameraPermissionHook {
  readonly permission: CameraPermission;
  /**
   * 다시 조회한다. `permissions` API가 없는 브라우저(Safari)는 `change` 이벤트가 오지 않으므로,
   * 권한 요청 직후 호출자가 이것을 불러 화면을 맞춘다.
   */
  readonly refresh: () => void;
}

export function useCameraPermission(): CameraPermissionHook {
  const [permission, setPermission] = useState<CameraPermission>(null);
  const mountedRef = useRef(true);
  /** 조회 순서가 뒤집혀 오래된 결과가 최신 값을 덮는 것을 막는다. */
  const requestIdRef = useRef(0);

  const read = useCallback((): void => {
    const id = requestIdRef.current + 1;
    requestIdRef.current = id;
    void readCameraPermission().then((next) => {
      if (!mountedRef.current || requestIdRef.current !== id) return;
      setPermission(next);
    });
  }, []);

  useEffect(() => {
    mountedRef.current = true;
    read();
    // 반환값은 **해제 함수**다. 미지원 브라우저에서도 no-op 해제자가 와서 분기가 필요 없다.
    const unwatch = watchCameraPermission((next) => {
      if (!mountedRef.current) return;
      setPermission(next);
    });
    return () => {
      mountedRef.current = false;
      unwatch();
    };
  }, [read]);

  return { permission, refresh: read };
}
