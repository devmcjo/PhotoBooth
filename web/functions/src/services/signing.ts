/**
 * GCS V4 서명 PUT URL 발급 + Firebase 다운로드 토큰 메타데이터 설정(설계 §5.4-A).
 *
 * 파일 바이트가 함수를 경유하지 않도록 클라가 서명 URL로 직접 PUT한다. 다운로드 토큰 URL이
 * 동작하려면 객체 메타 `firebaseStorageDownloadTokens`가 필요하다(§4.3). 서명에 이 메타 헤더를
 * 포함시키고, 클라가 PUT 시 동일 헤더를 보내야 메타가 설정된다.
 *
 * 반환 URL은 UploadContract.TokenDownloadUrl 형식(WPF·웹 공통 계약)으로 조립한다.
 */
import { randomUUID } from "node:crypto";
import { storage } from "../firebase";
import { tokenDownloadUrl } from "../domain/session";

/** 다운로드 토큰을 심는 커스텀 메타 헤더명(GCS는 x-goog-meta-{key}로 메타 수신). */
export const DOWNLOAD_TOKEN_META_KEY = "firebaseStorageDownloadTokens";
const DOWNLOAD_TOKEN_HEADER = `x-goog-meta-${DOWNLOAD_TOKEN_META_KEY}`;

/** 서명 PUT URL 유효시간(분). 짧게 유지(설계 §5.4). */
const PUT_URL_TTL_MINUTES = 15;

/**
 * Storage Emulator 실행 여부. Emulator에서는 getSignedUrl(V4)이 서비스 계정 client_email 없이는
 * 동작하지 않으므로("Cannot sign data without client_email"), Emulator일 때만 서명을 우회하고
 * Emulator 업로드 URL을 반환한다. **배포 환경엔 이 env가 없으므로 프로덕션은 항상 서명 경로**를 탄다.
 */
function storageEmulatorHost(): string | null {
  // FIREBASE_STORAGE_EMULATOR_HOST=host:port 형식(스킴 없음).
  const host = process.env.FIREBASE_STORAGE_EMULATOR_HOST;
  return host ? host : null;
}

/**
 * Emulator 업로드 URL 조립. Storage Emulator는 인증 없이 media 업로드를 받는다:
 * POST http://{host}/upload/storage/v1/b/{bucket}/o?name={path}&uploadType=media
 * 다운로드 토큰 메타는 Emulator가 자동 생성하지 않으므로, 스모크는 putUrl/downloadUrl의
 * 존재·형식만 검증한다(실제 파일 GET은 스모크 범위 밖).
 */
function emulatorPutUrl(host: string, bucketName: string, storagePath: string): string {
  const encodedName = encodeURIComponent(storagePath);
  return `http://${host}/upload/storage/v1/b/${bucketName}/o?name=${encodedName}&uploadType=media`;
}

export interface SignedUpload {
  /** 클라가 파일을 직접 PUT할 서명 URL. */
  putUrl: string;
  /** 업로드 완료 후 파일을 읽을 다운로드 토큰 URL(문서에 저장). */
  downloadUrl: string;
  /** PUT 시 클라가 반드시 함께 보내야 하는 헤더(서명에 포함됨). */
  requiredHeaders: Record<string, string>;
}

/**
 * 지정 경로에 대한 서명 PUT URL과 다운로드 토큰 URL을 발급한다.
 * @param bucketName Storage 버킷명(설정값).
 * @param storagePath results/... 또는 frames/... 경로.
 * @param contentType PUT 시 Content-Type(서명에 포함).
 */
export async function createSignedUpload(
  bucketName: string,
  storagePath: string,
  contentType: string
): Promise<SignedUpload> {
  const downloadToken = randomUUID();

  const emuHost = storageEmulatorHost();
  if (emuHost) {
    // Emulator 경로: 서명 불가 → Emulator 업로드 URL. 다운로드 URL은 계약 형식 그대로 조립.
    return {
      putUrl: emulatorPutUrl(emuHost, bucketName, storagePath),
      downloadUrl: tokenDownloadUrl(bucketName, storagePath, downloadToken),
      requiredHeaders: {
        "Content-Type": contentType,
        [DOWNLOAD_TOKEN_HEADER]: downloadToken,
      },
    };
  }

  const bucket = storage().bucket(bucketName);
  const file = bucket.file(storagePath);

  const extensionHeaders: Record<string, string> = {
    [DOWNLOAD_TOKEN_HEADER]: downloadToken,
  };

  const [putUrl] = await file.getSignedUrl({
    version: "v4",
    action: "write",
    expires: Date.now() + PUT_URL_TTL_MINUTES * 60 * 1000,
    contentType,
    extensionHeaders,
  });

  return {
    putUrl,
    downloadUrl: tokenDownloadUrl(bucketName, storagePath, downloadToken),
    requiredHeaders: {
      "Content-Type": contentType,
      [DOWNLOAD_TOKEN_HEADER]: downloadToken,
    },
  };
}

/** Storage prefix 하위 객체를 모두 삭제(프레임 이미지 정리·cascade용). */
export async function deleteStoragePrefix(
  bucketName: string,
  prefix: string
): Promise<void> {
  const bucket = storage().bucket(bucketName);
  await bucket.deleteFiles({ prefix, force: true });
}
