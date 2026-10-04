# NewGamePlay 나무·하천 주변 돌 추가 및 배치

후속 작업에서 도시 녹지와 Maps.png 비교·명암을 보강했다. **현재 Scene 검증과 화면은 [최신 비교 및 그래픽 보강 기록](../NewGamePlayVisualPolish/README.md)을 참고한다.** 아래 수량과 화면은 첫 나무·돌 배치 단계의 기록이다.

최신 main `34575d290f756faf95a6aee69a129437fd276579`의 산책로 배치 위에 작업했다. 원본 `Assets/Images/Maps/Reference/Maps.png`를 디자인과 위치의 기준으로 사용했다. 이번 요청은 새 나무·돌 리소스 제작을 허용하므로 이전 단계의 새 이미지 금지와 리소스 부족 상태를 대체한다.

## 리소스

Built-in image_gen으로 나무 3종(Tree_Broadleaf, Tree_Conical, Tree_Flower)과 돌 3종(RiverRock_Small, RiverRock_Medium, RiverRock_Cluster)을 독립 투명 Sprite로 제작했다. 높은 탑뷰·단순한 면 명암·좌측 상단 광원·짧은 줄기·연두/진녹색 수관·분홍 꽃·회색 돌을 기준 이미지에 맞췄다. 전체 프롬프트와 나무 후속 수정 프롬프트는 [AssetPrompts.json](AssetPrompts.json)에 있다.

최종 파일은 `Assets/Images/Maps/Nature`에 저장했다. 나무 및 돌 군집은 최대 128px, 단일 돌은 64px이며 PNG 합계 87,456 bytes. 생성 알파를 유지하고 투명 여백 정리와 축소만 수행했다. 투명 픽셀의 RGB 잔광은 잔디 위 합성에서 보이지 않음을 확인했다. Normal/Mask/AO와 방향·색상별 복제 Texture는 없다.

`Assets/Prefabs/MapAssets/Nature`의 Prefab 6개는 Transform/SpriteRenderer만 포함한다. 기존 RoadSurface.mat를 공유하며 Collider와 MonoBehaviour는 없다. Default Sorting Layer에서 돌 -8, 나무 -5로 기존 포장·산책로 위, 건물·기본 차량 정렬 아래에 둔다. Import는 Sprite Single, PPU 64, Bilinear, Clamp, Mipmap/ReadWrite Off, Alpha Is Transparency On, WebGL ETC2 RGBA8이다. NatureAtlas는 최대 512px, 6개 Sprite를 각 1회 포함한다. 기존 Atlas는 변경하지 않았다.

## 배치

나무 724개: 녹색 넓은 수관 346개, 뾰족한 수관 312개, 꽃나무 66개. 강변 돌 128개: 작은 돌 64개, 중형 돌 47개, 세 돌 군집 17개(실제 돌 덩어리는 총 162개).

Maps.png의 녹색 수관·분홍 꽃·하천 인접 회색 돌 위치를 검출하고 현재 Scene의 보호 영역을 피해 같은 구역의 가까운 녹지로 보정했다. 기준 좌표는 `x=(px-470.5)/8, y=(836-py)/8`이다. 나무 이동 거리 중앙값은 기준 이미지 6px이며, 일반 구역은 최대 16px, 좁은 외곽은 최대 34px 보정했다. 위치·표시 크기·종류·원본 대응 좌표는 [SourcePlacements.json](SourcePlacements.json)에 있다. 오프라인 배치 결과만 Scene에 저장하며 런타임 생성이나 무작위 시스템은 없다.

북쪽 166, 중앙 강변 192, 남서 산업/항만 11, 남동 204, 외곽 141, 소규모 녹지 10개의 나무를 배치했다. 상세 구역 역할은 [RegionPlan.json](RegionPlan.json)에 기록했다. 정자 2개와 분수 3개는 기존 상태를 유지했다. 각 지역의 공간에 맞는 작은/중형 표시 크기를 사용하고 그 안에서 Scale 차이는 0.92~1.08이다. 모든 수관의 광원 방향을 유지했다.

기존 `City/StaticDecoration/Nature` 아래 6개 구역으로 정리했다. 장식 Sprite 852개와 정리용 Empty 7개만 추가했다. 강변 돌 군집 하나가 세 돌을 표현해 별도 GameObject 증가를 줄였다. 외곽 상단·측면은 원본보다 좁은 기존 녹지 폭 때문에 작은 나무 한 줄에 가깝게 표현한 구간이 있으며, 남쪽과 공원은 불규칙 군집을 사용했다. 기존 도로·보도·하천·건물·다리·주차장·병원 접근로·소방서 야드·산책로·Camera는 변경하지 않았다.

## 검증

- [LayoutQA.json](LayoutQA.json): 실제 Sprite 알파 윤곽(8/255 초과)을 기준 픽셀당 2회 샘플링. 나무의 도로·보도·산책로·건물·주차장·수면 침범 0, 돌의 보호 영역 및 나무 침범 0. 돌은 물 경계에 부분적으로 걸치되 수면 비율을 8~45%로 제한했다. 기존 Scene 직렬화 블록 중 StaticDecoration 자식 목록만 변경했다.
- [UnityQA.json](UnityQA.json): 프로젝트를 복사한 `Temp/B`에서 실제 Unity 로드. 나무 724/돌 128 표시, Missing Sprite/Prefab/Script 0, 환경 Collider/Behaviour 0, Console Error 0, 기존 Collider 79개 유지. 도로·건물·하천·산책로·Camera를 배치 전 Scene과 실제 Unity 객체 기준으로 비교했다. 검증용 C#은 원본 Assets에 추가하지 않았다.
- [GameView480x854.png](GameView480x854.png): 고정 480×854 Game View 설정 및 기존 Camera 실제 렌더. 941×1672 렌더도 보관했다.
- [BuildQA.json](BuildQA.json), [BrowserQA.json](BrowserQA.json), [WebGL480x854.png](WebGL480x854.png): 해당 Scene의 WebGL Release 빌드와 Edge WebGL2 모바일 viewport 검증. 실제 모바일 기기의 성능/주행 검사는 수행하지 않았다.
- [Maps_vs_NewGamePlay_480x854.png](Maps_vs_NewGamePlay_480x854.png): 원본과 실제 Unity 화면 비교. 외곽·공원·하천의 수목 및 회색 돌 경계가 추가되어 녹지 밀도와 구역 차이가 개선됐다. 확정된 도로 폭·건물 크기·하천 지형 차이 때문에 모든 위치와 크기를 픽셀 단위로 동일하게 만들지는 않았다.

추가 나무·돌 리소스는 필요하지 않다. 이번 요청 범위 밖의 별도 수풀·화단·새 공원길 이미지는 추가하지 않았다. Commit/Push/Merge는 수행하지 않았다.
