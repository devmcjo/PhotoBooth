/**
 * 도메인 계층 공개 API — **순수 TS만** 있다(브라우저·React·Node import 0).
 * 이 규칙은 `tests/unit/domain/purity.test.ts`가 기계적으로 고정한다.
 */

export * from "./mathCompat";

export * from "./navigation/appState";
export * from "./navigation/stateMachine";
export * from "./navigation/idleCountdown";

export * from "./capture/cropRect";
export * from "./capture/centerCrop";
export * from "./capture/previewReadiness";
export * from "./capture/captureSession";
export * from "./capture/timelapseSpeed";
export * from "./capture/timelapsePlan";
export * from "./capture/timelapseSpool";
export * from "./capture/slotPlacement";

export * from "./frames/types";
export * from "./frames/slotAspect";
export * from "./frames/slotLayout";
export * from "./frames/editorTransform";
export * from "./frames/frameOrigin";
export * from "./frames/frameEditPolicy";
export * from "./frames/frameNaming";
export * from "./frames/slotsFile";
export * from "./frames/frameCatalogPolicy";
export * from "./frames/frameLoadPolicy";
export * from "./frames/frameCatalogProgress";
export * from "./frames/frameStorePolicy";
export * from "./frames/frameSavePolicy";
export * from "./frames/frameImagePolicy";
export * from "./frames/bundleManifest";
export * from "./frames/fallbackFrameSpec";

export * from "./settings/appSettings";
export * from "./settings/cutCountPolicy";
export * from "./settings/qrDeliveryPolicy";
export * from "./settings/qrEffectivePolicy";
export * from "./settings/settingsEditPolicy";
export * from "./settings/settingsImport";

export * from "./roles/userRole";
export * from "./roles/roleChangePolicy";

export * from "./upload/uploadContract";
export * from "./upload/uploadOrchestration";
export * from "./upload/qrRenderPlan";
export * from "./upload/exportFileName";

export * from "./results/resultNaming";
export * from "./results/resultSavePlan";
export * from "./results/resultsRetention";
export * from "./results/byteFormat";

export * from "./filters/filterParams";
