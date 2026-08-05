import { ACCOUNT_MODE_ADMIN, ACCOUNT_MODE_INFO } from "@shell/accountModeIntent";
import { useAccountScreen } from "@screens/account/useAccountScreen";
import { Button } from "@ui/components";
import { OverlayDialog } from "@ui/components/OverlayDialog";
import { PinKeypad } from "@ui/components/PinKeypad";
import { SettingRow, TextField } from "@ui/components/fields";
import { STRINGS } from "@ui/strings";
import styles from "./account.module.css";

/**
 * `Account` 화면 — 내 정보 / 관리자 도구 (03 §13)
 *
 * 이 파일은 **렌더만** 한다. 권한 판정은 `domain/accounts/accountAdminPolicy`가 소유하고
 * 화면은 역할 문자열을 비교하지 않는다(정적 검사 ACC-1).
 *
 * ⚠️ **비밀번호 변경·계정 생성 UI를 만들지 않는다**(it15에서 폐지 — 03 §13.1).
 * ⚠️ **[앱 종료] 버튼을 만들지 않는다**(WD5). [키오스크 종료]가 대체다.
 * ⚠️ PIN 변경·종료 확인은 **화면 로컬 오버레이/인라인**이다 — `pushModal`을 부르지 않는다(ACC-3).
 */

function pinStepTitle(step: "current" | "next" | "confirm"): string {
  if (step === "current") return STRINGS.account.pinCurrent;
  return step === "next" ? STRINGS.account.pinNew : STRINGS.account.pinConfirm;
}

export function AccountView() {
  const screen = useAccountScreen();
  const { user } = screen;

  if (user === null) {
    // 게스트는 팝오버 없이 곧바로 Login으로 가므로 정상 경로에서는 도달하지 않는다.
    return (
      <main className={styles.screen}>
        <p className={styles.note}>{STRINGS.frameEditor.rejectNotLoggedIn}</p>
        <div className={styles.actions}>
          <Button onClick={() => screen.close()}>{STRINGS.common.close}</Button>
        </div>
      </main>
    );
  }

  return (
    <main className={styles.screen}>
      <div className={styles.scroll}>
        <h1 className={styles.title}>{STRINGS.account.title}</h1>

        {/* 모드 전환은 화면 로컬 상태다 — `go()`를 쓰지 않는다(복귀 지점 보존). */}
        <div className={styles.tabs} role="group" aria-label={STRINGS.account.title}>
          <Button
            variant={screen.mode === ACCOUNT_MODE_INFO ? "primary" : "secondary"}
            aria-pressed={screen.mode === ACCOUNT_MODE_INFO}
            onClick={() => screen.setMode(ACCOUNT_MODE_INFO)}
          >
            {STRINGS.account.tabInfo}
          </Button>
          {screen.canAdmin && (
            <Button
              variant={screen.mode === ACCOUNT_MODE_ADMIN ? "primary" : "secondary"}
              aria-pressed={screen.mode === ACCOUNT_MODE_ADMIN}
              onClick={() => screen.setMode(ACCOUNT_MODE_ADMIN)}
            >
              {STRINGS.account.tabAdmin}
            </Button>
          )}
        </div>

        {screen.mode === ACCOUNT_MODE_INFO ? (
          <section className={styles.section} aria-labelledby="account-info">
            <h2 id="account-info" className={styles.sectionTitle}>
              {STRINGS.account.tabInfo}
            </h2>

            <dl className={styles.infoList}>
              {screen.infoRows.map((row) => (
                <div key={row.label} style={{ display: "contents" }}>
                  <dt className={styles.infoKey}>{row.label}</dt>
                  <dd className={styles.infoValue}>{row.value}</dd>
                </div>
              ))}
            </dl>

            <div className={styles.actions}>
              <Button onClick={() => screen.openPinChange()}>{STRINGS.account.changePin}</Button>
            </div>
          </section>
        ) : (
          <section className={styles.section} aria-labelledby="account-admin">
            <h2 id="account-admin" className={styles.sectionTitle}>
              {STRINGS.account.adminTitle}
            </h2>

            {screen.canManageUsers && (
              <div className={styles.actions}>
                <Button variant="primary" onClick={() => screen.openUserMgmt()}>
                  {STRINGS.account.openUserMgmt}
                </Button>
              </div>
            )}

            {screen.canEditLimits && (
              <>
                <h3 className={styles.sectionTitle}>{STRINGS.account.globalLimits}</h3>

                {screen.limits.kind === "failed" && (
                  <p className={styles.warning} role="alert">
                    {STRINGS.account.limitsLoadFailed}
                  </p>
                )}

                {screen.limits.kind === "ready" && (
                  <>
                    <SettingRow label={STRINGS.account.qrHours}>
                      <TextField
                        label={STRINGS.account.qrHours}
                        value={screen.limitsDraft.qrHours}
                        disabled={screen.limitsSaving}
                        onChange={(next) => screen.changeLimit("qrHours", next)}
                      />
                    </SettingRow>
                    <SettingRow label={STRINGS.account.qrCount}>
                      <TextField
                        label={STRINGS.account.qrCount}
                        value={screen.limitsDraft.qrCount}
                        disabled={screen.limitsSaving}
                        onChange={(next) => screen.changeLimit("qrCount", next)}
                      />
                    </SettingRow>
                  </>
                )}

                <div className={styles.actions}>
                  <Button
                    variant="primary"
                    disabled={screen.limits.kind !== "ready" || screen.limitsSaving}
                    onClick={() => screen.saveLimits()}
                  >
                    {STRINGS.common.save}
                  </Button>
                  <Button
                    disabled={screen.limits.kind === "loading"}
                    onClick={() => screen.refreshLimits()}
                  >
                    {STRINGS.settings.serverRecheck}
                  </Button>
                </div>
              </>
            )}

            {/* 파괴적이므로 **인라인 2단 확인**이다(새 셸 모달을 만들지 않는다). */}
            <h3 className={styles.sectionTitle}>{STRINGS.kiosk.exit}</h3>
            <div className={styles.actions}>
              {screen.confirmingExit ? (
                <>
                  <span className={styles.note}>{STRINGS.kiosk.exitConfirm}</span>
                  <Button variant="danger" onClick={() => screen.exitKiosk()}>
                    {STRINGS.kiosk.exit}
                  </Button>
                  <Button variant="ghost" onClick={() => screen.setConfirmingExit(false)}>
                    {STRINGS.common.cancel}
                  </Button>
                </>
              ) : (
                <Button variant="danger" onClick={() => screen.setConfirmingExit(true)}>
                  {STRINGS.kiosk.exit}
                </Button>
              )}
            </div>
          </section>
        )}
      </div>

      <div className={styles.bottomBar}>
        <Button variant="ghost" onClick={() => screen.close()}>
          {STRINGS.common.close}
        </Button>
      </div>

      {screen.pinChange !== null && (
        <OverlayDialog
          title={pinStepTitle(screen.pinChange.step)}
          onCancel={() => screen.closePinChange()}
          initialFocusId="account-pin-cancel"
          actions={
            <Button id="account-pin-cancel" variant="ghost" onClick={() => screen.closePinChange()}>
              {STRINGS.common.cancel}
            </Button>
          }
        >
          <PinKeypad
            value={screen.pinChange.buffer}
            disabled={screen.pinChange.busy}
            onDigit={(digit) => screen.pinDigit(digit)}
            onBackspace={() => screen.pinBackspace()}
            onSubmit={() => screen.submitPin()}
          />
          <p className={styles.pinMessage} role="alert" aria-live="assertive">
            {screen.pinChange.message ?? ""}
          </p>
        </OverlayDialog>
      )}
    </main>
  );
}
