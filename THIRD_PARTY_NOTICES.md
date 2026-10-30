# 구성요소와 라이선스 안내

프로젝트 전체 라이선스는 아직 지정하지 않았습니다. 이 문서는 전체 소스나 데이터에 사용·재배포 허가를 새로 부여하는 문서가 아닙니다.

- `src/`의 C#4개, 앱 manifest와 아이콘은 기존1.1.1 프로그램에서 원문 그대로 옮겼습니다. 파일별 해시는 `source-provenance.json`에 있습니다.
- `assets/data.db`와 `assets/images.bin`은 기존 아케이드용 카탈로그·최적화 썸네일 데이터입니다. 의상명·게임 관련 이미지의 권리는 각 권리자에게 있으며, 소스 코드의 라이선스와 동일하다고 가정하지 않습니다. 원본 게임·모델·세이브·실제 사용자 티켓은 포함하지 않습니다.
- 앱은 Windows의 .NET Framework, Windows Forms, GDI와 `winsqlite3.dll`을 사용합니다. Windows 시스템 DLL이나 별도의 SQLite provider DLL을 이 프로젝트에 재배포하지 않습니다.
- `tools/export_split_data.py`는 데이터 생성용 개발 도구입니다. 이미지 최적화에 선택적으로 사용하는 Pillow는 저장소에 포함하지 않으며 별도 환경의 Pillow 라이선스가 적용됩니다. 앱 실행과 C# 빌드에는 Pillow나 Python이 필요하지 않습니다.

GitHub Actions에서 사용하는 공식 checkout 도구와 GitHub CLI는 CI 실행 환경의 도구이며 사용자 배포 ZIP의 구성요소가 아닙니다.
