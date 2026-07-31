import { useRef } from "react";
import {
  ALLOWED_COUNTDOWN_SECS,
  ALLOWED_CUT_COUNTS,
  ALLOWED_RETAKE_LIMITS,
  MAX_RETENTION_HOURS,
  MIN_RETENTION_HOURS,
  type AppSettingsValues,
} from "@domain/settings/appSettings";
import { AUTO_CUT_COUNT } from "@domain/settings/cutCountPolicy";
import {
  displaySettingValue,
  isSettingEditable,
  settingLockReason,
  type SettingsEditContext,
} from "@domain/settings/settingsEditPolicy";
import { formatBytes } from "@domain/results/byteFormat";
import { describePersistState } from "@adapters/platform/persistStorage";
import { buildCameraOptions, needsPermissionHint } from "@screens/settings/cameraDevicePanel";
import { describeServerStatus } from "@screens/settings/serverStatusPanel";
import { useSettingsScreen } from "@screens/settings/useSettingsScreen";
import { Button, Spinner } from "@ui/components";
import { ChoiceGroup, NumberStepper, SettingRow, TextField, Toggle } from "@ui/components/fields";
import { formatCount, STRINGS } from "@ui/strings";
import styles from "./settings.module.css";

/**
 * `Settings` 화면 — 03 §12 · analysis/41 §2
 *
 * 이 파일은 **렌더만** 한다. 편집 가능 판정·패치 조립·저장 절차는
 * `domain/settings/settingsEditPolicy` · `screens/settings/*`가 소유한다.
 *
 * ⚠️ **여기서 clamp하지 않는다**(정적 검사 SET-1). 컷 수 7 → 6 같은 보정은 저장 후 재반영으로 보인다.
 * ⚠️ 게스트 제한은 4중이다: 렌더 가드(여기) + 액션 가드(`changeSetting`) +
 *    패치 제외(`buildSavePatch`) + 저장소 `omitKeys`. 렌더 가드만으로는 부족하다(M10).
 *
 * 이번 Step에서 **만들지 않은 것**(의도적 이월 — 스텁 문구를 운영자에게 노출하지 않기 위함):
 *   · [프레임 내보내기]/[가져오기] → **Step 15**(로컬 프레임 저장소가 선행)
 *   · [앱 업데이트 확인]          → **Step 16**(Service Worker 등록이 선행)
 *   · [진단·상태]                 → **Step 16**(모달 본체가 선행)
 */

function lockBadgeOf(
  key: keyof AppSettingsValues,
  ctx: SettingsEditContext,
): string | null {
  const reason = settingLockReason(key, ctx);
  if (reason === "guest") return STRINGS.settings.loginRequired;
  if (reason === "qrLimit") return STRINGS.settings.qrLimitBadge;
  return null;
}

export function SettingsView() {
  const screen = useSettingsScreen();
  const { ctx, draft } = screen;
  const values = draft.values;
  const fileInputRef = useRef<HTMLInputElement>(null);

  const locked = (key: keyof AppSettingsValues): boolean => !isSettingEditable(key, ctx);
  const badge = (key: keyof AppSettingsValues): string | null => lockBadgeOf(key, ctx);
  /** 게스트에게는 제한 boolean이 OFF로 보인다(03 §12.3). **저장에는 쓰이지 않는다.** */
  const shown = <K extends keyof AppSettingsValues>(key: K): AppSettingsValues[K] =>
    displaySettingValue(key, values[key], ctx);

  const cameraOptions = buildCameraOptions(screen.devices);
  const serverRows = describeServerStatus(screen.serverStatus);
  const qrBlockedNotice = ctx.qrBlocked ? STRINGS.settings.qrLimitNotice : null;

  return (
    <main className={styles.screen}>
      <div className={styles.scroll}>
        <h1 className={styles.title}>{STRINGS.settings.title}</h1>

        {ctx.isGuest && (
          <p className={styles.banner} role="status">
            {STRINGS.settings.guestBanner}
          </p>
        )}

        {/* ── 1. 촬영 ─────────────────────────────────────────────── */}
        <section className={styles.section} aria-labelledby="settings-capture">
          <h2 id="settings-capture" className={styles.sectionTitle}>
            {STRINGS.settings.sections.capture}
          </h2>

          <SettingRow label={STRINGS.settings.cutCount}>
            <ChoiceGroup
              label={STRINGS.settings.cutCount}
              /* ⚠️ 값 기반 선택이다. "자동"은 sentinel 0이고 clamp 대상이 아니다(WD19). */
              value={values.CutCount}
              options={[
                { value: AUTO_CUT_COUNT, label: STRINGS.settings.cutCountAuto },
                ...ALLOWED_CUT_COUNTS.map((count) => ({ value: count, label: String(count) })),
              ]}
              disabled={locked("CutCount")}
              onChange={(next) => screen.change("CutCount", next)}
            />
          </SettingRow>

          <SettingRow label={STRINGS.settings.countdown}>
            <ChoiceGroup
              label={STRINGS.settings.countdown}
              value={values.CountdownSec}
              options={ALLOWED_COUNTDOWN_SECS.map((sec) => ({
                value: sec,
                label: String(sec),
              }))}
              disabled={locked("CountdownSec")}
              onChange={(next) => screen.change("CountdownSec", next)}
            />
          </SettingRow>

          <SettingRow label={STRINGS.settings.mirrorMode} lockBadge={badge("MirrorMode")}>
            <Toggle
              label={STRINGS.settings.mirrorMode}
              checked={shown("MirrorMode")}
              disabled={locked("MirrorMode")}
              onChange={(next) => screen.change("MirrorMode", next)}
            />
          </SettingRow>

          <SettingRow label={STRINGS.settings.flashMode}>
            <Toggle
              label={STRINGS.settings.flashMode}
              checked={shown("FlashMode")}
              disabled={locked("FlashMode")}
              onChange={(next) => screen.change("FlashMode", next)}
            />
          </SettingRow>

          <SettingRow label={STRINGS.settings.shutterSound}>
            <Toggle
              label={STRINGS.settings.shutterSound}
              checked={shown("ShutterSound")}
              disabled={locked("ShutterSound")}
              onChange={(next) => screen.change("ShutterSound", next)}
            />
          </SettingRow>

          <SettingRow label={STRINGS.settings.retakeEnabled} lockBadge={badge("RetakeEnabled")}>
            <Toggle
              label={STRINGS.settings.retakeEnabled}
              checked={shown("RetakeEnabled")}
              disabled={locked("RetakeEnabled")}
              onChange={(next) => screen.change("RetakeEnabled", next)}
            />
          </SettingRow>

          <SettingRow label={STRINGS.settings.retakeLimit} lockBadge={badge("RetakeLimit")}>
            <ChoiceGroup
              label={STRINGS.settings.retakeLimit}
              value={values.RetakeLimit}
              options={ALLOWED_RETAKE_LIMITS.map((limit) => ({
                value: limit,
                label: String(limit),
              }))}
              disabled={locked("RetakeLimit")}
              onChange={(next) => screen.change("RetakeLimit", next)}
            />
          </SettingRow>
        </section>

        {/* ── 2. 장치 ─────────────────────────────────────────────── */}
        <section className={styles.section} aria-labelledby="settings-device">
          <h2 id="settings-device" className={styles.sectionTitle}>
            {STRINGS.settings.sections.device}
          </h2>

          <SettingRow label={STRINGS.settings.cameraDevice}>
            <div className={styles.actions} role="group" aria-label={STRINGS.settings.cameraDevice}>
              {cameraOptions.map((option, index) => (
                <Button
                  /* deviceId는 권한 전 빈 문자열일 수 있어 index를 섞어 키를 만든다. */
                  key={`${option.deviceId}-${index}`}
                  variant={option.deviceId === values.CameraDevice ? "primary" : "secondary"}
                  aria-pressed={option.deviceId === values.CameraDevice}
                  disabled={locked("CameraDevice")}
                  onClick={() => screen.chooseCamera(screen.devices[index]!)}
                >
                  {option.label}
                </Button>
              ))}
            </div>
          </SettingRow>

          <div className={styles.actions}>
            <Button onClick={() => screen.refreshCameras()}>
              {STRINGS.settings.cameraRescan}
            </Button>
            <Button disabled={cameraOptions.length === 0} onClick={() => screen.openCameraTest()}>
              {STRINGS.settings.cameraTest}
            </Button>
          </div>

          {cameraOptions.length === 0 && (
            <p className={styles.note}>{STRINGS.settings.cameraNone}</p>
          )}
          {needsPermissionHint(cameraOptions) && (
            <p className={styles.note}>{STRINGS.settings.cameraLabelHint}</p>
          )}

          <SettingRow label={STRINGS.settings.cameraFacing}>
            <ChoiceGroup
              label={STRINGS.settings.cameraFacing}
              value={draft.webExtras.CameraFacing}
              options={[
                { value: "user" as const, label: STRINGS.settings.cameraFacingUser },
                { value: "environment" as const, label: STRINGS.settings.cameraFacingEnvironment },
              ]}
              onChange={(next) => screen.chooseFacing(next)}
            />
          </SettingRow>
        </section>

        {/* ── 3. 출력·전송 ────────────────────────────────────────── */}
        <section className={styles.section} aria-labelledby="settings-output">
          <h2 id="settings-output" className={styles.sectionTitle}>
            {STRINGS.settings.sections.output}
          </h2>

          <SettingRow label={STRINGS.settings.outputFormat}>
            <ChoiceGroup
              label={STRINGS.settings.outputFormat}
              value={values.OutputFormat}
              options={[
                { value: "Jpg" as const, label: "JPG" },
                { value: "Png" as const, label: "PNG" },
              ]}
              disabled={locked("OutputFormat")}
              onChange={(next) => screen.change("OutputFormat", next)}
            />
          </SettingRow>

          <SettingRow
            label={STRINGS.settings.enableQrDelivery}
            lockBadge={badge("EnableQrDelivery")}
          >
            <Toggle
              label={STRINGS.settings.enableQrDelivery}
              checked={shown("EnableQrDelivery")}
              disabled={locked("EnableQrDelivery")}
              onChange={(next) => screen.toggleQr("EnableQrDelivery", next)}
            />
          </SettingRow>

          <SettingRow label={STRINGS.settings.sendPhoto} lockBadge={badge("SendPhoto")}>
            <Toggle
              label={STRINGS.settings.sendPhoto}
              checked={shown("SendPhoto")}
              disabled={locked("SendPhoto")}
              onChange={(next) => screen.toggleQr("SendPhoto", next)}
            />
          </SettingRow>

          <SettingRow label={STRINGS.settings.sendTimelapse} lockBadge={badge("SendTimelapse")}>
            <Toggle
              label={STRINGS.settings.sendTimelapse}
              checked={shown("SendTimelapse")}
              disabled={locked("SendTimelapse")}
              onChange={(next) => screen.toggleQr("SendTimelapse", next)}
            />
          </SettingRow>

          <SettingRow label={STRINGS.settings.retentionHours} lockBadge={badge("RetentionHours")}>
            <NumberStepper
              label={STRINGS.settings.retentionHours}
              value={values.RetentionHours}
              min={MIN_RETENTION_HOURS}
              max={MAX_RETENTION_HOURS}
              disabled={locked("RetentionHours")}
              onChange={(next) => screen.change("RetentionHours", next)}
            />
          </SettingRow>

          {qrBlockedNotice !== null && <p className={styles.note}>{qrBlockedNotice}</p>}

          <SettingRow label={STRINGS.settings.saveLocalCopy}>
            <Toggle
              label={STRINGS.settings.saveLocalCopy}
              checked={shown("SaveLocalCopy")}
              disabled={locked("SaveLocalCopy")}
              onChange={(next) => screen.change("SaveLocalCopy", next)}
            />
          </SettingRow>

          {/* ⚠️ 미지원 브라우저(Safari·Firefox·모바일)에서는 렌더하지 않는다 — 05 §5.3. */}
          {screen.folderSupported && (
            <SettingRow
              label={STRINGS.settings.localSaveFolder}
              description={
                values.LocalSavePath.length > 0
                  ? values.LocalSavePath
                  : STRINGS.settings.localSaveFolderNone
              }
            >
              <div className={styles.actions}>
                <Button onClick={() => screen.pickFolder()}>
                  {STRINGS.settings.localSaveFolderPick}
                </Button>
                <Button
                  variant="ghost"
                  disabled={values.LocalSavePath.length === 0}
                  onClick={() => screen.clearFolder()}
                >
                  {STRINGS.settings.localSaveFolderClear}
                </Button>
              </div>
            </SettingRow>
          )}
        </section>

        {/* ── 4. 필터 ─────────────────────────────────────────────── */}
        <section className={styles.section} aria-labelledby="settings-filters">
          <h2 id="settings-filters" className={styles.sectionTitle}>
            {STRINGS.settings.sections.filters}
          </h2>

          <SettingRow
            label={STRINGS.settings.filterNone}
            description={STRINGS.settings.filterNoneNote}
          >
            {/* 원본은 설정 키가 아니다 — 항상 on이고 끌 수 없다(analysis/41 §2.6). */}
            <Toggle
              label={STRINGS.settings.filterNone}
              checked
              disabled
              onChange={() => undefined}
            />
          </SettingRow>

          <SettingRow label={STRINGS.settings.filterGrayscale} lockBadge={badge("FilterGrayscale")}>
            <Toggle
              label={STRINGS.settings.filterGrayscale}
              checked={shown("FilterGrayscale")}
              disabled={locked("FilterGrayscale")}
              onChange={(next) => screen.change("FilterGrayscale", next)}
            />
          </SettingRow>

          <SettingRow
            label={STRINGS.settings.filterBrightness}
            lockBadge={badge("FilterBrightness")}
          >
            <Toggle
              label={STRINGS.settings.filterBrightness}
              checked={shown("FilterBrightness")}
              disabled={locked("FilterBrightness")}
              onChange={(next) => screen.change("FilterBrightness", next)}
            />
          </SettingRow>

          <SettingRow label={STRINGS.settings.filterBeauty} lockBadge={badge("FilterBeauty")}>
            <Toggle
              label={STRINGS.settings.filterBeauty}
              checked={shown("FilterBeauty")}
              disabled={locked("FilterBeauty")}
              onChange={(next) => screen.change("FilterBeauty", next)}
            />
          </SettingRow>
        </section>

        {/* ── 5. 고급 ─────────────────────────────────────────────── */}
        <section className={styles.section} aria-labelledby="settings-advanced">
          <h2 id="settings-advanced" className={styles.sectionTitle}>
            {STRINGS.settings.sections.advanced}
          </h2>

          <SettingRow label={STRINGS.settings.hostingBaseUrl} lockBadge={badge("HostingBaseUrl")}>
            <TextField
              label={STRINGS.settings.hostingBaseUrl}
              value={values.HostingBaseUrl}
              disabled={locked("HostingBaseUrl")}
              onChange={(next) => screen.change("HostingBaseUrl", next)}
            />
          </SettingRow>

          <SettingRow label={STRINGS.settings.storageBucket} lockBadge={badge("StorageBucket")}>
            <TextField
              label={STRINGS.settings.storageBucket}
              value={values.StorageBucket}
              disabled={locked("StorageBucket")}
              onChange={(next) => screen.change("StorageBucket", next)}
            />
          </SettingRow>

          <h3 className={styles.sectionTitle}>{STRINGS.settings.serverStatus}</h3>
          <dl className={styles.statusList}>
            {serverRows.map((row) => (
              <div key={row.label} style={{ display: "contents" }}>
                <dt className={styles.statusKey}>{row.label}</dt>
                <dd className={styles.statusValue}>{row.value}</dd>
              </div>
            ))}
          </dl>
          <div className={styles.actions}>
            <Button
              disabled={screen.serverStatus.kind === "loading"}
              onClick={() => screen.refreshServerStatus()}
            >
              {STRINGS.settings.serverRecheck}
            </Button>
          </div>
        </section>

        {/* ── 6. 저장소·데이터(웹 전용) ───────────────────────────── */}
        <section className={styles.section} aria-labelledby="settings-storage">
          <h2 id="settings-storage" className={styles.sectionTitle}>
            {STRINGS.settings.sections.storage}
          </h2>

          <dl className={styles.statusList}>
            <div style={{ display: "contents" }}>
              <dt className={styles.statusKey}>{STRINGS.settings.storagePersist}</dt>
              <dd className={styles.statusValue}>
                {screen.storage === null
                  ? STRINGS.settings.serverUnknown
                  : describePersistState(screen.storage.persistState)}
              </dd>
            </div>
            <div style={{ display: "contents" }}>
              <dt className={styles.statusKey}>{STRINGS.settings.storageUsage}</dt>
              <dd className={styles.statusValue}>
                {screen.storage === null || screen.storage.usage === null
                  ? STRINGS.settings.serverUnknown
                  : `${formatBytes(screen.storage.usage)} / ${
                      screen.storage.quota === null
                        ? STRINGS.settings.serverUnknown
                        : formatBytes(screen.storage.quota)
                    }`}
              </dd>
            </div>
          </dl>
          <div className={styles.actions}>
            <Button onClick={() => screen.requestPersist()}>
              {STRINGS.settings.storagePersistRequest}
            </Button>
          </div>

          <h3 className={styles.sectionTitle}>{STRINGS.settings.storedResults}</h3>
          {screen.storedResults.loading ? (
            <Spinner />
          ) : (
            <>
              <p className={styles.note}>
                {formatCount(
                  STRINGS.settings.storedResultsCount,
                  screen.storedResults.folders.length,
                )}
                {" · "}
                {formatBytes(screen.storedResults.totalBytes)}
              </p>
              {screen.storedResults.storageLow && (
                <p className={styles.warning} role="alert">
                  {STRINGS.settings.storageLowWarning}
                </p>
              )}

              {screen.storedResults.folders.length === 0 ? (
                <p className={styles.note}>{STRINGS.settings.storedResultsEmpty}</p>
              ) : (
                <ul className={styles.resultList}>
                  {screen.storedResults.folders.map((folder) => (
                    <li key={folder.name} className={styles.resultItem}>
                      <span className={styles.resultName}>{folder.name}</span>
                      <span className={styles.resultBytes}>{formatBytes(folder.bytes)}</span>
                      <Button variant="danger" onClick={() => screen.removeResult(folder.name)}>
                        {STRINGS.common.delete}
                      </Button>
                    </li>
                  ))}
                </ul>
              )}

              {/*
                전체 삭제는 **인라인 2단 확인**이다. 삭제 확인 공용 모달은 만들지 않는다 —
                프레임 삭제 확인도 화면 로컬 오버레이로 확정됐다(03 §790 · Step 15 FR-8).
              */}
              <div className={styles.actions}>
                {screen.confirmingDeleteAll ? (
                  <>
                    <span className={styles.note}>{STRINGS.settings.storedResultsConfirm}</span>
                    <Button variant="danger" onClick={() => screen.removeAllResults()}>
                      {STRINGS.common.delete}
                    </Button>
                    <Button variant="ghost" onClick={() => screen.setConfirmingDeleteAll(false)}>
                      {STRINGS.common.cancel}
                    </Button>
                  </>
                ) : (
                  <Button
                    variant="danger"
                    disabled={screen.storedResults.folders.length === 0}
                    onClick={() => screen.setConfirmingDeleteAll(true)}
                  >
                    {STRINGS.settings.storedResultsDeleteAll}
                  </Button>
                )}
              </div>
            </>
          )}

          <h3 className={styles.sectionTitle}>{STRINGS.settings.exportSettings}</h3>
          <div className={styles.actions}>
            <Button onClick={() => screen.exportSettings()}>
              {STRINGS.settings.exportSettings}
            </Button>
            <Button onClick={() => fileInputRef.current?.click()}>
              {STRINGS.settings.importSettings}
            </Button>
            <input
              ref={fileInputRef}
              type="file"
              accept="application/json,.json"
              hidden
              onChange={(event) => {
                const file = event.target.files?.[0];
                // 같은 파일을 다시 골라도 change가 오도록 값을 비운다.
                event.target.value = "";
                if (file !== undefined) screen.startImport(file);
              }}
            />
          </div>

          {screen.importError !== null && (
            <p className={styles.warning} role="alert">
              {screen.importError}
            </p>
          )}

          {screen.importPreview !== null && (
            <>
              <h3 className={styles.sectionTitle}>{STRINGS.settings.importPreviewTitle}</h3>
              {screen.importPreview.changes.length === 0 ? (
                <p className={styles.note}>{STRINGS.settings.importNoChanges}</p>
              ) : (
                <ul className={styles.previewList}>
                  {screen.importPreview.changes.map((change) => (
                    <li key={change.key} className={styles.previewItem}>
                      {change.key}: {JSON.stringify(change.from)} → {JSON.stringify(change.to)}
                    </li>
                  ))}
                </ul>
              )}
              {screen.importPreview.warnings.map((warning) => (
                <p key={warning} className={styles.note}>
                  {warning}
                </p>
              ))}
              <div className={styles.actions}>
                <Button variant="primary" onClick={() => screen.applyPreview()}>
                  {STRINGS.settings.importApply}
                </Button>
                <Button variant="ghost" onClick={() => screen.cancelImport()}>
                  {STRINGS.settings.importCancel}
                </Button>
              </div>
            </>
          )}
        </section>
      </div>

      {/* 저장/닫기는 스크롤 영역 **밖** sticky다 — 긴 목록에서도 항상 보인다(03 §12.4). */}
      <div className={styles.saveBar}>
        <Button variant="ghost" onClick={() => screen.close()}>
          {STRINGS.common.close}
        </Button>
        <Button variant="primary" onClick={() => screen.save()}>
          {STRINGS.common.save}
        </Button>
      </div>
    </main>
  );
}
