# 제3자 구성요소 고지 (THIRD-PARTY NOTICES)

제3자 구성요소 없음.

몽볼륨은 외부 라이브러리·패키지를 가져다 쓰지 않으며, 제3자 DLL·폰트·소리 등을 함께 배포하지 않는다. 설치본(`몽볼륨.exe`)에는 이 저장소의 소스(`src/`)와 앱 아이콘만 들어 있다.

- 사용하는 것은 Windows에 포함된 .NET Framework 4.x(WPF 포함)와 Windows 코어 오디오 API뿐이며, 시스템 구성요소로서 배포 대상이 아니다.
- 자동 업데이트 모듈(`src/MongUpdate.cs`, `src/MongUpdateUi.Wpf.cs`)은 몽 시리즈 공통 모듈로 Bh-Nakh 자체 저작물이며 GPL-3.0 대상이다.
- 빌드 도구(Roslyn 컴파일러, Inno Setup)는 설치 프로그램을 만들 때만 쓰고 결과물에 포함되지 않는다. 설치 프로그램에는 Inno Setup 런타임이 들어가며, 해당 조건은 Inno Setup 라이선스를 따른다.
