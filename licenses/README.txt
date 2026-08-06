================================================================================
MC포토(MCPhoto) — 오픈소스 라이선스 고지
Open Source Licenses
================================================================================

MC포토 본체는 MIT 라이선스로 배포됩니다. 전문은 이 폴더의
MCPhoto-LICENSE-MIT.txt 파일에 있습니다.

이 폴더에는 MC포토가 **재배포하는** 제3자 구성 요소의 라이선스 고지와
소스 코드 제공 안내도 함께 들어 있습니다.

MCPhoto itself is distributed under the MIT License; the full text is in
MCPhoto-LICENSE-MIT.txt in this folder. This folder also contains license
notices for third-party components that are REDISTRIBUTED with MCPhoto.


--------------------------------------------------------------------------------
MC포토 본체 / MCPhoto itself
--------------------------------------------------------------------------------

  라이선스 : MIT License
  전문     : MCPhoto-LICENSE-MIT.txt


--------------------------------------------------------------------------------
동봉된 제3자 구성 요소 / Redistributed third-party components
--------------------------------------------------------------------------------

  FFmpeg  (tools/ffmpeg/ffmpeg.exe)
      라이선스 : GNU General Public License v3 or later (GPLv3+)
      고지·소스 안내 : FFmpeg-README.txt
      라이선스 전문   : FFmpeg-COPYING.GPLv3.txt

      ※ FFmpeg는 MC포토와 별도의 실행 파일이며 서브프로세스로 호출됩니다.
        MC포토 소스 코드는 이로 인해 GPL의 적용을 받지 않습니다.


--------------------------------------------------------------------------------
동봉되지 않는 구성 요소 / Components NOT redistributed
--------------------------------------------------------------------------------

  NuGet 패키지 등 빌드 시점에 참조되는 라이브러리는 각자의 라이선스를 따르며,
  대부분 MIT/Apache-2.0 등 고지 의무가 가벼운 허용적 라이선스입니다.
  전체 목록은 소스 저장소의 프로젝트 파일(*.csproj)에서 확인할 수 있습니다.


--------------------------------------------------------------------------------
문의 / Contact
--------------------------------------------------------------------------------

  devmcjo@gmail.com
