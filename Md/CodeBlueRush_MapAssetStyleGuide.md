# CodeBlueRush 맵 리소스 아트 스타일 & 제작 가이드 — Maps.png 최우선 개정판

> 기준 프로젝트: Unity 6000.3.23f1 / 2D / WebGL / 모바일 / 480×854
>
> 최우선 참고 이미지: `Assets/Image/Maps/Reference/Maps.png`
>
> 연동 문서:
>
> - `CodeBlueRush_CityRoadDesignGuide.md`
> - `CodeBlueRush_BuildingPlacementGuide.md`
> - `ScriptRules.md` — 런타임 코드 작성 시에만 적용
>
> **아트/배치 우선순위: Maps.png → 도로/건물 가이드와의 연결 → 모바일 가독성 → 재사용/최적화**
>
> 최종 웹사이트 배포 용량은 100 MB를 초과하지 않는다. 단, 용량 제한은 Maps.png의 핵심 도시 구조를 바꾸는 이유로 사용하지 않는다.

---

# 1. 문서 목적

이 문서는 `Maps.png`와 같은 도시로 보이도록 모든 맵 리소스의 시점, 색감, 명암, 형태, 재질 표현, 제작 방법을 통일하는 기준이다.

이 문서의 목표:

- Maps.png와 같은 체감 스타일
- 리소스끼리 서로 다른 게임처럼 보이지 않음
- 모바일 화면에서 즉시 읽히는 형태
- 같은 도로/건물/나무를 효율적으로 재사용
- WebGL 용량과 성능 제한 유지

---

# 2. 최우선 기준

리소스 제작 중 충돌하면 다음 순서를 사용한다.

```text
1. Maps.png의 실제 시각 특징
↓
2. Maps.png의 도로/블록/랜드마크 위치 관계
↓
3. CityRoadDesignGuide의 도로·보도 구현 규칙
↓
4. BuildingPlacementGuide의 건물·랜드마크 배치 규칙
↓
5. 최적화
```

중요:

- Maps.png가 아트 방향의 Source of Truth다.
- “서울형” 같은 별도 컨셉은 보조 참고일 뿐 Maps.png보다 우선하지 않는다.
- 실제 도시 자료를 참고하더라도 Maps.png와 다른 도로 구조나 건물 형태를 강제로 적용하지 않는다.

---

# 3. Maps.png의 핵심 Visual DNA

Maps.png의 스타일은 다음처럼 정의한다.

> **밝고 선명하며 장난감 같은 질감의 캐주얼 로우폴리 2D 도시, 높은 각도의 탑뷰와 얕은 입체감, 조밀한 녹지와 선명한 물 색상**

핵심 특징:

- 높은 채도지만 지나치게 네온색은 아님
- 도로는 깨끗하고 정돈됨
- 건물은 흰색/회색/파랑 계열이 많음
- 지붕과 포인트 색은 빨강/파랑/주황 등으로 구분
- 나무는 매우 조밀하며 2~3단 면 명암이 보임
- 벚꽃/분홍 꽃나무가 포인트로 사용됨
- 물은 강한 파랑~청록색으로 도시와 대비됨
- 하천과 해안에 회색 바위 경계가 많음
- 건물과 소품은 작은 장난감 모형처럼 정리된 형태
- 실사 노이즈와 거친 텍스처가 거의 없음

---

# 4. 시점 규칙

## 4.1 Maps.png는 완전한 90° 평면 탑뷰가 아니다

Maps.png의 도로와 지면은 거의 정사영 탑뷰처럼 읽히지만, 건물에는 얕은 측면과 높이감이 보인다.

따라서 최종 기준은 다음과 같다.

```text
도로 / 지면 / 보도
→ 탑뷰 가독성 우선

건물 / 랜드마크 / 차량
→ 높은 각도의 Near-Orthographic Top View
→ 얕은 측면 노출 허용
→ 강한 원근 왜곡 금지
```

---

## 4.2 허용

- 지붕과 얕은 벽면이 동시에 보이는 건물
- 살짝 보이는 차량 측면
- 건물 높이를 느낄 수 있는 짧은 수직 면
- 카메라가 매우 높은 위치에서 내려다보는 느낌

---

## 4.3 금지

- 일반적인 아이소메트릭 45° 시점
- 멀리 있는 물체가 크게 작아지는 Perspective
- 건물마다 서로 다른 기울기
- 한 건물에서 벽면이 지붕보다 더 크게 보이는 시점
- 도로가 사다리꼴로 심하게 왜곡되는 표현

---

# 5. 입체감 표현

Maps.png처럼 입체감은 단순한 면과 그림자로 만든다.

기본 구성:

```text
Top Plane
+ 얕은 Side Plane
+ 2~3단 명암
+ 짧은 Baked Shadow
```

과도한 3D 재질 표현보다 실루엣과 큰 색 면을 우선한다.

---

# 6. 광원과 그림자

모든 리소스는 같은 광원 방향을 사용한다.

기본:

- 광원: 좌측 상단 계열
- 밝은 면: 좌측/상단
- 어두운 면: 우측/하단
- 그림자: 우측 하단
- 그림자 길이: 짧음
- 그림자 경계: 너무 날카롭지 않음

명암 단계:

```text
Highlight 선택
↓
Light
↓
Base
↓
Shade
```

미세한 실사형 그라데이션보다 넓은 면 단위 명암을 사용한다.

---

# 7. Maps.png 기준 색상 방향

아래 색은 고정 팔레트가 아니라 Maps.png의 톤을 맞추기 위한 범위다.

| 요소 | 방향 |
| --- | --- |
| 아스팔트 | 중간~어두운 청회색 |
| Sidewalk | 밝은 회백색 / 베이지 |
| 잔디 | 선명한 중간 녹색 |
| 나무 | 진녹색 + 중간 녹색 + 연녹색 |
| 물 | 진한 파랑 + 밝은 청록/하늘색 |
| 바위 | 밝은 회색~중간 회색 |
| 병원 | 흰색 + 푸른 유리 + 빨간 의료 포인트 |
| 소방서 | 빨강 + 흰색 + 회색 |
| 산업시설 | 회색 + 파랑 + 노랑/주황 포인트 |
| 주거 | 흰색/회색 벽 + 빨강/파랑/회색 지붕 |
| 업무/상업 | 흰색/회색 + 푸른 유리 |

Maps.png에서 추출되는 대표 범위 예시:

- 도로 회색: `#62646C` 계열
- 깊은 물: `#0657AA` 계열
- 밝은 물: `#0A70CB` ~ `#0A94E0`
- 짙은 녹지: `#315814` ~ `#546A17`
- 중간 녹지: `#689217` ~ `#95A61A`
- 밝은 보도/건물: `#DFD7D9` 계열
- 따뜻한 보도/경로: `#D0B09F` ~ `#E6C9A1`

실제 최종 색상은 Maps.png를 옆에 두고 눈으로 비교해 조정한다.

---

# 8. 디테일 밀도

우선순위:

```text
1. 큰 실루엣
2. 큰 색 면
3. 기능을 나타내는 특징
4. 로우폴리 면 명암
5. 중간 크기 디테일
6. 작은 장식
```

모바일 화면에서 거의 보이지 않는 디테일은 생략한다.

Maps.png의 풍부함은 작은 텍스처 노이즈보다 **나무, 건물, 주차장, 광장, 물, 바위의 조합 밀도**에서 나온다.

---

# 9. 외곽선

검은 만화식 Outline은 기본적으로 사용하지 않는다.

오브젝트 분리는 다음으로 만든다.

- 명도 차이
- 색 대비
- 면 명암
- 짧은 그림자
- 주변 바닥과의 색 차이

필요하면 아주 얇은 어두운 가장자리만 사용한다.

---

# 10. 도로 스타일

Maps.png 도로의 핵심:

- 깨끗한 회색 아스팔트
- 노면 노이즈가 적음
- 노란색 중앙선이 명확함
- 흰색 횡단보도가 강하게 보임
- Sidewalk와 아스팔트 경계가 명확함
- 교차로가 멀리서도 쉽게 읽힘

금지:

- 심한 균열
- 실사 아스팔트 사진
- 지나친 먼지/오염
- 읽기 어려운 작은 도로 문자
- 과한 반사

---

# 11. 차선 표시

기본 도로는 Maps.png처럼 양방향 도로로 읽히게 한다.

권장:

- 중앙: 노란색 선 또는 짧은 Dash
- 접근부/특수 구간: 필요한 흰색 표시
- Crosswalk: 밝은 흰색 Zebra

차선 표시를 Road Base에 모두 Bake하지 않고 Overlay로 분리할 수 있다.

---

# 12. Sidewalk 스타일

Maps.png Sidewalk는 도로보다 밝고 깨끗하다.

특징:

- 밝은 회색/베이지
- 단순한 블록 또는 판석 느낌
- 일정한 연석
- 도로보다 높은 명도
- 교차로 코너에서도 연속적

필수 타일:

- `Sidewalk_Straight`
- `Sidewalk_CornerInner`
- `Sidewalk_CornerOuter`
- `Sidewalk_TJunctionEdge`
- `Sidewalk_Driveway`
- `Sidewalk_BridgeEdge`

패턴을 너무 작게 만들지 않는다.

---

# 13. Crosswalk 스타일

- 흰색 Zebra 패턴
- 차량/보도 대비가 강함
- 폭과 Stripe 간격 통일
- Sidewalk와 직선으로 연결
- 교차로마다 크기가 크게 달라지지 않음

Maps.png에 없는 방향으로 자동 추가하지 않는다.

---

# 14. 중앙 녹지대 / 교통섬

Maps.png에는 도로 사이 또는 큰 블록 경계에 녹지 Strip이 반복된다.

표현:

- 밝은 잔디
- 작은 나무 반복
- 관목
- 필요 시 작은 꽃

도로와 한 장으로 합치기보다 별도 Sprite/Tile로 만든다.

---

# 15. 하천 스타일

Maps.png 하천은 도시의 핵심 색상 대비다.

특징:

- 강한 파랑
- 중앙부는 조금 깊은 색
- 가장자리에는 밝은 물색
- 바위가 물 경계를 따라 조밀하게 배치
- 주변은 잔디와 나무가 많음
- 하천 폭이 일정하지 않고 자연스럽게 굽음

기본 세트:

- `Water_Base`
- `Water_Edge`
- `Water_Corner`
- `Water_ShallowEdge`
- `Rock_Edge_Small`
- `Rock_Edge_Medium`
- `Rock_Edge_Large`

---

# 16. 해안 스타일

하단 바다는 하천보다 넓고 열린 수역으로 보이게 한다.

구성:

```text
도시/도로
→ 녹지 또는 제방
→ 바위
→ 얕은 물색
→ 깊은 파랑
```

해안선은 단순 직선 하나가 아니라 바위와 수변 경계로 자연스럽게 만든다.

---

# 17. 항만 수역 스타일

항만 수역은 일반 해안과 구분한다.

특징:

- 직선 부두
- 콘크리트 경계
- 크레인
- 컨테이너
- 선박
- 산업적 빈 공간

물 색은 도시 하천/바다와 같은 계열을 공유한다.

---

# 18. 바위 스타일

Maps.png 바위는 둥근 실사 돌이 아니라 단순한 로우폴리 덩어리다.

기본:

- 회색 2~3단 면
- Small / Medium / Large
- 회전/Scale 재사용
- 물 경계에 군집 배치

개별 바위 종류를 과도하게 늘리지 않는다.

---

# 19. 건물 공통 스타일

Maps.png 건물은 실제 도시 건축보다 단순화되어 있다.

구성:

```text
명확한 외곽 실루엣
+ 지붕 면
+ 얕은 벽면
+ 푸른 창/유리 영역
+ 옥상 설비 1~3개
+ 기능 포인트
+ 짧은 그림자
```

금지:

- 실사 건축 사진 Texture
- 창문 하나하나의 내부 표현
- 작은 글자 간판 남용
- 과도한 PBR 재질
- 리소스마다 다른 투시각

---

# 20. 주거 건물 스타일

Maps.png 좌측 상단 참고.

특징:

- 흰색/밝은 벽
- 빨강/파랑/회색 지붕
- 작은 마당/나무
- 단독주택과 중층 건물 혼합

필수:

- House_A/B/C
- ResidentialMidrise_A/B
- SmallApartment_A

지붕색은 Tint Variation을 적극 사용한다.

---

# 21. 상업·업무 건물 스타일

특징:

- 흰색/회색 본체
- 파란 유리창 면
- 평평하거나 단순한 지붕
- 옥상 설비
- 직사각형 실루엣

상가 표현은 큰 텍스트 간판보다 색상 포인트와 작은 Sign Overlay를 우선한다.

---

# 22. 병원 스타일

Maps.png 병원 캠퍼스는 매우 강한 Landmark다.

필수 시각 특징:

- 흰색 대형 본체
- 넓은 파란 유리창
- 큰 빨간 의료 Cross
- 응급 진입부
- 보조 의료동
- 주차장
- 원형 회차부
- 녹지

병원 1개 Sprite가 아니라 **여러 건물과 외부 공간으로 구성된 Landmark Set**으로 제작한다.

---

# 23. 산업·물류 스타일

특징:

- 큰 직사각형 건물
- 회색/파랑 중심
- 주황/노랑 포인트
- 큰 야드
- 트럭/화물차
- 탱크/사일로

건물보다 작업 공간과 물류 요소 비율이 높다.

---

# 24. 항만 스타일

필수:

- ContainerCrane
- Container
- Dock
- CargoShip
- Tank/Silo
- PortYard

Container는 기본 Sprite + Tint를 우선한다.

크레인은 큰 주황/노랑 포인트로 Maps.png처럼 멀리서도 읽혀야 한다.

---

# 25. 소방서 스타일

필수:

- 빨강/흰색/회색 본관
- 여러 Garage Door
- 높은 빨간 Tower
- 넓은 전면 출동 야드
- 소방차/구급차

건물 자체보다 **본관 + 타워 + 야드 + 차량** 전체가 Landmark로 읽혀야 한다.

---

# 26. 공원 구조물 스타일

사용:

- Pavilion
- 작은 Shelter
- Bench
- Fountain
- ParkPath

공원 구조물은 자연보다 시각적으로 강해지지 않게 한다.

Maps.png 중앙 하천 공원은 건물보다 나무, 물, 바위, 길이 주인공이다.

---

# 27. ParkPath 스타일

ParkPath는 Sidewalk와 구분한다.

특징:

- 따뜻한 베이지/모래색
- 부드러운 곡선
- 잔디와 직접 연결
- 공원 내부에서 자유롭게 굽음

일반 도로 Sidewalk처럼 직각 Grid로 강제하지 않는다.

---

# 28. 식생 스타일

Maps.png의 핵심 특징 중 하나가 높은 식생 밀도다.

기본:

- Tree_Small
- Tree_Medium
- Tree_Large
- Bush
- Hedge
- Flower
- FlowerTree

나무:

- 하나의 매끈한 원보다 여러 면 덩어리
- 진녹색/중간 녹색/연녹색 3단 명암
- 짧은 그림자
- 둥글지만 Low-Poly 느낌

회전/Scale/Tint로 재사용한다.

---

# 29. 분홍 꽃나무

Maps.png에는 분홍 꽃나무가 반복되지만 주색은 아니다.

사용 위치:

- 주거 녹지
- 병원
- 공원
- 광장
- 외곽 녹지 일부

일반 녹색 나무가 항상 더 큰 비중을 차지한다.

---

# 30. 광장과 분수

Maps.png에는 작은 분수 광장이 여러 번 등장한다.

스타일:

- 따뜻한 베이지 포장
- 선명한 파란 분수
- 둥근/기하학적 화단
- 작은 나무와 꽃

분수는 다른 물 리소스와 같은 파랑 계열을 사용한다.

---

# 31. 주차장 스타일

- 도로보다 약간 어둡거나 비슷한 회색
- 흰색 Parking Line
- 작은 차량 반복
- 나무섬/녹지섬
- 큰 빈 공간을 유지

주차 차량은 건물보다 디테일을 낮게 한다.

---

# 32. 차량 스타일

Maps.png 일반 차량은 작지만 색상 대비가 강하다.

기본 차종:

- Sedan
- SUV
- Van
- Truck
- Bus
- EmergencyVehicle

특징:

- 단순한 지붕/보닛/유리 면
- 짧은 그림자
- 소수 기본 Sprite + Tint
- 크기와 시점 통일

---

# 33. 시민 스타일

시민은 매우 작게 보이므로 세부 얼굴보다 실루엣을 우선한다.

필요한 경우:

- 머리
- 몸통
- 팔/다리
- 옷 색

고해상도 캐릭터 이미지를 사용하지 않는다.

현재 맵 리소스 1차 제작에서는 시민은 후순위다.

---

# 34. 소품 스타일

기본:

- TrafficLight
- StreetLight
- RoadSign
- Fence
- TrafficIsland
- ParkingBarrier
- Bench
- TrashBin
- Hydrant
- BusStop

크기:

- 64~128 px 중심

Maps.png에서 작은 화면에 거의 보이지 않는 소품은 과도하게 만들지 않는다.

---

# 35. 투명 배경 규칙

독립 리소스:

- PNG
- Transparent Background
- 오브젝트 전체가 잘리지 않음
- 최소 투명 여백

불투명 타일을 우선할 수 있는 것:

- Ground
- Road Base
- Water Base

---

# 36. Pivot 규칙

기본:

- 일반 독립 Sprite: Center
- 건물: 같은 카테고리끼리 동일 Pivot
- 특수 Landmark: 배치가 편한 전용 Pivot 허용

중요:

- 같은 종류의 건물에서 Pivot 규칙을 섞지 않는다.
- Pivot 때문에 Maps.png의 블록 정렬이 어려워지면 규칙을 먼저 통일한다.

---

# 37. 해상도 규칙

실제 모바일 화면에서 보이는 크기를 기준으로 한다.

권장:

- 소품: 64~128 px
- Small 건물: 128~256 px
- Medium 건물: 256 px 중심
- Large 건물: 256~512 px
- Landmark: 512 px 중심
- 큰 특수 리소스: 필요 시 1024 px 검토

2048 / 4096 px 개별 리소스는 기본 금지한다.

---

# 38. Sprite Atlas

권장:

- GroundAtlas
- RoadAtlas
- RoadMarkingAtlas
- SidewalkAtlas
- WaterCoastAtlas
- BuildingCommonAtlas
- BuildingLandmarkAtlas
- IndustrialPortAtlas
- VegetationAtlas
- PropAtlas
- VehicleAtlas
- CitizenAtlas

Atlas는 1024~2048을 우선 검토한다.

동일 Sprite를 여러 Atlas에 중복 포함하지 않는다.

---

# 39. Texture 설정

권장:

- Read/Write: Off
- Mip Map: 고정 탑뷰 2D Sprite는 기본 Off
- Web Platform Override 사용
- 투명 여백 최소화
- 필요 이상의 Max Size 금지
- Alpha가 필요 없는 Base Tile은 Alpha 제거 검토

Normal / Mask / AO / Metallic / Roughness Map은 기본적으로 제작하지 않는다.

명암은 Color Sprite에 Bake한다.

---

# 40. 이미지 생성 시 최우선 참고 자료

이미지 생성 요청에는 가능한 한 다음을 함께 제공한다.

```text
1. Maps.png
2. 같은 카테고리에서 이미 승인된 CodeBlueRush 리소스
3. 필요한 경우 실제 도시/서울 참고 이미지
```

3번은 구조 보조 참고일 뿐이다.

1번과 충돌하면 Maps.png를 따른다.

---

# 41. 이미지 생성 기본 프롬프트 구조

```text
[1] 프로젝트
CodeBlueRush / Unity 2D / mobile WebGL

[2] 최우선 Reference
Match the provided Maps.png first.

[3] 시점
high-angle near-orthographic top-down view,
roads and ground read as top-down,
buildings may show shallow side faces,
minimal perspective distortion

[4] 대상
정확한 리소스 이름과 용도

[5] 스타일
bright colorful casual low-poly toy-like city,
clean simplified geometry,
dense lush vegetation,
clear readable silhouette

[6] 명암
upper-left light,
short soft baked shadow toward lower-right,
2~3 step planar shading

[7] 색상
match Maps.png saturation and palette

[8] 배경
transparent PNG when independent asset

[9] 금지
no strong isometric angle,
no photorealistic texture,
no strong perspective,
no black cartoon outline,
no cropped object,
no excessive micro detail

[10] 크기
target production resolution from this guide
```

---

# 42. 공통 이미지 생성 프롬프트 예시

```text
CodeBlueRush Unity 2D map asset.

Use the provided Maps.png as the primary and highest-priority visual reference.
The new asset must look as if it belongs inside that exact city.

High-angle near-orthographic top-down view.
Roads and ground must remain easy to read from above.
Buildings may show shallow side faces like Maps.png,
but avoid strong perspective or a 45-degree isometric look.

Bright colorful casual low-poly toy-like city style.
Clean simplified geometry.
Clear silhouette.
Dense but controlled detail.
2 to 3-step planar shading.
Upper-left lighting.
Short soft baked shadow toward lower-right.

Match the saturation, road gray, vivid blue water,
lush green vegetation, white/blue building palette,
and small red/orange accents visible in Maps.png.

Transparent background for an independent asset.
Keep the object fully visible and centered.
Minimal transparent padding.
No photorealistic texture.
No heavy black outline.
No excessive tiny details.
No cropped object.
```

---

# 43. 도로 리소스 생성 추가 문구

도로 생성 시 추가:

```text
The road must connect seamlessly with the CodeBlueRush road set.
Use clean medium-dark gray asphalt,
clear yellow center marking,
bright white crosswalk markings,
and a light gray/beige sidewalk edge.
Preserve top-down readability and exact connection geometry.
```

---

# 44. 건물 리소스 생성 추가 문구

건물 생성 시 추가:

```text
Use a compact toy-like low-poly building mass.
Show the roof clearly and only a shallow amount of wall surface.
Use simple blue glass areas, clean white/gray walls,
and one or two strong functional color accents.
The building must match the scale and camera angle of Maps.png.
```

---

# 45. 식생 리소스 생성 추가 문구

```text
Create a dense low-poly tree canopy made from several simple foliage masses.
Use dark, mid, and light green planar shading.
Keep a compact top-down silhouette and a short baked shadow.
Match the lush vegetation density of Maps.png.
```

---

# 46. 생성 결과 QA — 시점

하나라도 실패하면 수정한다.

- [ ] 도로/바닥이 위에서 읽히는가
- [ ] 건물 측면이 너무 많이 보이지 않는가
- [ ] Maps.png와 카메라 각도가 비슷한가
- [ ] 강한 아이소메트릭이 아닌가
- [ ] Perspective 왜곡이 거의 없는가
- [ ] 같은 카테고리 리소스의 각도가 모두 같은가

---

# 47. 생성 결과 QA — 스타일

- [ ] Maps.png와 전체 밝기가 비슷한가
- [ ] 색상이 충분히 선명한가
- [ ] Low-Poly 면 분할이 보이는가
- [ ] 실사 질감이 없는가
- [ ] Outline이 과하지 않은가
- [ ] 광원 방향이 동일한가
- [ ] 그림자 방향이 동일한가
- [ ] 장난감 도시 같은 정돈된 느낌이 있는가

---

# 48. 생성 결과 QA — 도로/보도

- [ ] Road width가 기존 세트와 맞는가
- [ ] 중앙선 위치가 맞는가
- [ ] 회전 재사용 가능한가
- [ ] T자/십자 연결부가 정확한가
- [ ] Sidewalk 폭이 일관적인가
- [ ] Crosswalk가 Sidewalk와 연결되는가

---

# 49. 생성 결과 QA — 건물

- [ ] 블록에 넣었을 때 Maps.png 비율과 맞는가
- [ ] 지붕이 충분히 보이는가
- [ ] 벽면이 너무 높게 노출되지 않는가
- [ ] Collider를 단순하게 잡을 수 있는 실루엣인가
- [ ] 기능이 색/형태만으로 구분되는가
- [ ] 다른 건물과 광원/시점이 같은가

---

# 50. 생성 결과 QA — 자연

- [ ] 물이 Maps.png처럼 선명한 파랑인가
- [ ] 하천 Edge가 자연스럽게 조합 가능한가
- [ ] 바위가 Low-Poly로 통일되는가
- [ ] 나무가 너무 플랫하거나 실사적이지 않은가
- [ ] 꽃나무가 과도하게 많지 않은가

---

# 51. 최적화 QA

- [ ] 필요한 해상도 이상으로 크지 않은가
- [ ] 기존 Sprite 회전으로 대체 가능한가
- [ ] Tint로 대체 가능한가
- [ ] 투명 여백이 큰가
- [ ] Atlas에 넣을 수 있는가
- [ ] 불필요한 추가 Texture Map이 있는가
- [ ] 같은 기능의 중복 PNG가 있는가

---

# 52. 제작 순서

현재 프로젝트 방향에 맞춰 리소스를 먼저 만든다.

```text
1. Ground / Water / Coast / Rock
↓
2. Road / Intersection / Bridge / Sidewalk / Crosswalk
↓
3. Maps.png 도로·보도 구조를 NewGamePlay에 배치
↓
4. 도로 구조 QA
↓
5. 일반 건물
↓
6. 병원 / 항만 / 소방서 Landmark Set
↓
7. Parking / Plaza / Fountain
↓
8. Vegetation
↓
9. Traffic / Environment Props
↓
10. 차량
↓
11. 시민
↓
12. 시스템 로직 연결
```

---

# 53. 파트별 검증 흐름

```text
해당 파트 리소스 전체 제작
↓
Maps.png 스타일 대조
↓
Unity Import
↓
Atlas / Compression 설정
↓
해당 파트 전체 배치
↓
Maps.png와 Scene Screenshot 비교
↓
문제 수정
↓
해당 파트만 재테스트
↓
Release WebGL Build 용량 확인
↓
통과 시 다음 파트 진행
```

관련 없는 전체 시스템 회귀 테스트는 매번 하지 않는다.

---

# 54. Maps.png 대조 방법

각 파트 완료 후 가능한 한 같은 비율의 Scene Screenshot을 만든다.

비교 순서:

```text
1. 큰 도시 윤곽
2. 도로/하천 위치
3. 블록 크기
4. 랜드마크 실루엣
5. 녹지 밀도
6. 색상
7. 명암
8. 작은 장식
```

작은 장식이 비슷해도 1~4가 다르면 실패다.

---

# 55. 용량 Gate

최종 웹사이트:

- 목표: 85 MB 이하 권장
- 90 MB 이상: 경고
- 95 MB 이상: 재최적화
- 100 MB 초과: 실패

단, 최적화 순서는 다음으로 한다.

```text
1. 투명 여백 제거
2. Max Size 감소
3. 압축 강화
4. 중복 Sprite 통합
5. 회전/반전/Tint 재사용
6. 불필요한 Variation 삭제
7. 작은 장식 축소
8. 그래도 부족하면 비핵심 리소스 종류 축소
```

Maps.png의 주요 도로, 하천, 병원, 항만, 소방서 구조는 마지막까지 유지한다.

---

# 56. 최종 금지 사항

- Maps.png보다 다른 참고 이미지를 우선하는 것
- Maps.png에 없는 강한 아이소메트릭 시점
- 실사 사진 Texture
- 리소스마다 다른 카메라 각도
- 리소스마다 다른 광원 방향
- 과도한 검은 Outline
- 작은 화면에서 보이지 않는 미세 디테일
- 방향만 다른 중복 PNG
- 색만 다른 중복 PNG
- 불필요한 2K/4K Texture
- 전체 도시를 한 장의 거대한 Background PNG로 사용
- Normal/Mask/AO Texture 남용
- 잘린 오브젝트
- 큰 투명 여백
- 병원/항만/소방서 Landmark의 실루엣 약화
- 용량을 이유로 Maps.png의 핵심 구조를 임의 삭제

---

# 57. 최종 승인 Checklist

신규 리소스는 다음 항목이 모두 PASS일 때 승인한다.

- [ ] Maps.png를 최우선 Reference로 사용함
- [ ] Maps.png와 비슷한 높은 각도의 탑뷰임
- [ ] 강한 Perspective가 없음
- [ ] 도로/지면이 위에서 명확히 읽힘
- [ ] 건물의 얕은 입체감이 Maps.png와 유사함
- [ ] 좌측 상단 광원 계열이 유지됨
- [ ] 우측 하단 짧은 그림자가 유지됨
- [ ] 밝고 선명한 캐주얼 Low-Poly 색감임
- [ ] 도로 회색/물 파랑/녹지 톤이 Maps.png와 어울림
- [ ] 모바일에서 실루엣이 명확함
- [ ] 기존 리소스와 Scale이 맞음
- [ ] 재사용 가능 여부 확인 완료
- [ ] Atlas 적용 가능
- [ ] 불필요한 고해상도 Texture가 없음
- [ ] 최종 WebGL 용량 Gate 통과 가능

---

# 58. 최종 제작 원칙

CodeBlueRush의 맵 리소스는 다음 문장으로 정의한다.

> **`Maps.png`의 도로 구조, 블록 배치, 랜드마크 구성, 높은 각도의 탑뷰, 선명한 물과 녹지, 장난감 같은 캐주얼 로우폴리 표현을 최우선으로 재현한다.**

최적화는 다음 원칙으로 한다.

> **구조를 줄이는 것이 아니라 같은 구조를 더 적은 Sprite, 더 낮은 해상도, 더 높은 재사용률로 표현한다.**
