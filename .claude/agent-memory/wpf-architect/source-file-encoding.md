---
name: source-file-encoding
description: MCPhoto .cs 소스는 UTF-8 (BOM 없음) — 한글 주석 포함, 수정/신규 파일도 no BOM 유지
metadata:
  type: project
---

MCPhoto의 `.cs` 소스 파일은 **UTF-8 (BOM 없음)**으로 저장됨. 실측: `FirebaseClient.cs`/`ServiceRegistration.cs`/`UploadContract.cs` 선두 3바이트가 `75 73 69`(="usi", BOM `EF BB BF` 아님). 한글 XML doc 주석·문자열 다수 포함.

프로젝트 관례: nullable enable, file-scoped namespace(`namespace X;`), XML doc 한글 주석.

**Why**: 파일 수정 시 인코딩을 UTF-8 with BOM 등으로 바꾸면 불필요한 diff·잠재적 mojibake 위험.
**How to apply**: 기존 `.cs` 수정 시 UTF-8 no BOM 유지. 신규 `.cs`도 no BOM으로 생성. 설계 명세에 인코딩 보존 항목 포함.
