import type { UserRowPolicy } from "@domain/accounts/accountAdminPolicy";
import type { UserRole } from "@domain/roles/userRole";
import { roleLabel } from "@domain/roles/userRole";
import { formatIsoDate } from "@screens/account/accountInfoRows";
import { useUserMgmtScreen } from "@screens/userMgmt/useUserMgmtScreen";
import { Button, Spinner } from "@ui/components";
import { OverlayDialog } from "@ui/components/OverlayDialog";
import { PinKeypad } from "@ui/components/PinKeypad";
import { Select } from "@ui/components/fields";
import { formatCount, STRINGS } from "@ui/strings";
import styles from "./userMgmt.module.css";

/**
 * `UserMgmt` 화면 — power 전용 사용자 관리 (03 §14)
 *
 * 이 파일은 **렌더만** 한다. 능력 판정은 `buildUserRows`가 만든 `UserRowPolicy`가 전부이고
 * 화면은 역할 문자열을 비교하지 않는다(정적 검사 ACC-1).
 *
 * ⚠️ manager가 다른 manager 행을 볼 때 **[삭제]는 있고 [PIN]은 없다**. 비대칭이 규격이다
 *    (`canManage`는 동급 허용 · `canResetPin`은 동급 차단 — analysis/60 §1.3.1).
 * ⚠️ 표(넓은 화면)와 카드(좁은 화면)가 **같은 `UserRowActions`를 공유**한다. 한쪽에만 가드를
 *    넣으면 좁은 화면에서 [PIN]이 되살아난다.
 * ⚠️ 가로 스크롤을 만들지 않는다(03 §1.2).
 */

function createdAtText(iso: string): string {
  const formatted = formatIsoDate(iso);
  return formatted.length > 0 ? formatted : STRINGS.account.unknown;
}

interface RowActionsProps {
  readonly row: UserRowPolicy;
  readonly busy: boolean;
  readonly confirmingDeleteId: string | null;
  readonly onConfirmDelete: (id: string | null) => void;
  readonly onDelete: (row: UserRowPolicy) => void;
  readonly onChangeRole: (row: UserRowPolicy, next: UserRole) => void;
  readonly onResetPin: (row: UserRowPolicy) => void;
}

/** 행 1개의 조작부. 표·카드가 공유한다(렌더 가드가 한 곳이다). */
function UserRowActions({
  row,
  busy,
  confirmingDeleteId,
  onConfirmDelete,
  onDelete,
  onChangeRole,
  onResetPin,
}: RowActionsProps) {
  const confirming = confirmingDeleteId === row.user.id;

  return (
    <div className={styles.rowActions}>
      {row.assignableRoles.length > 0 && (
        <Select
          label={`${STRINGS.userMgmt.roleLabel}: ${row.user.id}`}
          value={row.user.role}
          disabled={busy}
          options={row.assignableRoles.map((role) => ({ value: role, label: roleLabel(role) }))}
          onChange={(next) => onChangeRole(row, next)}
        />
      )}

      {row.canResetPin && (
        <Button disabled={busy} onClick={() => onResetPin(row)}>
          {STRINGS.userMgmt.resetPin}
        </Button>
      )}

      {row.canDelete &&
        (confirming ? (
          <>
            <span className={styles.confirmText}>
              {formatCount(STRINGS.userMgmt.deleteConfirm, row.user.id)}
            </span>
            <Button variant="danger" disabled={busy} onClick={() => onDelete(row)}>
              {STRINGS.common.delete}
            </Button>
            <Button variant="ghost" disabled={busy} onClick={() => onConfirmDelete(null)}>
              {STRINGS.common.cancel}
            </Button>
          </>
        ) : (
          <Button variant="danger" disabled={busy} onClick={() => onConfirmDelete(row.user.id)}>
            {STRINGS.common.delete}
          </Button>
        ))}
    </div>
  );
}

export function UserMgmtView() {
  const screen = useUserMgmtScreen();
  const { view } = screen;

  const actions = (row: UserRowPolicy) => (
    <UserRowActions
      row={row}
      busy={screen.busy}
      confirmingDeleteId={screen.confirmingDeleteId}
      onConfirmDelete={(id) => screen.setConfirmingDeleteId(id)}
      onDelete={(target) => screen.deleteAccount(target.user)}
      onChangeRole={(target, next) => screen.changeRole(target.user, next)}
      onResetPin={(target) => screen.openPinReset(target.user)}
    />
  );

  return (
    <main className={styles.screen}>
      <div className={styles.scroll}>
        <h1 className={styles.title}>{STRINGS.userMgmt.title}</h1>

        {view.kind === "loading" && <Spinner />}

        {view.kind === "failed" && (
          /* ⚠️ 실패는 **빈 목록과 시각적으로 달라야 한다**(03 §14). */
          <div className={styles.failed} role="alert">
            <p className={styles.failedText}>{STRINGS.userMgmt.loadFailed}</p>
            <Button onClick={() => screen.refresh()}>{STRINGS.common.retry}</Button>
          </div>
        )}

        {view.kind === "ready" && (
          <>
            <p className={styles.summary}>{formatCount(STRINGS.userMgmt.total, view.total)}</p>

            {view.rows.length === 0 ? (
              <p className={styles.note}>{STRINGS.userMgmt.empty}</p>
            ) : (
              <>
                {/* 넓은 화면: 표. 정렬은 고정이라 헤더에 ▼만 표기한다(it19 — 안내 문구 없음). */}
                <table className={styles.table}>
                  <thead>
                    <tr>
                      <th scope="col">{STRINGS.userMgmt.colId}</th>
                      <th scope="col">{STRINGS.userMgmt.colEmail}</th>
                      <th scope="col">{STRINGS.userMgmt.colRole} ▼</th>
                      <th scope="col">{STRINGS.userMgmt.colCreatedAt}</th>
                      <th scope="col">{STRINGS.userMgmt.colActions}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {view.rows.map((row) => (
                      <tr key={row.user.id}>
                        <td>{row.user.id}</td>
                        <td>{row.user.email ?? STRINGS.account.none}</td>
                        <td>{roleLabel(row.user.role)}</td>
                        <td>{createdAtText(row.user.createdAt)}</td>
                        <td>{actions(row)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>

                {/* 좁은 화면: 카드. 같은 `UserRowPolicy`를 같은 컴포넌트로 렌더한다. */}
                <ul className={styles.cards}>
                  {view.rows.map((row) => (
                    <li key={row.user.id} className={styles.card}>
                      <div className={styles.cardHead}>
                        <span className={styles.cardId}>{row.user.id}</span>
                        <span className={styles.roleBadge}>{roleLabel(row.user.role)}</span>
                      </div>
                      <p className={styles.cardMeta}>
                        {row.user.email ?? STRINGS.account.none}
                        {" · "}
                        {createdAtText(row.user.createdAt)}
                      </p>
                      {actions(row)}
                    </li>
                  ))}
                </ul>
              </>
            )}
          </>
        )}
      </div>

      <div className={styles.bottomBar}>
        <Button variant="ghost" onClick={() => screen.back()}>
          {STRINGS.userMgmt.back}
        </Button>
        <Button disabled={view.kind === "loading"} onClick={() => screen.refresh()}>
          {STRINGS.settings.serverRecheck}
        </Button>
      </div>

      {screen.pinReset !== null && (
        <OverlayDialog
          title={`${STRINGS.userMgmt.pinResetTitle} — ${screen.pinReset.target.id}`}
          onCancel={() => screen.closePinReset()}
          initialFocusId="usermgmt-pin-cancel"
          actions={
            <Button
              id="usermgmt-pin-cancel"
              variant="ghost"
              onClick={() => screen.closePinReset()}
            >
              {STRINGS.common.cancel}
            </Button>
          }
        >
          <p className={styles.note}>
            {screen.pinReset.step === "first"
              ? STRINGS.account.pinNew
              : STRINGS.account.pinConfirm}
          </p>
          <PinKeypad
            value={screen.pinReset.buffer}
            disabled={screen.pinReset.busy}
            onDigit={(digit) => screen.pinDigit(digit)}
            onBackspace={() => screen.pinBackspace()}
            onSubmit={() => screen.submitPinReset()}
          />
          <p className={styles.pinMessage} role="alert" aria-live="assertive">
            {screen.pinReset.message ?? ""}
          </p>
        </OverlayDialog>
      )}
    </main>
  );
}
