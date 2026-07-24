/**
 * Firebase Admin SDK 초기화 — **런타임 기본 서비스계정(ADC)**로만 초기화한다(설계 §0.2, §1.2).
 *
 * 키 파일을 로드하지 않는다. Cloud Functions 런타임/Emulator가 ADC를 자동 주입한다.
 * 이것이 방향 B의 근본 이점: 유출할 키 파일이 어디에도 존재하지 않는다.
 */
import { initializeApp, getApps, App } from "firebase-admin/app";
import { getFirestore, Firestore } from "firebase-admin/firestore";
import { getStorage, Storage } from "firebase-admin/storage";

let app: App | null = null;

function ensureApp(): App {
  if (app) return app;
  // 이미 초기화된 앱이 있으면 재사용(핫 인스턴스/테스트 반복 호출 대비).
  const existing = getApps();
  app = existing.length > 0 ? existing[0] : initializeApp();
  return app;
}

/** Firestore 핸들(ADC 자격). */
export function db(): Firestore {
  return getFirestore(ensureApp());
}

/** Storage 핸들(ADC 자격). */
export function storage(): Storage {
  return getStorage(ensureApp());
}
