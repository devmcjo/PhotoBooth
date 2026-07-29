---
name: di-wiring-revert-experiment
description: 배선(DI 등록 형태) 결함 수정을 리뷰할 때 "등록을 옛 형태로 되돌려 테스트가 실제로 실패하는지" 실험해 indirect 근거를 direct로 승격하는 방법
metadata:
  type: feedback
---

배선 결함(“코드는 다 맞는데 아무도 호출하지 않아 생기는 결함”) 수정 리뷰에서 **“이 테스트가 재발을 잡는가?”**는
코드를 읽어서는 indirect 근거밖에 못 만든다. 등록 형태를 **실제로 되돌려 테스트를 돌리면** direct 근거가 된다.

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
