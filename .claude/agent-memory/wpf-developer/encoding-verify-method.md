---
name: encoding-verify-method
description: 이 프로젝트 .cs/.xaml은 no-BOM + LF. CR 카운트는 grep 말고 tr로 세야 정확(오탐 주의)
metadata:
  type: feedback
---

이 저장소(MCPhoto)의 소스 `.cs`/`.xaml`은 **UTF-8 no-BOM + LF-only**가 관례다(`.gitattributes` 없음, git core.autocrlf 영향으로 워킹카피가 CRLF로 바뀔 수 있으니 저장 후 반드시 확인).

**Why:** BOM 변경/CRLF 혼입은 한글 깨짐·diff 오염의 원인. 커밋된 원본이 LF-only인지 `git show HEAD:<file>`로, 워킹카피를 파일에서 직접 검증해야 한다.

**How to apply:** CR 개수는 `grep -c $'\r'`로 세지 말 것 — grep은 CR이 없어도 라인 전체를 매칭해 **CRLF로 오탐**한다. 정확한 방법:
- BOM: `head -c 3 <file> | od -An -tx1` → `75 73 69`("usi") 또는 `3c 55 73`("<Us")면 no-BOM. `ef bb bf`면 BOM 있음(비관례).
- CR: `tr -cd '\r' < <file> | wc -c` → 0이면 LF-only.
- 원본 대비: `git show HEAD:<file> | tr -cd '\r' | wc -c` 와 비교.

git의 "LF will be replaced by CRLF the next time Git touches it" 경고는 autocrlf 설정 경고일 뿐, 현재 워킹카피가 LF-only(CR=0)면 문제 없다.
