# GitHub Desktop와 Releases

이 문서는 로컬 준비 절차입니다. 실제 GitHub 원격 생성·push·릴리즈 게시를 완료했다는 의미는 아닙니다.

## 소스 저장소 올리기

1. GitHub Desktop의 **File → Add local repository**에서 준비한 `pripara-arcade-remote` 폴더를 선택합니다. Git 저장소가 아니라면 Desktop에서 저장소를 만듭니다.
2. 변경 파일을 검토합니다. `src/`, `assets/`, 빌드 도구·문서만 포함하고 `bin/obj/dist/.local-tests/private`와 EXE·게임·로그·덤프는 커밋하지 않습니다.
3. 로컬 커밋 후 **Publish repository**로 본인의 저장소를 만듭니다. 프로젝트 이름은 `pripara-arcade-remote`입니다. 실제 공개 여부는 사용자가 선택합니다.

데이터를 저장소에 그대로 넣으며 Git LFS는 사용하지 않습니다. 현재 가장 큰 파일은 `assets/images.bin`의24,855,484바이트입니다. 소스 ZIP에는 EXE가 없고, 사용자는 저장소만으로 빌드할 수 있습니다.

## Releases에 직접 첨부하기

```powershell
.\build.ps1
.\scripts\Test-Offline.ps1
.\scripts\New-Release.ps1
```

GitHub 웹사이트의 **Releases → Draft a new release**에서 `v1.1.2` 태그를 지정하고 `dist/release/v1.1.2/`의 파일을 첨부합니다. EXE 개별 파일·`data.db`·선택 이미지·full/light ZIP·사용 안내·릴리즈 안내·`SHA256SUMS.txt`가 준비됩니다. 내용을 검토한 뒤 게시합니다.

`scripts/New-SourcePackage.ps1`로 만드는 `dist/pripara-arcade-remote-github-ready.zip`은 **소스 저장소 준비용**입니다. 사용자가 실행할 full/light ZIP과 혼동하지 마세요.

## 태그 워크플로

Windows CI는 Framework compiler로 빌드하고 게임 접근 없는 오프라인 검사만 합니다. 새 `v1.1.2` 태그를 원격으로 push하거나 Actions에서 기존 태그를 지정하면 릴리즈 워크플로가 **draft**를 만듭니다. 이미 같은 태그의 Release가 있으면 덮어쓰지 않고 중단합니다. 수동 게시 전 내용을 검토하세요.

워크플로의 `contents:write`는 이 draft 생성에만 필요합니다. 토큰·비밀번호를 소스에 넣지 않으며, Actions가 제공하는 토큰을 사용합니다. 로컬 빌드·패키징 스크립트는 네트워크나 GitHub 계정을 사용하지 않습니다.

버전을 올릴 때는 의도한 소스 변경과 데이터 검토를 먼저 수행하고 AssemblyVersion·app.manifest·provenance·태그 검증을 함께 갱신합니다. 기존 전체 라이선스를 임의로 새로 부여하지 않습니다.
