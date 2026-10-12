# 차량 크기 조사와 제작 기준

확인일: 2026-10-13. 사용자는 구급차의 크기 기준을 현대 스타리아, 버스를 현대 일렉시티로 승인했다. 그림만으로 모든 차량의 정확한 연식/특장 사양을 단정할 수 없으므로 아래에 명시한 제조사 제원을 크기 모델로 사용한다.

| 파일 | 크기 모델 | 전장×전폭(mm) | 공식 출처 |
| --- | --- | --- | --- |
| Ambulance | 스타리아 크기 기준 | 5,255×1,995 | [현대 카탈로그](https://www.hyundai.com/contents/repn-car/catalog/staria-lounge-limousine-catalog.pdf) |
| Avante | 2023 더 뉴 아반떼 CN7 | 4,710×1,825 | [현대 헤리티지 제원](https://www.hyundai.com/kr/ko/brand/brandstory/heritage/2023-the-new-avante) |
| Bus | 일렉시티 | 10,995×2,490 | [현대 공식 제원](https://www.hyundai.com/kr/ko/c/products/bus/elec-city) |
| Carnival | 카니발 | 5,155×1,995 | [기아 공식 제원](https://www.kia.com/kr/vehicles/carnival/specification) |
| Grandeur | 그랜저 GN7 | 5,035×1,880 | [현대 카탈로그](https://www.hyundai.com/contents/repn-car/catalog/grandeur-catalog.pdf) |
| PCX | PCX 125 JK05 | 1,935×740 | [혼다 공식 제원](https://www.honda.co.jp/PCX/) |
| PorterII | LPG 2WD 초장축 슈퍼캡, 범퍼가드 제외 | 5,100×1,740 | [현대 카탈로그](https://www.hyundai.com/contents/repn-car/catalog/porter2-catalog.pdf) |
| Ray | 레이 | 3,595×1,595 | [기아 공식 제원](https://www.kia.com/kr/vehicles/ray/specification) |
| SonataTaxi | 쏘나타 택시 | 4,945×1,860 | [현대 택시 전용 카탈로그](https://www.hyundai.com/contents/repn-car/catalog/sonata-taxi-catalog.pdf) |
| Sorento | 쏘렌토 | 4,815×1,900 | [기아 공식 제원](https://www.kia.com/kr/vehicles/sorento/specification) |
| Sportage | 스포티지 | 4,685×1,865 | [기아 공식 제원](https://www.kia.com/kr/vehicles/sportage/specification) |

쏘나타 택시는 일반 쏘나타의 제원을 대입하지 않고 택시 전용 카탈로그의 4,945mm를 사용한다. 포터는 전기 모델의 5,105mm와 LPG 슈퍼캡 5,100mm가 다르므로 후자를 명시했다. 탑차 그림의 정확한 특장 제조사와 특장 사양은 확인되지 않았다. 그림에 해당하는 실차를 특정했다는 뜻은 아니다.

현재 GamePlay 구급차는 Sprite의 284×472px 사각형, PPU 100, Transform Scale `(0.2,0.2,0.2)`를 사용한다. 따라서 렌더링 경계의 기준 크기는 가로 0.568, 세로 0.944 월드 유닛이다. SpriteRenderer의 `m_Size`는 Simple 모드에서는 실제 크기 기준이 아니므로 사용하지 않는다.

각 차량의 표시 크기는 기존 구급차의 가로·세로 축 비율을 유지한다.

- 표시 폭 = 0.568 × 차량 전폭 / 1,995
- 표시 길이 = 0.944 × 차량 전장 / 5,255
- 모든 Prefab 루트 Transform Scale은 `(0.2,0.2,0.2)`

기존 구급차 그림의 종횡비가 실차 전장/전폭 비율과 다르므로 이 계산은 구급차를 기준으로 두 축의 상대 크기를 맞추는 것이다. 월드 유닛=미터인 단일 축척으로 해석하면 안 된다. 원본 이미지나 기존 구급차 씬 설정은 변경하지 않는다.

11개 PNG 모두 동일한 300×500 캔버스지만 투명 여백과 Sprite 슬라이싱이 서로 다르다. 따라서 파일 크기만으로 맞추지 않고 차량 전체의 표시 경계를 기준으로 보정한다. 버스와 레이의 기존 잘게 나뉜 Sprite를 단독으로 선택하지 않는다. 제조사 전폭은 차체 제원이고 그림에는 미러 등의 돌출부가 포함되어 있으므로, 그림 전체 외곽을 그 전폭의 게임 표시 범위에 대응시키는 시각 기준이다.

조사 결과의 기계 판독 파일: `vehicle-specs.json`.

검증(Verification): 기존 씬의 구급차 Transform/Sprite/PPU와 11개 PNG의 해상도·알파 경계를 읽기 전용으로 확인했다. 11종 표시 크기 계산을 확인했고 `calculated-size-preview.png`를 시각 검토했다. 이 이미지는 Unity Game View 캡처가 아니다.

## 실제 저장 결과

사용자의 실제 저장 요청에 따라 설치된 Unity 실행 경로를 확인했다. `C:/Program Files/Unity/Hub/Editor/6000.3.23f1/Editor/Unity.exe`의 버전이 프로젝트의 `6000.3.23f1`과 일치한다.

원본 프로젝트가 다른 프로세스에 의해 잠겨 있어 `unity-save-01/project/` 격리 복사본에서 Unity를 실행했다. 사용자의 에디터는 종료하지 않았다. 초기 제한 실행의 라이선스 초기화 정체와 배치 모드의 초기 빈 씬 제약을 해결한 뒤, 최종 실행은 정상 종료 코드 0으로 완료했다. 최종 실행에서 기존 라이선스 채널 연결 경고 후 해당 버전의 새 채널로 연결해 라이선스가 초기화됐고, 실제 생성·검증까지 완료됐다.

- `PrefabUtility`와 `AssetDatabase`로 **11개 Prefab + 11개 전체 Sprite 에셋**을 저장했다.
- Unity에서 저장한 Prefab 11개를 `LoadPrefabContents`로 다시 열어 루트 Scale `(0.2,0.2,0.2)`와 렌더링 경계의 크기를 검사했다. 결과: `prefab-verification.json`.
- 생성된 에셋과 Unity가 생성한 `.meta`를 함께 원본 `Assets/Prefabs/Car/`에 전달했다. `Sprites/` 하위 폴더는 전체 이미지 Sprite를 담는다.
- 전달 후 **224개 정적 검사 통과**: 11종, 고유 GUID, 원본 PNG 참조, 루트 Scale, 전장/전폭 표시 비율, 활성 Renderer와 표시 순서. 결과: `delivery-verification.json`.
- 작업 시작 전에 있던 Assets/Packages/ProjectSettings의 **304개 파일 해시가 그대로 유지**됐다. 기존 GamePlay 씬과 Cafe24 폰트의 사용자 변경도 보존했다.
- Game View의 실제 렌더링 캡처와 Play Mode 물리 주행은 실행하지 않았다. 최종 Unity 실행은 `-batchmode -nographics`이며 저장/재로딩/기하 크기 검증이다.

생성 도구는 작업 출력 폴더에 보관했다. 원본 프로젝트의 런타임/에디터 스크립트는 추가하거나 수정하지 않았다. Prefab은 루트 아래 `Visual` 자식에 표시용 SpriteRenderer를 갖고, 자식 Scale에서 두 축을 보정한다. 원본 PNG와 원본 .meta는 변경하지 않았다. 차량 AI, Rigidbody와 Collider는 이번 표시용 Prefab에 추가하지 않았다.

최종 실행 로그: `unity-save-01/unity-create-retry.log`. 격리 프로젝트 캐시는 `.gitignore`의 `project/`로 제외했다. AGENTS.md는 수정하지 않았다.
