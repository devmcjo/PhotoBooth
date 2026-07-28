---
name: design-doc-incremental-write
description: 설계 문서는 절 단위로 나눠 Write→Edit append로 저장한다(한 호출 8000자 미만) — 한 번에 몰아 쓰면 연결 끊김 시 통째로 유실
metadata:
  type: feedback
---

MCPhoto 설계 문서(`docs/design/wpf-itNN-*.md`)는 **§0~§1만 Write로 생성한 뒤 절마다 별도 Edit 호출로 append**한다.
한 도구 호출에 8,000자 이상 쓰지 않는다.

**Why:** 앞선 이터레이션에서 설계 문서를 한 번의 Write로 몰아 쓰던 중 API 연결이 끊겨 **산출물이 통째로 유실**됐다.
증분 저장하면 끊겨도 직전 절까지는 디스크에 남아 재개할 수 있다. (team-lead가 it16 착수 시 명시 지시)

**How to apply:**
- 절 경계(§N)마다 Edit 1회. 마지막 줄을 `old_string`으로 잡아 append하는 방식이 안전하다.
- 조사(코드 읽기)를 먼저 끝내고 쓰기를 시작해야 절 순서가 뒤엉키지 않는다.
- 완성 후 `head -c 3 <file> | od -An -tx1`로 BOM 없음(≠ `ef bb bf`)만 확인하면 된다.

관련: [[it15-frame-local-only-policy]]
