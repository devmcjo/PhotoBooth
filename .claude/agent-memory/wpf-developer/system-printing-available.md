---
name: system-printing-available
description: net10.0-windows + UseWPF에서 System.Printing은 PackageReference 없이 참조된다(실측) — 프린터 열거는 추가 의존성 0
metadata:
  type: project
---

`System.Printing`(`LocalPrintServer`·`PrintQueue`)은 **net10.0-windows + `<UseWPF>true</UseWPF>` 프로젝트에서
csproj 수정 없이 그대로 `using`할 수 있다.** it24 Step 5 실측(2026-08-11): `MCPhoto.App`에
`src/MCPhoto.App/Services/SystemPrinterEnumerator.cs`를 추가하고 빌드 성공 — `<Reference>`·
`<FrameworkReference>`·PackageReference 어느 것도 필요 없었다. 이 머신 실측: 설치 프린터 1대
("Microsoft Print to PDF", Local), 열거 소요 100ms 미만, 관리자 권한 불요.

**Why:** 설계가 `System.Drawing.Common`(패키지 추가) vs WMI `Win32_Printer`(인쇄 스택 이원화) 대신
System.Printing을 고른 근거가 "추가 의존성 0"이었고, 그 전제가 미검증 가정(U3)이었다. 실측으로 해소됐다.

**How to apply:**
- 프린터 관련 기능을 확장할 때 패키지를 찾지 말고 `System.Printing`을 바로 쓴다(net10.0-windows·UseWPF 프로젝트 한정 —
  `MCPhoto.Core`는 `net10.0`이라 여기서 못 쓴다. 그래서 Core에는 POCO 계약만 두고 구현은 App에 있다).
- `LocalPrintServer`·`PrintQueue`·`PrintQueueCollection`은 전부 `Dispose` 대상이다. 열거 후 즉시 해제하고
  POCO로 복사해 넘긴다 — VM이 큐 객체를 들면 화면이 살아 있는 동안 스풀러 자원이 잠긴다.
- 기본 생성자를 쓴다(`PrintSystemDesiredAccess.AdministrateServer`를 요구하면 키오스크 계정에서 열거가 통째로 실패).
- 스풀러 중지 상태 실측은 서비스 중지 권한이 필요해 미수행 — 예외 타입에 의존하지 않는 catch-all로 처리했다.
