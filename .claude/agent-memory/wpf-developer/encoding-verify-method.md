---
name: encoding-verify-method
description: MCPhoto 소스는 no-BOM이 진짜 게이트. git 저장분은 LF지만 autocrlf=true라 워킹카피 CRLF는 정상 — 개행은 diff 오염으로 판정
metadata:
  type: feedback
---

이 저장소(MCPhoto)의 소스 `.cs`/`.xaml`/`.csproj`는 **UTF-8 no-BOM**이 관례이고, **git에 저장된 내용은 LF-only**다.
단 `core.autocrlf=true`(`.gitattributes` 없음)이므로 **워킹카피는 CRLF가 정상**이다 — 워킹카피 CR>0을 위반으로 오판하지 말 것.

**Why:** BOM 혼입은 한글 깨짐·불필요 diff의 실제 원인이라 반드시 막아야 한다. 반면 개행은 autocrlf가
커밋 시 LF로 정규화하므로 워킹카피가 CRLF든 LF든 **커밋 결과가 동일**하다(it15 실측: Edit으로 파일 전체가
LF로 바뀐 경우에도 `git diff`는 실제 변경 줄만 보여줬다). 따라서 개행은 **diff 오염 여부로 판정**하는 것이 옳다.

**How to apply:**
- **BOM(진짜 게이트)**: `head -c 3 <file> | od -An -tx1` → `ef bb bf`면 BOM 있음 = 위반.
  정상값 예: `75 73 69`("usi"=using), `6e 61 6d`("nam"=namespace), `3c 55 73`("<Us"=UserControl),
  `3c 52 65`("<Re"=ResourceDictionary), `3c 50 72`("<Pr"=Project).
  신규 파일도 동일 — Write 툴은 no-BOM으로 쓰므로 그대로 두면 된다.
- **개행**: CR 개수는 `grep -c $'\r'`로 세지 말 것(CR이 없어도 라인 전체를 매칭해 **오탐**).
  정확한 방법은 `tr -cd '\r' < <file> | wc -c`이고, `wc -l`과 같으면 CRLF, 0이면 LF-only.
  최종 판정은 개수가 아니라 **`git diff --numstat` / `git diff`가 실제 변경 줄만 보이는지**로 한다.
- git의 "LF will be replaced by CRLF the next time Git touches it" 경고는 autocrlf 안내일 뿐 문제가 아니다.

관련: [[mcphoto-solution]], [[it15-frame-local-only]]
