# 데이터 형식1

기본 파일명은 `data.db` / `images.bin`입니다. EXE와 같은 폴더에 두고 변경 후 리모컨을 재실행합니다. 실시간 자동 갱신을 보장하지 않습니다.

## data.db

SQLite3 DB이며 `application_id=0x50435231`, `user_version=1`입니다.

| 테이블 | 내용 |
| --- | --- |
| `meta(key TEXT PRIMARY KEY,value TEXT)` | `schema_version=1`, 자유 문자열 `data_version`, 실제 행 수 `item_count/set_count`, 진단용 `image_count` |
| `items` | `id` INTEGER PK, `name/jpName` TEXT, `brand` INTEGER, `brandName/category/model` TEXT, `isDress/sourceRow/thumbnail` INTEGER, `search` TEXT, 선택 출처 `nameSource` TEXT |
| `sets` | `id` TEXT PK, `name/brandName/search` TEXT, `brand/top/bottom/shoes/head` INTEGER |

카테고리는 `top/bottom/shoes/head`, 불리언은0/1입니다. 빈 세트 부위는0입니다. 아이템·부위·세트 참조와 원피스/하의 배제 규칙을 확인합니다. 필수 열·타입을 유지하면 추가 열·추가 meta를 넣을 수 있습니다. 앱은 데이터 버전·행 수·전체 해시를 고정하지 않습니다.

`sourceRow`는 전송할 때의 빠른 조회 힌트입니다. 게임 표에서 실제 ID·부위도 다시 확인하며 행이 바뀌면 ID로 재검색합니다. DB에만 새 아이템을 적는다고 게임에 모델이 생기는 것은 아닙니다. 기존 아케이드에 존재하는 아이템만 사용하세요.

## images.bin

`<아이템ID>.png` 항목을 담은 ZIP입니다. 일부 ID만 있는 이미지 파일도 허용하며, 파일 없음·깨짐·그림 누락은 대체 표시합니다. DB와 이미지의 해시·버전을 강제로 묶지 않습니다. 최대320장을 캐시합니다. 현재 배포 그림은128px 상한이며 원본 비율을 유지하고 작은 그림을 확대하지 않습니다.

## 생성 도구

Python3.10 이상을 사용하는 선택 개발 도구입니다. 입력·출력 경로를 모두 명시하며 기존 게임·EXE·설정을 읽거나 바꾸지 않습니다.

```powershell
python -B tools/export_split_data.py --catalog examples/catalog.example.json --without-images --out-dir .local-tests/example-data
python -B tools/export_split_data.py --catalog YOUR-CATALOG.json --thumbs YOUR-THUMBNAILS.zip --out-dir YOUR-OUTPUT --data-version YOUR-VERSION
```

그림 최적화에는 별도 Pillow 환경이 필요합니다. `--original-images`는 원본 PNG 바이트를 그대로 복사하며 Pillow 없이 실행할 수 있습니다. 기본 adaptive128px/256색은 밝은 부분이 흰 배경으로 합쳐지는 그림을 자동으로 감지해 더 정밀하게 변환합니다. 특정 의상ID 예외는 없습니다. 반투명 그림은 RGBA를 유지합니다.

`--overwrite`를 명시하지 않으면 기존 출력 파일을 덮어쓰지 않습니다. 예제는 소형 형식 설명용이며 동봉6,229개 전체 DB를 대체하는 릴리즈 데이터가 아닙니다.
