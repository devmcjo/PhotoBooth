# 서드파티 라이선스 고지 (MC포토 웹 클라이언트)

배포물(`web/kiosk/` 정적 번들)에 포함되는 서드파티 코드의 라이선스 목록이다.
상용 배포 시 이 문서를 함께 제공한다. 새 런타임 의존을 추가하면 **여기에 먼저 적는다**.

> 웹 클라이언트의 타임랩스 인코딩은 **브라우저 내장 인코더**(WebCodecs / MediaRecorder)를 쓴다.
> Windows 클라이언트와 달리 **ffmpeg(GPLv3) 노출이 없다**(`12 B14`).

| 패키지 | 버전 | 라이선스 | 용도 | 비고 |
|--------|------|----------|------|------|
| react | 18.3.1 | MIT | UI 렌더링 | — |
| react-dom | 18.3.1 | MIT | UI 렌더링 | — |
| zustand | 5.0.2 | MIT | 상태 관리 | — |
| mp4-muxer | 5.2.2 | MIT | WebCodecs 출력(H.264 chunk)을 MP4 컨테이너로 muxing | 상류에서 deprecated(후속 `mediabunny`는 MPL-2.0이라 미채택). MIT이므로 필요 시 vendoring 가능 |
| qrcode-generator | 2.0.4 | MIT | QR 코드 모듈 행렬 생성(ECC **Q** — Windows `QrService.cs`와 일치) | **런타임 의존 0**, 자체 `.d.ts` 동봉. 우리가 쓰는 표면은 `qrcode` · `addData` · `make` · `getModuleCount` · `isDark` **5개뿐**이라 교체 비용이 `src/adapters/qr/qrService.ts` 한 파일에 국한된다. HTML 문자열을 만드는 `createImgTag`/`createSvgTag`는 **쓰지 않는다**(`innerHTML` 경로 회피) |

개발 전용 의존(`devDependencies`)은 배포물에 포함되지 않으므로 목록에서 제외한다.

## `mp4-muxer` deprecated 경고에 대한 판단

`npm install` 시 `"This library is superseded by Mediabunny."` 경고가 뜬다. 그럼에도 채택한 근거:

1. **라이선스가 우리 기준(MIT/Apache-2.0 계열)을 만족하는 유일한 후보**다. 후속작 `mediabunny`는
   MPL-2.0(파일 단위 카피레프트)이고 unpacked 크기가 약 64배다.
2. 기능이 고정된 라이브러리다. 우리가 쓰는 표면은 `Muxer` · `ArrayBufferTarget` ·
   `addVideoChunk` · `finalize` **4개뿐**이며 MP4 컨테이너 규격은 변하지 않는다.
3. **런타임 CDN 로드가 아니다**(01 §7). 번들에 포함되고 `package-lock.json`으로 고정되므로
   패키지가 npm에서 사라져도 재현 빌드가 깨지지 않는다. MIT라 최악의 경우 **vendoring**이 합법이다.
4. 표면이 4개뿐이라 교체 비용이 `src/adapters/encode/encode.worker.ts` 한 파일에 국한된다
   (`createMuxer` 포트 뒤에 가둬 두었다).

## 라이선스 전문
- MIT License 전문: 각 패키지의 `node_modules/{pkg}/LICENSE` 참조.
  배포 패키징 시 위 5개 패키지의 LICENSE 파일을 함께 동봉한다.
  ⚠️ `qrcode-generator`는 별도 `LICENSE` 파일 없이 **소스 헤더 주석**(`dist/qrcode.js`·`dist/qrcode.d.ts`)에
  MIT 고지가 들어 있다(Copyright (c) 2009 Kazuhiko Arase). 동봉 시 그 헤더를 옮겨 적는다.
