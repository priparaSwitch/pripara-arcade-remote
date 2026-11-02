# pripara-arcade-remote

프리파라 아케이드 게임의 코디 화면에서 의상을 골라 입히는 Windows 리모컨입니다. **1.1.3**은 즐겨찾기, ID·이름·등급 정렬, 세트 대표 상의 숨김을 추가합니다.

**실제 GitHub 업로드·Releases 게시·게임 전송은 이 준비 작업에서 수행하지 않았습니다.** 새 빌드의 검사는 가상 메모리와 리모컨 자체 UI·데이터 읽기만 사용합니다.

## 실행 파일을 사용하는 경우

GitHub Releases에서 다음 중 하나를 받습니다.

- `full.zip`: 실행 파일, `data.db`, 그림용 `images.bin`, 사용 안내
- `light.zip`: **같은 실행 파일**과 `data.db`, 사용 안내. 그림 없이 목록과 전송 기능을 사용합니다.

개별 EXE·`data.db`·`images.bin`도 릴리즈 첨부 파일로 준비합니다. EXE와 `data.db`는 같은 폴더에 두고, 그림이 필요하면 `images.bin`도 넣습니다. 데이터 형식1에 맞는 DB·이미지를 교체하고 리모컨을 재실행하면 EXE를 다시 만들 필요가 없습니다.

Windows 10/11 **x64**, .NET Framework와 Windows 내장 SQLite 구성 요소를 사용합니다. 별도의 SQLite DLL·Python·전체 .NET SDK를 설치할 필요는 없습니다. 기존 manifest에 따라 실행 시 관리자 권한 확인이 나올 수 있습니다.

게임에서 지원하는 코디 화면을 연 뒤 리모컨에서 의상을 고르고 전송합니다. DEBUG ON/OFF 모두 기존 처리 그대로이며, 다른 장면·다른 캐릭터 타입·유효하지 않은 아이템·전환 중인 상태에는 전송을 제한합니다. 게임 EXE의 전체 해시를 허용 목록으로 고정하지 않습니다.

탭 순서는 **★ 즐겨찾기 → 의상세트 → 전체 → 헤어 액세서리 → 상의 원피스 → 하의 → 슈즈**입니다. 카드 우측 상단 별표는 즐겨찾기만 변경하며 게임에 전송하지 않습니다. 즐겨찾기는 사용자별 로컬 설정에 저장됩니다. 전체·상의 목록에서는 NS 카탈로그에서 확인한 세트 대표 ID 174개를 숨깁니다. 우측 정렬 메뉴는 ID·이름·등급과 오름차순·내림차순을 지원합니다. 현재 배포 DB에는 등급값이 없어 등급순서는 ID순서로 표시됩니다. 추후 `data.db`의 `item_meta.grade`에 등급값을 제공하면 EXE 변경 없이 반영됩니다.

게임·세이브·카메라 프로그램·가상카메라 드라이버·웹 패처·QR 도구는 포함하지 않습니다. 이 도구는 게임 파일을 패치하는 프로그램이 아닙니다.

## 소스에서 빌드

Windows의 .NET Framework x64 C# compiler(`csc.exe`)만 사용합니다. NuGet이나 전체 .NET SDK, 게임 원본은 필요하지 않습니다. C# 소스, 아이콘, manifest와 외부 데이터가 저장소에 포함되어 있습니다.

```powershell
.\scripts\Verify-Repository.ps1
.\build.ps1
.\scripts\Test-Offline.ps1
.\scripts\New-Release.ps1
```

- 실행용 빌드: `bin/Release/`
- 릴리즈 첨부 파일: `dist/release/v1.1.3/`
- GitHub 소스 준비 ZIP: `scripts/New-SourcePackage.ps1` 실행 후 `dist/`

실행용 full/light ZIP과 GitHub 소스 준비 ZIP은 서로 다릅니다. 출력·검사 결과는 Git에서 제외합니다. 자세한 내용은 [빌드 안내](docs/BUILD.md), [데이터 형식](docs/DATA_FORMAT.md), [GitHub Desktop·Releases 안내](docs/RELEASE.md)를 참고하세요.

프로젝트 전체 라이선스는 아직 지정하지 않았습니다. 소스·데이터에 임의로 MIT 등의 라이선스를 부여하지 않습니다. [구성요소와 권리 안내](THIRD_PARTY_NOTICES.md)를 함께 확인하세요.
