================================================================================
FFmpeg — 오픈소스 고지 및 소스 코드 제공 안내
FFmpeg — Open Source Notice and Written Offer for Source Code
================================================================================

MC포토(MCPhoto)는 동영상 녹화 및 타임랩스 생성을 위해 FFmpeg를 사용합니다.
This software uses libraries from the FFmpeg project.

FFmpeg는 MC포토와 **별도의 실행 파일**(tools/ffmpeg/ffmpeg.exe)로 동봉되며,
명령행 인자와 표준 입출력(파이프)으로만 통신합니다. MC포토 자체 소스 코드는
MIT 라이선스로 배포됩니다(설치 폴더의 LICENSE 파일 참조).

FFmpeg is bundled as a SEPARATE EXECUTABLE and is invoked as a subprocess,
communicating only via command-line arguments and standard I/O pipes.
MCPhoto's own source code is licensed under the MIT License.


--------------------------------------------------------------------------------
1. 동봉된 FFmpeg 바이너리 정보 / Bundled binary
--------------------------------------------------------------------------------

  파일 / File        : tools/ffmpeg/ffmpeg.exe
  버전 / Version     : ffmpeg version 8.1.2-essentials_build-www.gyan.dev
  저작권 / Copyright : Copyright (c) 2000-2026 the FFmpeg developers
  빌드 / Built with  : gcc 16.1.0 (Rev2, Built by MSYS2 project)
  배포처 / Publisher : CODEX FFMPEG @ gyan.dev  (https://www.gyan.dev/ffmpeg/builds/)
  라이선스 / License : GNU General Public License version 3 (GPLv3) or later

  FFmpeg는 FFmpeg 개발자들의 저작물이며, 저작권은 각 기여자에게 있습니다.
  이 소프트웨어에 동봉된 바이너리에는 위 configuration에 나열된 여러 서드파티
  라이브러리(libx264 등)가 정적 링크되어 있고, 각 라이브러리의 저작권 또한
  해당 권리자에게 있습니다. 전체 저작권 표시는 아래 3항의 대응 소스에 포함된
  각 프로젝트의 COPYING/LICENSE/AUTHORS 파일에서 확인할 수 있습니다.

  FFmpeg is copyright of the FFmpeg developers and its contributors. The bundled
  binary statically links third-party libraries (libx264 and others listed in the
  configuration above); their respective copyrights belong to their holders. The
  complete copyright notices are included in the Corresponding Source (section 3).

  이 바이너리는 수정 없이 그대로 재배포됩니다.
  This binary is redistributed WITHOUT MODIFICATION.

  라이선스가 GPLv3인 이유: 아래 configuration의 --enable-gpl 및 --enable-version3
  옵션으로 빌드되었기 때문입니다(GPL 라이브러리인 libx264 등이 정적 링크됨).

  Full configuration string (as reported by `ffmpeg -version`):

    --enable-gpl --enable-version3 --enable-static --disable-w32threads
    --disable-autodetect --enable-cairo --enable-fontconfig --enable-iconv
    --enable-gnutls --enable-libxml2 --enable-gmp --enable-bzlib --enable-lzma
    --enable-zlib --enable-libsrt --enable-libssh --enable-libzmq --enable-avisynth
    --enable-sdl2 --enable-libwebp --enable-libx264 --enable-libx265 --enable-libxvid
    --enable-libaom --enable-libopenjpeg --enable-libvpx --enable-mediafoundation
    --enable-libass --enable-libfreetype --enable-libfribidi --enable-libharfbuzz
    --enable-libvidstab --enable-libvmaf --enable-libzimg --enable-amf
    --enable-cuda-llvm --enable-cuvid --enable-dxva2 --enable-d3d11va
    --enable-d3d12va --enable-ffnvcodec --enable-libvpl --enable-nvdec --enable-nvenc
    --enable-vaapi --enable-openal --enable-libgme --enable-libopenmpt
    --enable-libopencore-amrwb --enable-libmp3lame --enable-libtheora
    --enable-libvo-amrwbenc --enable-libgsm --enable-libopencore-amrnb --enable-libopus
    --enable-libspeex --enable-libvorbis --enable-librubberband


--------------------------------------------------------------------------------
2. 라이선스 전문 / Full license text
--------------------------------------------------------------------------------

  GPLv3 전문은 같은 폴더의 아래 파일에 있습니다.
  The complete text of the GPLv3 is provided in:

      FFmpeg-COPYING.GPLv3.txt

  FFmpeg 프로젝트의 라이선스 정책: https://ffmpeg.org/legal.html


--------------------------------------------------------------------------------
3. 대응 소스 코드 (Corresponding Source)
--------------------------------------------------------------------------------

  GPLv3 제6조에 따라, 위 바이너리에 대응하는 완전한 소스 코드를 아래에서
  받으실 수 있습니다. 여기에는 FFmpeg 소스와 정적 링크된 모든 라이브러리의
  소스, 그리고 빌드에 사용된 스크립트가 포함됩니다.

  In accordance with GPLv3 Section 6, the complete Corresponding Source for the
  bundled binary — including the sources of all statically linked libraries and
  the scripts used to control compilation — is available at:

    (1) 빌드 소스 및 빌드 스크립트 / Build sources and scripts
        https://github.com/GyanD/codexffmpeg
        (해당 릴리스 태그: 8.1.2)

    (2) FFmpeg 업스트림 소스 / FFmpeg upstream source
        https://ffmpeg.org/download.html
        https://git.ffmpeg.org/ffmpeg.git   (tag: n8.1.2)

  ※ 위 주소가 접속되지 않는 경우 아래 4항의 서면 제공 오퍼를 이용해 주십시오.


--------------------------------------------------------------------------------
4. 서면 소스 제공 오퍼 (Written Offer) — 3년간 유효
--------------------------------------------------------------------------------

  본 소프트웨어를 배포받은 날로부터 최소 3년간, 아래 연락처로 요청하시면
  위 바이너리에 대응하는 완전한 소스 코드를 매체 비용 이하의 실비만 받고
  제공해 드립니다. 이 오퍼는 본 소프트웨어의 사본을 가진 누구에게나 유효합니다.

  For at least three (3) years from the date you received this software, we will
  provide, to anyone who possesses a copy of this software, a complete
  machine-readable copy of the Corresponding Source for the bundled FFmpeg
  binary, for a charge no more than our cost of physically performing the
  source distribution.

      연락처 / Contact : devmcjo@gmail.com
      제목 예시 / Subject example : "MCPhoto FFmpeg source request"

  요청 시 아래 정보를 함께 알려주시면 처리가 빠릅니다.
      - MC포토 버전 (앱의 설정 > 진단·상태 화면에서 확인 가능)
      - FFmpeg 버전 (위 1항의 버전 문자열)


--------------------------------------------------------------------------------
5. 추가 제약 없음 / No additional restrictions
--------------------------------------------------------------------------------

  MC포토의 이용 약관은 동봉된 FFmpeg 바이너리에 대해 GPLv3가 부여하는 권리를
  제한하지 않습니다. 귀하는 GPLv3가 허용하는 범위에서 해당 바이너리를 자유롭게
  사용·복제·수정·재배포할 수 있습니다.

  No terms of MCPhoto impose any further restrictions on the rights granted by
  the GPLv3 with respect to the bundled FFmpeg binary.


--------------------------------------------------------------------------------
6. 참고 / Note
--------------------------------------------------------------------------------

  FFmpeg 및 관련 상표는 각 권리자의 자산입니다.
  본 고지는 MC포토가 FFmpeg 프로젝트의 보증이나 추천을 받았음을 의미하지 않습니다.

  FFmpeg is a trademark of Fabrice Bellard, originator of the FFmpeg project.
  This notice does not imply endorsement by the FFmpeg project.
