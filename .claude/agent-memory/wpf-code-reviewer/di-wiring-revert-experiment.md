---
name: di-wiring-revert-experiment
description: 리뷰 대상 변경을 일시적으로 되돌리는(mutation) 실험으로 "테스트가 그 불변식을 실제로 강제하는가"를 실측해 indirect 근거를 direct로 승격하는 방법
metadata:
  type: feedback
---

**“이 테스트가 재발을 잡는가?”**는 코드를 읽어서는 indirect 근거밖에 못 만든다. 리뷰 대상 코드를 **일시적으로
되돌려(mutation) 테스트를 돌리면** direct 근거가 된다. DI 배선뿐 아니라 가드 조건·상태 리셋·XAML 불변식 전부에 쓴다.

**되돌릴 가치가 있는 것**: 세션/상태 enum 대입, 가드의 예외 조건(`!= EditOwnLocal` → `!false`), 권한 축
(`IsPower()` → `CanWriteFrames()`), 실패 후 `return` → 낙하, XAML `DataContext=` 부착. `sed -i` + 유일 ASCII 앵커로
바이트 안전하게 치환하고 `cp` 백업에서 복원하면 CRLF·BOM이 보존된다(Edit 도구는 개행을 정규화할 위험이 있다).

**이 실험만이 잡는 결함 유형 — 자명하게 통과하는(tautological) 테스트**: 2026-07-30 프레임 서버등록 팝업 리뷰에서
“팝업 열 때 체크박스 리셋”을 검증한다는 테스트가, 리셋 코드를 **삭제해도 38/38 통과**했다. 시퀀스가
`Cancel()`(자체 리셋 보유)을 먼저 거쳐서 단언이 이미 참이었기 때문이다. 제목·주석만 읽으면 절대 안 보인다.
6개 mutation 중 5개는 정상적으로 실패(10 / 1 / 2 / 1 / 1건) → 커버리지 증명, 1개는 구멍 발견.

```bash
cp src/<Proj>/ServiceRegistration.cs "$SCRATCH/ServiceRegistration.cs.bak"   # 1) 백업
# 2) Edit으로 옛 등록 한 줄로 되돌림 (예: AddSingleton<IBackendSession, BackendSession>())
dotnet test <sln> -c Debug --nologo --filter "FullyQualifiedName~<대상테스트클래스>"
# 3) Edit으로 복원 → diff로 바이트 동일 확인
diff "$SCRATCH/ServiceRegistration.cs.bak" src/<Proj>/ServiceRegistration.cs && git diff --numstat -- <파일>
```

**함정(실제로 밟았다)**: 실험 뒤 최종 5회 연속 검증을 `dotnet test --no-build`로 돌리면 **되돌린 코드로 컴파일된
바이너리**를 그대로 재사용해 5회 전부 실패한다. 복원 후에는 반드시 `dotnet build --no-incremental`을 먼저 돌린다.

**Why:** 리뷰 지시에 “등록 형태를 되돌렸을 때 실패하는 테스트가 있는가”가 자주 포함되는데, 테스트 소스를 읽는
것만으로는 컨테이너 해석 순서·`TryAdd` 우선순위 때문에 오판할 수 있다. 실측(2026-07-29, 로그아웃 JWT 수정)에서는
8건 중 4건이 실패해 커버리지가 증명됐다 — 홀더가 둘로 갈라지는 경우도 같은 4건이 잡는다.

**How to apply:** 합성 루트/DI 등록 형태가 diff에 보이면 이 실험을 리뷰 절차에 넣고 결과(실패 테스트 이름·건수)를
보고서에 붙인다. 백업→복원→`diff` 무차이 확인까지가 1세트다. 관련: [[photobooth-logout-paths]]
