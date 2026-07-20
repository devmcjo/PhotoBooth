# PreToolUse hook: 화면에 보이는 UI를 실행하는 명령 차단.
# 정책: 개발 중 UI 노출 금지. UI 실행(스모크 테스트 포함)은 전체 개발 완료 후
#       사용자 승인 시에만 허용 — 그때 이 훅을 제거/비활성화한다.
$raw = [Console]::In.ReadToEnd()
if (-not $raw) { exit 0 }
try { $payload = $raw | ConvertFrom-Json } catch { exit 0 }
$cmd = [string]$payload.tool_input.command
if (-not $cmd) { exit 0 }

$patterns = @(
    'dotnet(\.exe)?\s+(run|watch|exec)\b',
    'mcphoto[^\s"'']*\.exe',
    '[\\/]bin[\\/](x64[\\/]|x86[\\/])?(debug|release)[\\/][^\s"'']*\.exe',
    '\bstart-process\b',
    '\binvoke-item\b',
    '(^|[;&|(]\s*)(start|ii|saps)(\.exe)?\s',
    'cmd(\.exe)?\s+/c\s+.*\bstart\b',
    '(^|[;&|(]\s*)explorer(\.exe)?(\s|$)',
    '(^|[;&|(]\s*)notepad(\.exe)?\b',
    '(^|[;&|(]\s*)(msedge|chrome|firefox|iexplore|brave|opera)(\.exe)?\b',
    '\brundll32\b'
)

foreach ($p in $patterns) {
    if ($cmd -match "(?i)$p") {
        $reason = "차단됨(정책): 화면에 보이는 UI 실행 금지 — 사용자가 이 PC를 사용 중. 패턴 '$p' 감지. 검증은 headless(dotnet build/test, CLI)만 사용하고, UI 관측이 필요한 완료 기준은 SKIP+사유 기록 후 '사용자 확인 필요 목록'으로 최종 보고하라. UI 실행은 전체 개발 완료 후 사용자 승인 시에만 허용된다."
        $out = @{ hookSpecificOutput = @{ hookEventName = 'PreToolUse'; permissionDecision = 'deny'; permissionDecisionReason = $reason } }
        $out | ConvertTo-Json -Compress -Depth 5
        exit 0
    }
}
exit 0
