# CodeBlueRush 맵 리소스 아트 스타일 & 제작 가이드

> 기준 프로젝트: Unity 6000.3.23f1 / 2D / WebGL / 모바일 / 480×854
>
> 연동 문서:
>
> - `CodeBlueRush_CityRoadDesignGuide.md`
> - `CodeBlueRush_BuildingPlacementGuide.md`
> - `ScriptRules.md`
>
> 최우선 순위:
>
> **1순위 용량 제한 → 2순위 연산 최적화 → 3순위 구현 완료**
>
> 최종 웹사이트 배포 용량은 **절대로 100 MB를 초과하지 않는다.**
>
> 이 문서는 최종 도시 마스터 참고 이미지와 동일한 수준의 **시각적 완성도, 색감, 명암, 형태 언어, 탑뷰 가독성**을 유지하면서도 WebGL 모바일 용량/성능 제한을 지키기 위한 리소스 제작 기준을 정의한다.

---

# 1. 문서 우선순위

리소스 제작 중 기준이 충돌하면 다음 순서로 판단한다.

1. `CodeBlueRush_CityRoadDesignGuide.md`
   - 6개 구역 위치
   - 도로 성격
   - 차선 비율
   - 교차로
   - 진입로
   - 랜드마크
2. `CodeBlueRush_BuildingPlacementGuide.md`
   - 건물 종류
   - 건물 크기
   - 건물 밀도
   - 도로 이격 거리
   - 재사용 규칙
3. **최종 도시 마스터 참고 이미지**
   - 색감
   - 명암
   - 로우폴리 표현
   - 디테일 밀도
   - 전체 분위기
4. 실제 서울 도시 패턴
   - 아파트 단지
   - 상업 블록
   - 대로/골목
   - 병원 캠퍼스
   - 공원/하천
   - 산업/항만 구조

즉,

> **게임 기획이 1순위이며, 서울은 현실감을 보강하기 위한 2차 참고 자료다.**

---

# 2. 마스터 참고 이미지의 역할

최종 도시 이미지는 게임에 직접 사용하는 배경 이미지가 아니다.

용도:

- 모든 신규 맵 리소스의 **아트 방향 기준**
- 건물 색감 기준
- 도로 질감 기준
- 식생 밀도 기준
- 물/해안 표현 기준
- 저폴리 명암 기준
- 구역별 분위기 기준
- 리소스 제작 QA 비교 이미지

금지:

- 마스터 이미지를 그대로 거대한 1장 PNG로 게임에 사용
- 마스터 이미지를 여러 조각으로 잘라 전체 맵을 구성
- 마스터 이미지 해상도를 따라 개별 리소스를 과도하게 고해상도로 제작

목표는 **같은 체감 퀄리티**이지,
동일한 총 픽셀 수를 사용하는 것이 아니다.

---

# 3. 최종 아트 스타일 정의

CodeBlueRush의 최종 맵 아트 스타일은 다음과 같다.

> **밝고 선명한 서울형 캐주얼 로우폴리 2D 탑뷰 도시**

핵심 특징:

- 밝고 선명한 색 대비
- 지나치게 사실적이지 않은 단순화된 형태
- 면 단위로 나뉜 로우폴리 명암
- 모바일 화면에서 한눈에 읽히는 실루엣
- 현실 서울의 구조를 참고한 도시 배치
- 건물/도로/식생/차량이 동일한 미술 스타일
- 작은 화면에서도 구역 차이가 즉시 보이는 색과 형태

피해야 할 방향:

- 실사 사진 스타일
- 픽셀 아트
- 셀 셰이딩 외곽선 만화 스타일
- 수채화
- 과도한 손그림 질감
- 극도로 단순한 플랫 아이콘
- 사실적인 PBR 3D 렌더
- 어두운 사이버펑크 스타일

---

# 4. 카메라 / 시점 규칙

## 4.1 기본 시점

**Orthographic Top View**

카메라는 지면을 위에서 아래로 바라보는 탑뷰 기준을 유지한다.

금지:

- 아이소메트릭
- 3/4 시점
- 원근감이 강한 Perspective
- 건물 벽면이 과도하게 보이는 사선 시점
- 멀어질수록 오브젝트가 작아지는 원근 표현

---

## 4.2 입체감 표현

완전한 탑뷰이지만 마스터 이미지처럼 시각적 입체감을 허용한다.

방법:

- 지붕 면 분할
- 짧고 일정한 가장자리 음영
- 옥상 구조물
- 단순한 면 명암
- 작은 Baked Shadow

금지:

- 높은 건물 옆면을 크게 노출
- 카메라 방향에 따라 형태가 왜곡되는 표현
- 리소스마다 서로 다른 투시각

즉,

> **카메라는 탑뷰이고, 입체감은 Sprite 내부의 명암으로 표현한다.**

---

# 5. 조명 / 명암 규칙

모든 리소스는 동일한 광원 방향을 공유한다.

## 기본 광원

- 광원 방향: **좌측 상단**
- 밝은 면: 좌측 상단 계열
- 어두운 면: 우측 하단 계열
- 그림자 방향: **우측 하단**
- 그림자 길이: 짧게
- 그림자 경계: 너무 날카롭지 않게

## 명암 단계

기본적으로 3단계를 사용한다.

```text
Light
↓
Base
↓
Shade
```

필요한 경우 Highlight 1단계를 추가할 수 있다.

미세한 그라데이션보다 **넓은 면 단위 명암**을 우선한다.

---

# 6. 색상 방향

마스터 참고 이미지 기준으로 다음 계열을 유지한다.

| 요소 | 주요 색상 방향 |
| --- | --- |
| 아스팔트 | 중간 명도의 청회색 / 회색 |
| 콘크리트 | 밝은 회백색 / 베이지 |
| 잔디 | 선명한 중간 녹색 |
| 나무 | 진녹색 + 연녹색 면 분할 |
| 물 | 선명한 청색~청록색 |
| 병원 | 흰색 + 푸른 유리 + 빨간 의료 포인트 |
| 소방서 | 붉은색 + 회색 + 흰색 |
| 산업시설 | 회색 + 파랑 + 노랑/주황 포인트 |
| 주거 | 베이지 / 흰색 / 회색 + 지붕 색 변화 |
| 상업 | 흰색 / 회색 기반 + 제한된 강한 간판색 |

대표적인 마스터 이미지 색감 참고 범위:

- 짙은 녹색 계열: `#487615`
- 연한 녹색 계열: `#93B448`
- 중간 녹색 계열: `#78A442`
- 밝은 물 계열: `#0877BD`
- 깊은 물 계열: `#0B5E8B`
- 도로 청회색 계열: `#646D7B`
- 밝은 회백색 계열: `#DCD9C6`
- 따뜻한 콘크리트 계열: `#C4AF9C`

위 색상은 **절대 고정 팔레트가 아니라 마스터 이미지와의 톤 일치를 위한 기준 범위**다.

---

# 7. 디테일 밀도 규칙

같은 퀄리티를 만들기 위해 모든 리소스를 복잡하게 만들 필요는 없다.

우선순위:

1. 실루엣
2. 큰 색 면
3. 로우폴리 명암
4. 기능을 나타내는 특징
5. 중간 크기 디테일
6. 작은 장식

작은 화면에서 보이지 않는 디테일은 제거한다.

예:

병원에 필요한 것:

- 큰 흰색 본관
- 푸른 창문 영역
- 빨간 의료 표시
- 응급실 입구
- 옥상 구조물

불필요:

- 모든 창문 내부 표현
- 실내 가구
- 작은 간판 글자
- 실제 건축 도면 수준의 구조

---

# 8. 외곽선 규칙

검은 만화식 OutLine은 기본적으로 사용하지 않는다.

오브젝트 분리는 다음으로 해결한다.

- 색 대비
- 명암
- 짧은 Baked Shadow
- 주변 바닥과의 명도 차이

필요한 경우 매우 얇은 어두운 가장자리만 허용한다.

---

# 9. 건물 제작 규칙

`CodeBlueRush_BuildingPlacementGuide.md`의 크기/종류를 그대로 따른다.

## Small

예:

- 주택
- 작은 상점
- 관리소
- 매점

권장 해상도:

**128~256 px**

## Medium

예:

- 빌라
- 중형 상가
- 창고
- 의료 보조동

권장 해상도:

**256 px 중심**

## Large

예:

- 아파트
- 오피스
- 공장
- 대형 상가

권장 해상도:

**256~512 px**

## Landmark

예:

- 대형 아파트 단지
- 중앙 광장 + 대형 상가
- 종합병원
- 컨테이너 크레인
- 호수 중앙 섬/정자
- 소방서 본관 + 타워

권장 해상도:

**512 px 중심**

1024 px는 실제 게임 화면에서 512 px로 품질이 부족한 경우에만 허용한다.

---

# 10. 건물 형태 규칙

건물은 복잡한 현실 건축물을 그대로 복제하지 않는다.

기본 구성:

```text
큰 건물 실루엣
+
지붕 면 분할
+
옥상 설비 1~3개
+
창문/유리 영역
+
기능 포인트
+
짧은 Baked Shadow
```

건물 하나에 너무 많은 작은 요소를 넣지 않는다.

서울 느낌은 작은 장식이 아니라 다음 요소로 만든다.

- 아파트 동 배치
- 상가 밀집도
- 주차장
- 도로와 건물 거리
- 옥상 설비
- 빌라/상가 형태
- 녹지와 차량 배치

---

# 11. 건물 반복 사용 규칙

새 이미지를 생성하기 전에 기존 리소스 재사용 가능 여부를 먼저 확인한다.

다양성 우선순위:

```text
1. 위치 변경
2. 90° 회전
3. 좌우 반전
4. Color Tint
5. 간판 Overlay
6. 주변 소품 변경
7. 그래도 부족할 때만 신규 Sprite
```

상업지역 예:

```text
SmallShop_Base
+
Convenience_Sign
Pharmacy_Sign
Cafe_Sign
Restaurant_Sign
```

동일한 기능을 위해 완전히 새로운 건물 본체를 계속 생성하지 않는다.

---

# 12. 도로 제작 규칙

`CodeBlueRush_CityRoadDesignGuide.md`와 완전히 동일하게 제작한다.

최소 기본 세트:

- Road_Straight_2Lane
- Road_Straight_4Lane
- Road_Corner
- Road_TJunction
- Road_Intersection_4Way
- Road_Bridge
- Road_Entrance
- Road_Asphalt_Base
- RoadMarking_Line
- RoadMarking_Arrow
- Crosswalk

방향별 이미지 별도 제작 금지.

예:

```text
Road_Corner_NE
Road_Corner_NW
Road_Corner_SE
Road_Corner_SW
```

4장을 만들지 않고,

```text
Road_Corner
```

1장을 회전 사용한다.

---

# 13. 도로 시각 스타일

마스터 이미지처럼 다음 특징을 가진다.

- 깨끗한 청회색 아스팔트
- 흰색/노란색 차선 명확
- 횡단보도 높은 가독성
- 인도와 도로 경계 분명
- 교차로가 멀리서도 인식됨
- 실제 서울형 대로처럼 중앙선/차선이 정돈됨

과도한 요소 금지:

- 심한 아스팔트 균열
- 실사 노면 텍스처
- 작은 자갈 표현
- 과도한 오염
- 읽기 어려운 도로 문자

---

# 14. 인도 / 보도 제작 규칙

표현:

- 밝은 회색 / 베이지
- 단순한 블록 패턴
- 일정한 연석
- 도로보다 밝게

패턴을 너무 작게 만들지 않는다.

하나의 기본 타일을 반복 사용하고
코너와 특수 진입부만 추가한다.

---

# 15. 물 / 하천 제작 규칙

마스터 이미지처럼 물은 도시에서 강한 색 대비 요소다.

기본:

- 청색~청록색
- 중앙은 조금 진하게
- 가장자리는 조금 밝게
- 작은 하이라이트 허용

금지:

- 실사 물 사진
- 복잡한 반사
- 실시간 Reflection
- 물 타일마다 개별 애니메이션

하천은 다음 최소 세트로 조합한다.

- Water_Base
- Water_Edge
- Water_Corner
- Rock_Edge

---

# 16. 해안 / 바위 제작 규칙

해안은 다음 구조를 권장한다.

```text
물
↓
밝은 물 가장자리
↓
회색 바위
↓
잔디 / 지면
```

바위는 단순한 로우폴리 면 분할을 사용한다.

개별 바위 종류를 과도하게 늘리지 않는다.

Small / Medium / Large 3종 정도를 기본으로 하고
회전/크기 변경으로 재사용한다.

---

# 17. 식생 제작 규칙

기본 리소스:

- Tree_Small
- Tree_Medium
- Tree_Large
- Bush
- Hedge
- Flower
- FlowerBed

나무 표현:

- 둥근 하나의 덩어리보다 여러 단순 면 덩어리
- 진녹색 / 중간 녹색 / 연녹색 3단 명암
- 짧은 Baked Shadow
- 탑뷰 실루엣 우선

동일 Tree를 다음으로 변형한다.

- 회전
- Scale
- Tint

나무 종류를 계속 추가하지 않는다.

---

# 18. 벚꽃 사용 규칙

서울형 봄 분위기를 위해 일부 벚꽃 계열 나무를 사용할 수 있다.

사용 위치:

- 주거 아파트 단지
- 공원
- 병원 녹지
- 상업 광장 일부

과도한 사용 금지.

도시 전체가 분홍색으로 보이지 않도록 일반 녹색 나무가 주가 된다.

---

# 19. 산업 / 항만 리소스 규칙

기본 세트:

- Warehouse
- Factory
- LogisticsCenter
- Container
- Tank
- Silo
- ContainerCrane
- Dock

산업지역은 색을 지나치게 화려하게 하지 않는다.

기본:

- 회색
- 푸른색
- 흰색

포인트:

- 노랑
- 주황
- 빨강

컨테이너는 여러 장의 색상 PNG 대신
기본 Sprite + Tint 사용을 우선한다.

---

# 20. 병원 리소스 규칙

병원은 다른 건물과 즉시 구분되어야 한다.

필수 시각 특징:

- 큰 흰색 본체
- 푸른 유리 영역
- 빨간 의료 포인트
- 응급실 입구
- 주차장
- 녹지
- 차량 회전 공간

글자를 크게 넣기보다 **형태와 색상**으로 병원임을 표현한다.

---

# 21. 소방서 리소스 규칙

필수 특징:

- 빨간색 / 회색 본관
- 여러 차량 차고 문
- 넓은 전면 출동 공간
- 훈련/통신 타워
- 구급차/소방차 주차 영역

소방서 앞은 시각적으로 복잡하게 만들지 않는다.

게임 시작 지점이므로 주행 방향이 즉시 보여야 한다.

---

# 22. 차량 제작 규칙

차량 역시 같은 로우폴리 탑뷰 스타일을 사용한다.

기본:

- 명확한 실루엣
- 지붕/보닛/유리 면 분할
- 짧은 그림자
- 복잡한 반사 금지

일반 차량은 소수 기본 차종을 제작하고 Tint로 색상을 변경한다.

예:

- Sedan
- SUV
- Van
- Truck
- Bus

---

# 23. 시민 제작 규칙

시민은 모바일 화면에서 매우 작게 보인다.

따라서 세부 얼굴 표현보다 실루엣을 우선한다.

- 머리
- 몸통
- 팔/다리
- 옷 색상

소수 기본 Sprite를 색상 Variation으로 재사용한다.

고해상도 캐릭터 Sprite를 사용하지 않는다.

---

# 24. 소품 제작 규칙

기본 교통/환경 소품:

- TrafficLight
- StreetLight
- RoadSign
- Fence
- TrafficIsland
- ParkingLine
- ParkingBarrier
- Bench
- TrashBin
- Hydrant
- BusStop

소품은 64~128 px 중심으로 제작한다.

카메라에서 실제로 구분되지 않는 세부 구조는 제거한다.

---

# 25. 투명 PNG 규칙

개별 독립 리소스는 기본적으로:

- PNG
- Transparent Background
- 오브젝트 전체가 잘리지 않음
- 중앙 정렬
- 가장자리 최소 여백
- 불필요한 큰 투명 Canvas 금지

단,

다음은 Alpha가 필요 없는 불투명 타일 사용을 우선한다.

- Ground
- Road Base
- Water Base

---

# 26. Sprite Pivot 규칙

일반 독립 리소스:

- Pivot: Center

건물/랜드마크:

- 기본 Center
- 특별한 배치 기준이 필요한 경우 Bottom Center가 아니라
  **월드 배치에 일관된 전용 Pivot**을 사용

동일 카테고리에서 Pivot 규칙을 섞지 않는다.

---

# 27. Sprite Atlas 규칙

기존 최적화 문서와 동일하게 유지한다.

권장:

- GroundAtlas
- RoadAtlas
- RoadMarkingAtlas
- SidewalkAtlas
- BuildingCommonAtlas
- BuildingLandmarkAtlas
- VegetationAtlas
- PropAtlas
- VehicleAtlas
- CitizenAtlas

Atlas:

- 1024~2048 우선
- 사용하지 않는 Sprite 제외
- 동일 Sprite 중복 포함 금지

---

# 28. 금지 텍스처

다음은 기본적으로 제작하지 않는다.

- Normal Map
- Height Map
- AO Map
- Metallic Map
- Roughness Map
- 건물별 Shadow Map
- 건물별 Emission Map

필요한 명암은 Color Sprite에 Bake한다.

---

# 29. 이미지 생성 기본 프롬프트 구조

새 리소스를 이미지 생성할 때 다음 순서를 유지한다.

```text
[1] 게임/엔진
Unity 2D / CodeBlueRush / 모바일 WebGL

[2] 시점
strict orthographic top-down view

[3] 대상
정확한 리소스 이름과 기능

[4] 디자인
bright colorful Seoul-inspired casual low-poly city style

[5] 형태
simple clear silhouette, simplified planar geometry

[6] 명암
light from upper-left, short shadow to lower-right,
3-step low-poly planar shading

[7] 색상
match CodeBlueRush master city reference

[8] 배경
transparent PNG

[9] 금지
no isometric, no perspective, no realistic photo texture,
no text unless specifically required, no cropped object

[10] 크기
target production size according to this guide
```

---

# 30. 이미지 생성 공통 프롬프트 예시

```text
CodeBlueRush Unity 2D map asset.

Create [ASSET NAME] for a mobile WebGL game.

Strict orthographic top-down view.
No isometric camera, no perspective tilt.

Match the CodeBlueRush master city reference:
bright colorful Seoul-inspired casual low-poly style,
clean simplified geometry,
clear silhouette,
3-step planar shading,
consistent upper-left lighting,
short baked shadow toward lower-right.

The asset must look like it belongs to the same city as
all other CodeBlueRush roads, buildings, vegetation and vehicles.

Transparent background.
Object fully visible and centered.
Minimal transparent padding.
No text unless specifically requested.
No photorealistic texture.
No overly fine details.
No separate normal-map style lighting.
```

---

# 31. 생성 시 반드시 함께 제공할 참고 자료

이미지 생성 요청에는 가능한 경우 다음을 함께 사용한다.

1. 최종 도시 마스터 참고 이미지
2. 같은 카테고리의 이미 승인된 CodeBlueRush 리소스
3. 필요할 경우 실제 서울 참고 이미지

중요:

실제 서울 이미지는 **구조/패턴 참고**다.

최종 아트 스타일은 항상 CodeBlueRush 마스터 이미지에 맞춘다.

---

# 32. 생성 결과 QA — 시점

다음 중 하나라도 실패하면 재생성한다.

- 탑뷰인가
- 아이소메트릭이 아닌가
- 원근 왜곡이 없는가
- 오브젝트 방향이 Unity 맵과 호환되는가
- 한쪽 벽면이 과도하게 보이지 않는가

---

# 33. 생성 결과 QA — 스타일

다음이 모두 맞아야 한다.

- 마스터 이미지와 밝기가 비슷함
- 색 대비가 선명함
- 로우폴리 면 분할이 있음
- 실사 질감이 없음
- 검은 외곽선이 과하지 않음
- 다른 리소스와 같은 광원 방향
- 같은 그림자 방향

---

# 34. 생성 결과 QA — 기능

도로:

- 차선 수 정확
- 차선 폭 일관
- 연결부 정확
- 회전 재사용 가능

건물:

- Small / Medium / Large 규격에 맞음
- Collider를 단순하게 잡을 수 있는 실루엣
- 구역 성격과 일치

랜드마크:

- 멀리서도 구분 가능
- 일반 건물보다 시각적 특징이 강함

식생/소품:

- 작은 화면에서도 형태 구분 가능
- 과도한 세부묘사 없음

---

# 35. 생성 결과 QA — 최적화

리소스 승인 전 확인한다.

- 필요한 해상도 이상으로 크지 않은가
- 기존 Sprite로 대체할 수 없는가
- 회전/반전/Tint로 대체할 수 없는가
- 투명 여백이 크지 않은가
- Atlas에 포함 가능한가
- Alpha가 정말 필요한가
- Normal/Mask Map이 추가되지 않았는가
- 같은 기능의 중복 Texture가 없는가

---

# 36. 리소스 품질 판단 기준

고해상도라고 높은 품질이 아니다.

CodeBlueRush에서 높은 품질은 다음을 의미한다.

```text
일관된 탑뷰
+
명확한 실루엣
+
통일된 색감
+
통일된 명암
+
구역 성격 전달
+
모바일 화면 가독성
+
높은 리소스 재사용률
```

마스터 이미지 수준의 체감 완성도는
**많은 Texture가 아니라 일관성**으로 만든다.

---

# 37. 제작 순서

최종 리소스 제작은 다음 순서를 권장한다.

## 1단계 — 기반

- Ground
- Water
- Coast
- Rock

## 2단계 — 도로

- Road Base
- Road Marking
- Intersection
- Corner
- Bridge
- Sidewalk

## 3단계 — 건물

- 주거
- 상업
- 병원
- 산업
- 공원 시설
- 소방서

## 4단계 — 랜드마크

- 대형 아파트 단지
- 중앙 광장 + 대형 상가
- 대형 종합병원
- 컨테이너 크레인
- 호수 중앙 섬 + 정자
- 소방서 본관 + 타워

## 5단계 — 식생

- Trees
- Bush
- Hedge
- Flower

## 6단계 — 교통/환경 소품

- Traffic Light
- Street Light
- Sign
- Fence
- Parking
- Bus Stop

## 7단계 — 동적 오브젝트

- 일반 차량
- 시민
- 필요한 이펙트

각 단계가 끝나면 리소스 전체를 만든 뒤
해당 파트만 Unity에 적용하고 테스트한다.

---

# 38. 단계별 검증 흐름

모든 리소스 제작 파트는 다음 흐름을 사용한다.

```text
해당 파트 리소스 전체 제작
↓
스타일 QA
↓
중복/해상도/용량 검사
↓
Unity Import
↓
Atlas / Compression 설정
↓
해당 파트 전체 배치
↓
해당 파트 테스트
↓
문제 수정
↓
해당 파트만 재테스트
↓
Release WebGL Build 용량 확인
↓
정상 통과 시 종료
```

관련 없는 전체 시스템 회귀 테스트는 매번 하지 않는다.

공용 핵심 구조 변경이나 전체 단계 완료 시에만 필요한 범위의 통합 테스트를 수행한다.

---

# 39. 용량 Gate

최종 웹사이트:

- 목표: 85 MB 이하
- 경고: 90 MB 이상
- 재최적화 필수: 95 MB 이상
- 100 MB 초과: 절대 실패

리소스 품질과 용량이 충돌하면 다음 순서로 조정한다.

```text
1. 투명 여백 제거
2. Max Size 감소
3. 압축 강화
4. 중복 Sprite 통합
5. Tint / Overlay 재사용
6. 불필요한 Variation 삭제
7. 작은 장식 삭제
8. 그래도 부족하면 리소스 종류 축소
```

랜드마크와 게임플레이 가독성은 마지막까지 유지한다.

---

# 40. 최종 금지 사항

다음 결과는 승인하지 않는다.

- 아이소메트릭 시점
- 기울어진 건물
- 실사 스타일
- 다른 리소스와 광원 방향 불일치
- 리소스마다 다른 색감
- 과도한 검은 외곽선
- 지나친 미세 디테일
- 작은 화면에서 알아볼 수 없는 구조
- 동일 리소스의 방향별 중복 PNG
- 단순 색상 차이만 있는 중복 PNG
- 불필요한 1024/2048/4096 이미지
- 거대한 전체 도시 배경 PNG
- Normal/Mask/AO 텍스처 남용
- 잘린 오브젝트
- 큰 투명 여백
- 구역 기획과 맞지 않는 건물/도로
- 100 MB 제한을 위협하는 리소스 추가

---

# 41. 최종 승인 조건

신규 리소스는 다음 항목이 모두 `PASS`일 때만 최종 승인한다.

- [ ] `CodeBlueRush_CityRoadDesignGuide.md`와 충돌 없음
- [ ] `CodeBlueRush_BuildingPlacementGuide.md`와 충돌 없음
- [ ] 마스터 도시 이미지와 동일한 아트 방향
- [ ] 엄격한 Orthographic Top View
- [ ] 동일한 상단 좌측 광원
- [ ] 동일한 우측 하단 그림자 방향
- [ ] 밝고 선명한 캐주얼 로우폴리 색감
- [ ] 모바일 화면에서 실루엣이 명확함
- [ ] 필요한 최소 해상도 사용
- [ ] 재사용 가능 여부 확인 완료
- [ ] Atlas 적용 가능
- [ ] Web 압축 가능
- [ ] 불필요한 추가 Map 없음
- [ ] Build Size 예산 내
- [ ] 최종 웹사이트 100 MB 이하 유지 가능

---

# 42. 최종 제작 원칙

CodeBlueRush의 이미지 리소스는 다음 문장으로 정의한다.

> **서울의 실제 도시 패턴을 참고하되, 확정된 6개 구역 기획을 최우선으로 유지하고, 최종 도시 마스터 이미지와 동일한 밝고 선명한 캐주얼 로우폴리 탑뷰 스타일로 제작한다.**

그리고 모든 판단은 다음 우선순위를 절대 변경하지 않는다.

```text
1순위: 최종 웹사이트 100 MB 제한
↓
2순위: 모바일 WebGL 연산 최적화
↓
3순위: 기획한 도시와 리소스 구현 완료
```

시각적 퀄리티는 **고해상도/다량 리소스가 아니라 통일된 디자인과 재사용 구조**로 유지한다.
