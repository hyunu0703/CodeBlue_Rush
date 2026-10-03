# CodeBlueRush Map Asset Style Guide

## 1. 문서 목적

이 문서는 CodeBlueRush의 전체 맵 리소스를 동일한 스타일과 규격으로 제작하기 위한 기준이다.

대상:
- 이미지 생성
- Unity Sprite Import
- Prefab 제작
- 맵 조립
- City Generator 적용

`CodeBlueRush_MapAssetProductionGuide.md`가 **무엇을 제작할지** 정의한다면,
이 문서는 **어떤 모습과 규격으로 제작할지** 정의한다.

---

# 2. 프로젝트 환경

- Unity: 6000.3.23f1
- Language: C#
- Game Type: 2D Top View
- Platform: WebGL
- Screen: Portrait 480 × 854
- Single Player

---

# 3. Master Style Reference

CodeBlueRush의 최종 도시 맵 이미지를 모든 맵 리소스의
**Master Style Reference**로 사용한다.

권장 저장 경로:

`Assets/Image/Maps/Reference/CodeBlueRush_Map_MasterReference.png`

## 절대 규칙

새로운 리소스를 만들 때 임의의 스타일을 새로 정의하지 않는다.

항상 Master Style Reference의 다음 요소를 기준으로 한다.

- 카메라 시점
- 형태 단순화 정도
- 색상 채도
- 명암 대비
- 로우폴리 면 분할
- 그림자 표현
- 건물 비율
- 도로 비율
- 나무 형태
- 자동차 형태
- 전체적인 밝기

새 리소스가 단독으로 예쁘게 보이는 것보다
**기존 리소스와 함께 배치했을 때 같은 게임의 리소스로 보이는 것**을 우선한다.

---

# 4. 카메라 / 시점

## 고정 규칙

모든 게임플레이용 리소스는:

**2D Orthographic Top View**

기준으로 제작한다.

금지:

- Isometric
- 3점 투시
- 원근 투시
- 기울어진 카메라
- 앞면을 과도하게 보여주는 건물
- 리소스마다 다른 카메라 각도

## 건물

건물은 위에서 바라본 형태를 중심으로 한다.

지붕이 가장 잘 보이고,
외벽은 깊이 표현을 위한 최소한의 부분만 보이도록 한다.

Master Reference보다 더 강한 입체감을 추가하지 않는다.

---

# 5. 스타일

## 고정 스타일

- 밝고 선명한 Casual Low Poly
- 단순화된 형태
- 명확한 실루엣
- 적은 수의 큰 색상 면
- 부드러운 명암보다 면 단위 명암
- 과도한 텍스처 표현 금지
- 실제 사진 같은 표현 금지

## 디테일 기준

480 × 854 모바일 화면에서 알아볼 수 없는 작은 디테일은 줄인다.

예:

좋음:

- 큰 창문
- 지붕 형태
- 자동차 색상
- 큰 간판 형태
- 나무의 큰 면 분할

피해야 함:

- 작은 볼트
- 작은 벽돌 하나하나
- 매우 작은 글자
- 미세한 표면 스크래치
- 지나치게 복잡한 텍스처

---

# 6. 색상

## 고정 규칙

Master Reference의 색상 분위기를 유지한다.

전체적으로:

- 밝음
- 선명함
- 높은 가독성
- 적당히 높은 채도
- 지나친 네온 색상 금지
- 지나치게 어두운 색상 금지

### 주요 색상 방향

도로:
- Dark Gray

인도:
- Light Gray / Beige Gray

잔디:
- Bright Green

나무:
- Light Green + Medium Green + Dark Green

물:
- Clear Blue / Cyan Blue

병원:
- White + Blue

구급대:
- White + Red

주택:
- White / Beige 벽
- Red / Blue / Gray 계열 지붕

산업시설:
- Blue / Gray

## 색상 변형

같은 종류의 리소스는 완전히 다른 색상 팔레트를 만들지 않는다.

예:

Tree_01
Tree_02
Tree_03

은 형태와 밝기에 차이를 줄 수 있지만,
같은 도시의 나무처럼 보여야 한다.

---

# 7. 광원

## 고정 규칙

모든 리소스의 광원 방향을 통일한다.

기준:

**왼쪽 위 → 오른쪽 아래**

즉:

- 왼쪽 위 면 = 상대적으로 밝음
- 오른쪽 아래 면 = 상대적으로 어두움
- 그림자 = 오른쪽 아래 방향

Master Reference에서 특정 리소스의 광원 표현과 충돌하는 경우
Master Reference를 우선한다.

리소스마다 광원 방향을 변경하지 않는다.

---

# 8. 그림자

## 기본 원칙

건물, 차량, 나무처럼 높이가 있는 오브젝트에는
간단한 접지 그림자를 사용할 수 있다.

그림자는:

- 짧고 단순하게
- 반투명
- 부드러운 가장자리
- 오른쪽 아래 방향

으로 통일한다.

## 금지

- 매우 긴 그림자
- 검은색에 가까운 강한 그림자
- 사실적인 복잡한 그림자
- 리소스마다 방향이 다른 그림자

## 별도 그림자 Sprite

현재 기본 방식은:

**오브젝트 Sprite에 간단한 그림자 포함**

을 권장한다.

게임에서 동적 그림자가 필요해질 경우에만 별도 Sprite 방식으로 변경한다.

---

# 9. 투명 배경

건물, 차량, 나무, 소품 등 독립 오브젝트는:

**Transparent PNG**

로 제작한다.

배경에 다음을 넣지 않는다.

- 흰색
- 검은색
- 하늘
- 잔디
- 도로
- 임의의 바닥

단, 바닥 자체가 리소스인 경우는 예외다.

예:

- Road
- Grass
- Sidewalk
- Parking
- Water

---

# 10. Sprite Padding

독립 Sprite가 이미지 경계에 바로 붙지 않도록 한다.

## 권장값

전체 이미지 크기의 약:

**5 ~ 10%**

를 투명 여백으로 둔다.

예:

256 × 256 Sprite라면
약 12 ~ 24px 정도의 안전 여백을 확보한다.

단:

도로 타일처럼 정확한 연결이 필요한 리소스는
연결 방향에 Padding을 사용하지 않는다.

---

# 11. 기준 방향

방향성이 있는 리소스는 기본 방향을 통일한다.

## 차량

기본 방향:

**위쪽(North)**

차량은 가능하면 한 방향 Sprite를 제작하고
Unity Transform 회전으로 재사용한다.

별도 방향 Sprite는 회전 시 디자인이 깨지는 경우에만 사용한다.

## 건물

건물 입구 기본 방향:

**아래쪽(South)**

필요한 경우:

- North
- East
- South
- West

버전을 별도로 제작한다.

## 시민

기본 방향 세트를 사용할 경우:

- North
- South
- East
- West

4방향을 기본으로 한다.

---

# 12. Sprite 기본 해상도

아래 값은 **권장 제작 해상도**이며
Unity 월드 규격 확인 후 변경할 수 있다.

## 바닥 / 도로 타일

권장:

`512 × 512 px`

대형 교차로:

`1024 × 1024 px`

## 나무 / 소형 오브젝트

권장:

`256 × 256 px`

## 차량

권장:

`256 × 256 px`

실제 차량이 이미지 전체를 채우지 않도록
Padding을 유지한다.

## 시민

권장:

`128 × 128 px`
또는
`256 × 256 px`

## 소형 건물

권장:

`512 × 512 px`

## 중형 건물

권장:

`768 × 768 px`
또는
`1024 × 1024 px`

## 대형 건물

병원 / 구급대 / 슈퍼마켓 등:

`1024 × 1024 px`

필요하면 더 큰 원본을 제작한 뒤 Unity에서 축소한다.

---

# 13. 도로 규격

도로는 모든 맵 리소스 중 가장 먼저 규격을 맞춰야 한다.

## 중요 규칙

같은 도로 종류는:

- 전체 폭
- 차선 폭
- 중앙선 위치
- 연결 위치
- 인도 연결 위치

가 완전히 동일해야 한다.

## 기준 타일

권장:

`512 × 512 px`

을 도로 기본 Tile Unit으로 사용한다.

단, 현재 CodeBlueRush의 기존 Road 시스템과 크기가 다르면
**기존 Road Prefab의 실제 Unity 크기를 우선한다.**

---

# 14. 도로 연결 규칙

직선 / 코너 / T자 / 십자 교차로는
같은 도로 규격을 공유한다.

예:

```text
Road_Straight
      ↓
Road_Corner
      ↓
Road_TJunction
      ↓
Road_Intersection

모든 연결 지점의 폭과 중심 위치가 동일해야 한다.
도로 끝
도로 Sprite 끝부분에는:
- 나무
- 차량
- 건물
- 장식
을 넣지 않는다.
도로 연결을 방해하는 요소를 제거한다.
15. 차선
차선은 가능한 일정한 폭을 유지한다.
필수 통일 요소
- 중앙선 굵기
- 흰색 차선 굵기
- 차선 간격
- 정지선 굵기
- 화살표 크기
- 횡단보도 간격
도로마다 임의로 크기를 바꾸지 않는다.
16. 도로와 노면 표시 분리
가능하면 다음 요소는 별도의 Sprite Overlay로 제작한다.
- 진행 방향 화살표
- 정지선
- 주차선
- 특수 도로 표시
- 버스 표시
- 일부 횡단보도
장점:
- 같은 도로 재사용 가능
- Prefab 수 감소
- 랜덤 맵 조립 용이
단, 교차로처럼 항상 같은 표시가 필요한 경우
도로 Sprite에 포함할 수 있다.
17. 인도
인도 역시 도로와 연결되는 규격을 통일한다.
통일 대상:
- 인도 폭
- 연석 폭
- 코너 반경
- 색상
- 바닥 패턴
도로 타일과 인도 타일의 경계에 틈이 생기지 않아야 한다.
18. 건물 규격
건물은 독립 Sprite로 제작한다.
건물 Sprite에는 기본적으로:
- 건물 본체
- 지붕
- 고정된 건물 장식
- 간단한 그림자
만 포함한다.
가능하면 다음 요소는 분리한다.
- 주차장
- 나무
- 차량
- 벤치
- 가로등
- 도로
- 대형 화단
이를 통해 같은 건물을 여러 배치에서 재사용한다.
19. 건물 디자인
Master Reference의 형태를 기준으로 한다.
병원
주요 특징:
- White
- Blue
- 의료시설 느낌
- 큰 건물
- Rooftop Helipad 가능
- Emergency Entrance
구급대
주요 특징:
- White
- Red
- 다수의 Ambulance Garage
- 병원과 시각적으로 구분
상업시설
- 밝은 색상
- 명확한 입구
- 서로 다른 지붕 형태
- 간단한 브랜드 느낌
실제 브랜드 로고를 복제하지 않는다.
주택
다양성은:
- 지붕 색상
- 건물 크기
- 마당 형태
위주로 만든다.
스타일 자체는 변경하지 않는다.
20. 텍스트
게임 리소스에 불필요한 텍스트를 사용하지 않는다.
텍스트가 반드시 필요한 경우:
- H
- Emergency
- Hospital Symbol
- Pharmacy Symbol
- Parking Symbol
처럼 게임플레이에서 의미가 명확한 요소만 사용한다.
작은 장식용 텍스트는 피한다.
가능하면 텍스트는 Unity UI/Text로 처리한다.
21. 식생
나무는 Master Reference의 Low Poly 형태를 따른다.
나무 기본 구조
- 둥근 실루엣
- 여러 개의 polygon-like 면
- 밝은 초록
- 중간 초록
- 어두운 초록
2~4단계 정도의 색상 면으로 표현한다.
반복 방지
같은 나무를 여러 번 배치할 때는:
- 크기 약간 변경
- 회전 변경
- 2~4개의 Variant 사용
으로 반복감을 줄인다.
22. 차량
차량은 도로보다 확실히 작게 보여야 한다.
종류별 크기 관계:
일반 승용차
<
밴
<
구급차 / 소형 트럭
<
버스 / 대형 트럭

차량마다 비율을 임의로 바꾸지 않는다.
구급차는 CodeBlueRush의 주요 플레이 오브젝트이므로
일반 차량보다 시각적으로 쉽게 구분되어야 한다.
23. 시민
시민은 모바일 화면에서 식별 가능한 단순한 실루엣을 사용한다.
과도한 얼굴 묘사는 하지 않는다.
구분은 주로:
- 머리 색
- 상의 색
- 하의 색
- 체형
- 작은 액세서리
정도로 한다.
환경 리소스보다 시각적으로 튀지 않도록 한다.
24. Asset Naming Convention
파일 이름은 영어를 사용한다.
기본 형식:
Category_Type_Variant.png
예:
Road_Straight_2Lane.png
Road_Straight_4Lane.png

Road_Corner_NE.png
Road_Corner_NW.png
Road_Corner_SE.png
Road_Corner_SW.png

Road_TJunction_N.png
Road_TJunction_E.png

Road_Intersection_4Way.png

Sidewalk_Straight_01.png

Tree_Small_01.png
Tree_Medium_01.png
Tree_Large_01.png

Building_Hospital_01.png
Building_AmbulanceStation_01.png
Building_House_Red_01.png

Vehicle_Car_Red_01.png
Vehicle_Car_Blue_01.png
Vehicle_Ambulance_01.png

Prop_TrafficLight_01.png
Prop_Bench_01.png

공백과 한글 파일명을 사용하지 않는다.
25. 권장 폴더 구조
Assets/
└── Image/
    └── Maps/
        ├── Reference/
        ├── Ground/
        ├── Water/
        ├── Road/
        ├── RoadMarking/
        ├── Sidewalk/
        ├── Nature/
        │   ├── Tree/
        │   ├── Bush/
        │   └── Flower/
        ├── Building/
        │   ├── Medical/
        │   ├── Commercial/
        │   ├── Residential/
        │   ├── Office/
        │   └── Industrial/
        ├── Park/
        ├── Prop/
        ├── Parking/
        ├── Vehicle/
        ├── Citizen/
        ├── Harbor/
        └── Helper/

폴더를 필요 이상으로 세분화하지 않는다.
26. Unity Import 기본 설정
다음은 권장 기본값이다.
Texture Type
Sprite (2D and UI)
Sprite Mode
독립 이미지:
Single
Sprite Sheet를 사용하는 경우:
Multiple
Pixels Per Unit
현재 프로젝트 월드 크기와 기존 Ambulance / Road Prefab 규격을 확인한 뒤 확정한다.
모든 맵 리소스는 동일한 기준 PPU를 사용하는 것을 우선한다.
임의로 Sprite마다 다른 PPU를 사용하지 않는다.
Mesh Type
권장:
Full Rect
특히 Tile / Road / Ground 계열은 Full Rect를 사용한다.
Filter Mode
Master Reference의 부드러운 Low Poly 스타일을 위해:
Bilinear
권장.
Pixel Art Filter를 사용하지 않는다.
Compression
원본 제작 및 품질 확인 단계:
None
권장.
WebGL 최종 빌드 크기가 문제가 될 경우
Profiler 및 Build Report 확인 후 압축 설정을 조정한다.
27. Pivot
Ground / Road / Tile
Center
차량
Center
나무 / 소품
기본:
Center
물리 위치 기준이 중요한 경우:
Bottom Center
검토 가능.
건물
기본:
Center
단, 기존 City Generator의 배치 기준과 맞지 않는다면
Prefab 기준점을 우선한다.
28. Sprite 회전
같은 형태를 Unity Transform 회전으로 재사용할 수 있다면
불필요한 Sprite를 추가 생성하지 않는다.
예:
직선 도로:
Horizontal
Vertical

을 모두 만들 필요가 없는 구조라면 하나를 회전해서 사용한다.
그러나:
- 그림자 방향
- 텍스트
- 비대칭 구조
때문에 회전 결과가 어색하면 별도 Sprite를 사용한다.
29. Random City Generator 고려
CodeBlueRush의 리소스는 단일 정적인 맵뿐 아니라
City Generator에서 조합될 수 있어야 한다.
따라서:
- Road 연결 규격 통일
- Building 독립 배치
- Decoration 분리
- Sidewalk 분리
- Parking 분리
- 불필요한 주변 요소 제거
를 우선한다.
한 Sprite 안에 지나치게 많은 시스템 요소를 합치지 않는다.
30. Collider 기준
이미지 자체에서 Collider 형태를 결정하지 않는다.
Collider는 Unity Prefab 단계에서 설정한다.
예:
Building Sprite
↓
Building Prefab
↓
Collider2D

도로 / 장식 / 건물 이미지는
Collider 때문에 이미지 형태를 변경하지 않는다.
31. Sorting Layer 고려
권장 렌더링 순서:
Ground
Water
Road
RoadMarking
Sidewalk
Decoration
Building
Vehicle
Citizen
Effect
UI

실제 프로젝트의 기존 Sorting Layer가 있다면
기존 설정을 우선한다.
32. 제작 단계
새로운 카테고리를 제작할 때 다음 순서를 따른다.
Master Reference 확인
↓
기존 같은 카테고리 Asset 확인
↓
크기 규격 확인
↓
대표 Asset 1개 제작
↓
Unity 테스트
↓
크기 / 스타일 확인
↓
대표 Asset 확정
↓
같은 카테고리 Variant 제작

처음부터 카테고리 전체 이미지를 대량 생성하지 않는다.
대표 Asset을 먼저 확정한다.
33. 이미지 생성 작업 규칙
이미지 생성 시 항상 다음 내용을 전달한다.
CodeBlueRush Master Style Reference를 사용한다.

완전한 2D Orthographic Top View.

Master Reference와 동일한:
- 색상
- Low Poly 면 표현
- 광원
- 명암
- 카메라
- 형태 단순화 수준

을 유지한다.

Transparent PNG.

Unity에서 독립 Sprite로 사용할 수 있도록
주변 건물 / 도로 / 나무 / 차량을 포함하지 않는다.

34. 카테고리 내부 통일
같은 카테고리의 첫 번째 확정 Asset을
해당 카테고리의 Secondary Reference로 사용한다.
예:
Master Map
+
Road_Straight_2Lane
↓
나머지 Road 제작 기준

또는:
Master Map
+
Tree_Medium_01
↓
나머지 Tree 제작 기준

이 방법으로 이미지 생성 과정에서 발생하는
스타일 변화를 최소화한다.
35. 이미지 생성 우선순위
권장 순서:
Ground
↓
Water / Coast
↓
Road
↓
Road Marking
↓
Sidewalk
↓
Nature
↓
Traffic Props
↓
Parking
↓
Medical Buildings
↓
Commercial Buildings
↓
Residential Buildings
↓
Office Buildings
↓
Industrial / Harbor
↓
Park
↓
Vehicles
↓
Citizens
↓
Helper Assets

이전 단계 Asset을 다음 단계의 Reference로 활용한다.
36. 하지 말아야 할 것
다음 항목은 금지한다.
- Master Reference 없이 이미지 생성
- Asset마다 다른 카메라 시점
- Isometric 변환
- Asset마다 다른 광원
- 지나치게 사실적인 텍스처
- Pixel Art 스타일 혼합
- Asset마다 다른 Low Poly 강도
- 같은 도로인데 폭이 다른 Sprite
- 도로 연결 위치 불일치
- 불필요한 배경 포함
- 불필요한 문자 생성
- 건물과 차량을 한 Sprite로 합치기
- 나무와 건물을 불필요하게 합치기
- 이미지 하나에 전체 지역을 통째로 제작
- Unity에서 재사용할 수 없는 구조 제작
37. Asset 완료 조건
각 Asset은 아래 조건을 모두 만족해야 완료로 판단한다.
Visual
- Master Reference와 같은 스타일인가?
- Orthographic Top View인가?
- Low Poly 표현이 동일한가?
- 광원 방향이 동일한가?
- 채도와 명암이 동일한가?
- 다른 Asset과 함께 배치해도 어색하지 않은가?
Technical
- Transparent PNG인가?
- 필요한 Padding이 있는가?
- 파일명이 규칙에 맞는가?
- Sprite가 독립적으로 사용할 수 있는가?
- 불필요한 배경이 없는가?
Road
도로 Asset이라면 추가 확인:
- 다른 Road Tile과 폭이 동일한가?
- 연결 위치가 동일한가?
- 중앙선이 정확히 이어지는가?
- 차선이 정확히 이어지는가?
- 인도와 연결 가능한가?
Unity
- Sprite Import가 정상인가?
- Pivot이 적절한가?
- PPU가 프로젝트 기준과 동일한가?
- Full Rect가 필요한 Asset에 적용됐는가?
- 배치했을 때 크기가 정상인가?
38. 최종 기준
CodeBlueRush 맵 Asset 제작에서 가장 중요한 우선순위는 다음과 같다.
1. Master Reference와의 시각적 통일
2. 게임플레이 가독성
3. Asset 간 크기 통일
4. Road / Tile 연결 정확성
5. Unity 재사용성
6. Random City Generator 호환
7. 파일 및 폴더 관리
8. 불필요한 Asset 수 감소