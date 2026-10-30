# 빌드와 오프라인 검사

Windows 10/11 x64와 .NET Framework의 `Framework64/v4.0.30319/csc.exe`가 필요합니다. 전체 .NET SDK·Visual Studio·NuGet·게임 원본은 필요하지 않습니다. Windows PowerShell5.1 또는 PowerShell7에서 프로젝트 폴더를 기준으로 실행합니다.

```powershell
.\scripts\Verify-Repository.ps1
.\build.ps1
.\scripts\Test-Offline.ps1
.\scripts\Test-CleanBuild.ps1
```

스크립트는 현재 작업 폴더가 달라도 자신의 위치를 기준으로 소스·아이콘·데이터를 찾습니다. 경로에 공백이 있어도 사용할 수 있습니다.

```powershell
.\build.ps1 -OutputDirectory 'bin\Other Folder'
.\build.ps1 -OutputDirectory 'bin\Light' -WithoutImages
.\build.ps1 -OutputDirectory 'bin\CodeOnly' -CodeOnly
```

`-WithoutImages`는 기존 그림을 지우지 않습니다. 그림 파일이 이미 있는 출력 폴더라면 새 폴더를 사용하라는 오류를 냅니다. `-CodeOnly`는 실행 파일만 만들며 실행에 필요한 `data.db`는 별도로 준비해야 합니다.

다른 형식1 데이터는 `-DataDirectory`로 지정할 수 있습니다. 일반 앱은 카탈로그 크기·버전·해시를 고정하지 않지만, 동봉한 원본 회귀검사 `SelfTests`는 준비된6,229/823 데이터셋을 기준으로 합니다. 변경 데이터에는 그 데이터에 맞는 별도 검사를 적용하세요.

`Test-Offline.ps1`은 새 폴더에 동일 EXE와 데이터를 복사해 full/light 각각15개 검사를 실행합니다. 여기에는 `testMode`로 만든 실제 화면의 버전 표시·전체 탭·검색·선택 검사가 포함됩니다. 테스트 호스트의 실행 진입점은 `--self-test`로 제한하며 관리자 manifest의 EXE를 OS에서 직접 실행하지 않습니다. 게임 읽기용 `--status`, 전송용 `--send-twinkle-test`는 CI에서 호출하지 않습니다.

실제 Windows 내장 SQLite 로드와 전체5,783 이미지의 GDI 읽기, 깨진 선택 이미지 파일도 검사합니다. `Test-CleanBuild.ps1`은 공개 소스만 공백이 있는 새 폴더로 복사한 뒤 다시 빌드·검사합니다. 이 결과는 새 실게임 인식·착용 시험이 아닙니다.

기존 Framework compiler는 매 빌드의 EXE 해시가 같다는 보장을 하지 않습니다. 릴리즈마다 생성되는 `SHA256SUMS.txt`로 해당 파일을 확인하세요. 현재 C# 원문의 무결성은 `source-provenance.json`, 변경하지 않은 외부 데이터는 `assets/manifest.json`으로 확인합니다. 1.1.2의 변경 범위는 UI이며 기존17바이트 전송 로직은 유지합니다.
