# 대표 맵 Asset 4종 제작 기록

## 범위와 기준

- 기준 main: `56d81bc9fa6e75b538aa2b572823e29519fe368d`. 작업 전에 `git fetch origin main`으로 HEAD와 원격 main의 일치를 확인했다.
- `CodeBlueRush_MapAssetStyleGuide.md`를 제작 규격으로, `CodeBlueRush_MapAssetProductionGuide.md`를 제작 범위로 사용했다.
- `Assets/Images/Maps/Reference/CodeBlueRush_Map_MasterReference.png`를 직접 확인하고 네 번의 생성 요청에 모두 스타일 참조로 전달했다.
- 도구: built-in `image_gen`. 실제 전달한 프롬프트는 `CodeBlueRush_MapAssetRepresentativePrompts.json`에 기록했다.
- 대표 PNG 네 개만 생성했다. 추가 Variant, 게임플레이 코드 변경, 기존 Prefab 교체, commit/push는 수행하지 않았다.
- 생성 원본의 해상도와 알파를 그대로 보존했다. 픽셀 편집, 리사이즈, 배경 합성으로 생성 결과를 대체하지 않았다.

## 저장 파일

| 경로 (`Assets/Images/Maps/` 기준) | 실제 원본 해상도 | 형식 | 표현 |
| --- | --- | --- | --- |
| `Ground/Ground_Grass_Default_01.png` | 1254 × 1254 | RGB PNG | 밝은 황록색, 넓고 낮은 대비의 면 |
| `Road/Road_Straight_2Lane.png` | 897 × 1752 | RGB PNG | 세로 왕복 2차선, 회청색 차도, 황색 중앙 실선, 흰색 가장자리 선 |
| `Sidewalk/Sidewalk_Straight_01.png` | 724 × 2172 | RGB PNG | 밝은 베이지 회색 보도, 왼쪽 연석, 큰 보도판 |
| `Nature/Tree/Tree_Medium_01.png` | 1254 × 1254 | RGBA PNG | 단일 둥근 수관, 큰 녹색 면, 짧은 줄기와 오른쪽 아래 접지 그림자 |

가이드의 제작 해상도는 권장값이다. 생성 도구가 요청 해상도와 다른 크기를 반환했으므로 위 실제 원본값을 사용한다. 파일 이름만 512/256 규격인 것처럼 취급하지 않는다.

## 기존 구조에서 확인한 치수

### Road

- `Assets/Editor/Road/RoadSamples.cs`의 `CreateSurface` 및 `Assets/Prefabs/Road/Straight2Lane.asset` 확인.
- 차도 본체: x = -2 ~ +2, y = 0 ~ 20. **폭 4, 길이 20** Unity 단위.
- 방향별 차선 중심: x = -1, +1. 기존 `TrafficLane`을 기준으로 운행한다.
- 기존 메시의 흰선은 중심 x = ±2, 반폭 0.04로 차도 바깥까지 그려져 전체 메시 AABB는 **폭 4.08, 길이 20**이다.
- 기존 중앙선 메시 폭은 0.1이다. 이번 이미지의 선 굵기는 아래 픽셀 기준이며 기존 메시 마킹과 완전히 동일하지는 않다.
- 기존 Road는 `MeshRenderer`/`MeshFilter` 방식이다. PNG를 추가하는 것만으로 기존 메시가 교체되지는 않는다.

### City

- `Assets/Editor/Map/CitySamples.cs`, `Assets/Scripts/Map/CityMap.cs`, `Assets/Prefabs/City/Straight.prefab` 확인.
- 셀은 20 × 20. City 직선 포트는 (0,-10), (0,+10), 루트 스케일은 1.
- Road의 시작점 원점과 City의 셀 중심 원점은 서로 다르다. 새 Sprite의 Center Pivot은 City 루트 중심에 대응한다.
- Road Prefab에 시각 자식으로 붙일 경우 중심은 로컬 (0,10), City Prefab에 붙일 경우 중심은 로컬 (0,0)이다.
- 기존 `Surface` 자식을 그대로 재활용하면 기존 -10 오프셋까지 중복 적용하지 않도록 해야 한다.

### Sidewalk / Tree / Ambulance

- `Assets/Prefabs/Environment/Sidewalk.prefab`: 기본 Square Sprite 0.16 × 시각 자식 scale 6.25 = 기본 폭 1.
- City 직선의 인도 인스턴스는 root scale (0.6,10)으로 **0.6 × 10** 단위이며 기존 배치 및 `SidewalkPath`에는 x=±5 경로가 있다.
- `Assets/Prefabs/Environment/Tree.prefab`: 기본 Square Sprite 0.16 × 5.625 = 시각 폭 **0.9** 단위.
- 구급차 원본 PPU는 700, Collider는 1 × 1.9. 차량 원본 제작 밀도는 새 맵 Asset의 공통 PPU로 복사하지 않았다.

## 공통 Import 및 배치 기준

네 PNG의 `.meta`에 공통으로 적용:

- Texture Type: Sprite (2D and UI)
- Sprite Mode: Single
- PPU: **100** (이번 대표 맵 Asset의 공통 기준)
- Mesh: Full Rect
- Pivot: Center (0.5, 0.5)
- Filter: Bilinear / Wrap: Clamp / Mip Map: Off
- Compression: None / Max Size: 4096 / NPOT Scale: None
- Alpha Is Transparency: On. 나무의 생성 알파를 보존한다.

원본 픽셀 수 / PPU는 임포트된 Sprite의 기본 크기다. 실제 월드 크기는 **시각 자식 Transform**에서 아래처럼 맞춘다. RoadChunk 루트나 차선 Transform은 스케일하지 않는다.

| Asset | 기준 월드 크기 | 시각 자식 Scale X | Scale Y |
| --- | --- | --- | --- |
| Ground | 20 × 20 (City 1셀) | 1.59489633 | 1.59489633 |
| Road | 4.08 × 20 (기존 메시 외곽) | 0.45484950 | 1.14155251 |
| Sidewalk | 0.6 × 10 (기존 직선 인도 모듈) | 0.08287293 | 0.46040516 |
| Tree | 수관 가시 폭 약 0.9 | 0.12640449 | 0.12640449 |

나무의 수관 폭은 alpha > 64의 약 712 px를 기준으로 계산했다. 투명 패딩까지 포함한 전체 Sprite 사각형은 약 1.585 × 1.585 단위다. 기존 Tree 시각 자식의 5.625 scale에 다시 곱하는 값이 아니다.

실제 배치 코드는 반올림값 대신 `targetWorldSize / sprite.bounds.size`로 계산하면 된다. 나무는 가시 수관 폭을 기준으로 동일 비율을 유지한다.

## 도로 연결 기준

- 세로 방향으로 이미지 위/아래 끝까지 도로와 선이 이어진다. 연결 방향 패딩, 끝막음, 주변 오브젝트가 없다.
- 모든 1752행에서 황색 중앙선의 기준 픽셀 범위는 x=441~455로 일정하다. 임계값은 R>180, G>150, B<100이다.
- 이 범위의 중심은 이미지 중심과 정확히 일치한다. 4.08단위 배치 시 중앙선 중심은 월드 x=0이다.
- 상단과 하단의 흰선 범위는 모두 x=11~25 및 x=872~886이다(R/G/B>210).
- 새 이미지의 중앙선/흰선 기준 폭은 약 0.0682단위, 흰선 중심은 약 x=±1.956단위다. 후속 Corner/T/Intersection 이미지가 만들어질 때 이 동일한 접속 단면을 사용해야 한다.
- 위 치수는 기존 메시의 외곽과 차선 중심을 유지하면서 새 그림의 마킹을 표준화한 것이다. 기존 메시 재질/마킹과 새 PNG를 혼용하는 최종 연결 검증은 별도다.
- 상단/하단 RGB가 완전히 같은 무손실 반복 텍스처는 아니다. RGB 채널 평균 차이는 약 1.05/255, 최대 차이는 20/255이다. **접속부 선 위치와 외곽 기하는 일치하며, 픽셀 색까지 완전히 동일하다고 보장하지 않는다.**

Road와 Sidewalk를 직접 붙이는 시각 검증 배치에서는 인도 중심 x=±(2.04+0.3)=±2.34를 사용한다. 오른쪽은 원본 왼쪽 연석, 왼쪽은 SpriteRenderer.flipX로 연석을 도로 쪽에 둔다. 이는 검증용 배치이며 기존 x=±5의 보행 경로나 슬롯을 변경하지 않는다. 실제 City 도입 때에는 인도 시각 범위와 보행 경로를 함께 검토해야 한다.

## 검증 기록

- 네 파일의 PNG 디코딩, 실제 해상도 및 색상 모드 확인.
- 도로 모든 행의 중앙선 위치와 상·하단 흰선 접속 위치 확인.
- 나무 alpha 범위 0~255 확인. 외곽 5% 영역에 생성 도구에서 남은 최대 alpha 1/255의 거의 투명한 픽셀이 일부 존재한다. 가시 오브젝트와 그림자는 충분히 안쪽에 위치한다.
- Unity 6000.3.23f1의 별도 임시 프로젝트에서 **61개 Import/크기/기하 접속 검증을 통과**했다. 실제 Single Sprite 로드, 원본 픽셀 크기, PPU, Full Rect, Center Pivot, Bilinear, 무압축, 월드 크기, 도로 두 개의 접속, 도로-인도의 맞닿는 경계를 확인했다.
- 검증 로그: `Temp/MapAssetRepresentativeReview/unity-verified.log`, 결과: `validation.txt`, Unity 렌더링: `unity-preview.png` (같은 디렉터리). 임시 프로젝트는 Git 제외 경로다.
- 임시 프로젝트의 기본 렌더러로 검증했다. 원본 프로젝트의 URP/WebGL 주행 테스트 및 기존 코너/교차로 메시와의 시각 혼용은 이 검증에 포함하지 않았다.
- Master Reference SHA-256은 생성 전후 동일: `E99B3C14ABBC68A851A3BE858862238F9987D8F9FF47D0B186BCFA48DF824701`.

현재 네 대표 이미지를 스타일·크기 검토 기준으로 남긴다. 나머지 Variant는 아직 만들지 않았으며, 기존 Road/City 게임플레이 Prefab에 적용하지 않았다.
