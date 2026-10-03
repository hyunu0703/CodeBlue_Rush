# CodeBlueRush Map Asset Style Guide

## 1. 문서 목적

이 문서는 CodeBlueRush의 전체 맵 리소스를 동일한 스타일과 기술 규격으로 제작하기 위한 기준이다.

대상:

- 이미지 생성
- Unity Sprite Import
- Prefab 제작
- 맵 조립
- City Generator 적용
- Asset 품질 검사

`CodeBlueRush_MapAssetProductionGuide.md`가
**무엇을 제작할지** 정의한다면,

이 문서는
**어떤 모습과 규격으로 제작할지** 정의한다.

---

# 2. 프로젝트 환경

- Unity: 6000.3.23f1
- Language: C#
- Game Type: 2D Top-Down
- Platform: WebGL
- Screen: Portrait 480 × 854
- Player: Single Player

---

# 3. Master Style Reference

CodeBlueRush의 모든 맵 Asset은 다음 이미지를
유일한 최종 Master Style Reference로 사용한다.

`Assets/Images/Maps/Reference/CodeBlueRush_Map_MasterReference.png`

Reference 원본 크기:

`941 × 1672 px`

이 이미지는 실제 게임 Sprite가 아니라
**시각적 디자인 기준 이미지**이다.

따라서 Reference 자체의:

- Pixels Per Unit
- Sprite Mode
- Compression
- Max Size
- Pivot

등의 Unity Import 설정을
게임용 Asset에 그대로 복사하지 않는다.

새로운 Asset을 생성하기 전에 반드시
Master Reference 이미지를 직접 확인한다.

확인 대상:

- 카메라 시점
- Low Poly 면 표현
- 형태 단순화 정도
- 색상
- 밝기
- 채도
- 명암
- 광원
- 그림자
- 건물 크기 관계
- 도로 크기 관계
- 차량 크기 관계
- 나무 형태
- 물 표현
- 암석 표현
- 도시 밀도

시각적 설명과 Reference가 충돌하는 경우:

**Master Reference를 우선한다.**

단:

- Unity 기술 규격
- 기존 Road System
- 기존 City Generator
- 기존 게임플레이 시스템

과 충돌하면 기존 프로젝트 구조를 우선한다.

Master Reference 자체는 수정하거나 다시 생성하지 않는다.

---

# 4. 카메라 / 시점

## 기본 시점

CodeBlueRush는:

**2D Top-Down / Near-Orthographic**

시점을 사용한다.

도로와 차량 이동 구조는
위에서 내려다보는 탑뷰를 기준으로 한다.

Master Reference처럼 다음 요소의 얕은 높이 표현은 허용한다.

- 건물 높이
- 건물의 짧은 측면
- 나무의 입체 면
- 차량의 얕은 높이
- 바위의 높낮이
- 난간
- 연석

즉 완전히 평평한 수직 Sprite보다
Master Reference의 얕은 입체감을 우선한다.

## 금지

- 강한 Isometric 45도 시점
- 3점 투시
- 명확한 소실점
- 강한 원근감
- 가까운 물체와 먼 물체의 큰 크기 차이
- 지나친 건물 측면 노출
- 카테고리마다 다른 카메라 각도

최종 판단 기준은 항상:

`CodeBlueRush_Map_MasterReference.png`

이다.

---

# 5. 전체 스타일

## 고정 스타일

- 밝고 선명한 Casual Low Poly
- 단순화된 형태
- 명확한 실루엣
- 큰 색상 면
- 면 단위 명암
- 밝은 모바일 게임 스타일
- 높은 시각적 가독성

## 금지

- 실제 사진 느낌
- PBR Texture
- 사실적인 표면 노이즈
- 과도한 재질 디테일
- Pixel Art
- Hand Painted RPG 스타일
- 서로 다른 Low Poly 스타일 혼합

---

# 6. 디테일 기준

480 × 854 모바일 화면에서
실제로 알아볼 수 있는 디테일을 우선한다.

## 표현해야 하는 요소

- 큰 창문
- 지붕 형태
- 건물 입구
- 차량 색상
- 큰 간판 영역
- 나무의 큰 면
- 도로 차선
- 횡단보도
- 명확한 실루엣

## 줄여야 하는 요소

- 작은 볼트
- 작은 벽돌 하나하나
- 매우 작은 문자
- 표면 스크래치
- 작은 기계 부품
- 지나치게 복잡한 텍스처

---

# 7. 색상

Master Reference의 색상 분위기를 유지한다.

전체 방향:

- 밝음
- 선명함
- 높은 가독성
- 적당히 높은 채도
- 분명한 색 대비

금지:

- 지나친 Neon
- 전체적으로 회색빛인 색조
- 매우 어두운 색상
- 사실적인 탁한 색상

## 주요 색상 방향

### 도로

- Dark Gray
- Blue Gray 일부 허용

### 인도

- Light Gray
- Beige Gray

### 잔디

- Bright Green
- Medium Green

### 나무

- Light Green
- Medium Green
- Dark Green

### 물

- Clear Blue
- Cyan Blue
- Deep Blue 일부

### 병원

- White
- Blue

### 구급대

- White
- Red

### 주택

벽:

- White
- Beige
- Light Gray

지붕:

- Red
- Blue
- Gray

### 산업시설

- Blue
- Gray
- White

---

# 8. 색상 Variant

같은 종류의 Asset은
완전히 다른 색상 스타일을 만들지 않는다.

예:

```text
Tree_01
Tree_02
Tree_03
```

형태나 밝기는 조금 다를 수 있지만
모두 같은 도시의 나무처럼 보여야 한다.

차량 역시:

```text
Vehicle_Car_Red
Vehicle_Car_Blue
Vehicle_Car_Yellow
```

색상만 달라지고
기본 형태와 렌더링 스타일은 동일하게 유지한다.

---

# 9. 광원

모든 Asset의 광원 방향을 통일한다.

기준:

**왼쪽 위 → 오른쪽 아래**

즉:

- 왼쪽 위 면 = 상대적으로 밝음
- 오른쪽 아래 면 = 상대적으로 어두움
- 그림자 = 오른쪽 아래 방향

Master Reference의 특정 표현이 문서와 다르면
Master Reference를 우선한다.

리소스마다 광원 방향을 변경하지 않는다.

---

# 10. 그림자

높이가 있는 오브젝트에는 간단한 접지 그림자를 사용할 수 있다.

대상:

- 건물
- 차량
- 나무
- 바위
- 일부 큰 소품

그림자 기준:

- 짧음
- 단순함
- 반투명
- 부드러운 가장자리
- 오른쪽 아래 방향

## 금지

- 매우 긴 그림자
- 완전한 검정 그림자
- 사실적인 복잡한 그림자
- Asset마다 다른 그림자 방향

## 기본 방식

기본적으로:

**오브젝트 Sprite에 간단한 그림자 포함**

을 사용한다.

특별히 독립 제어가 필요한 경우에만
Shadow Sprite를 분리한다.

---

# 11. 투명 배경

독립 오브젝트는:

**Transparent PNG**

로 제작한다.

대상:

- Building
- Tree
- Vehicle
- Citizen
- Prop
- Rock
- Boat

배경에 넣지 않는 요소:

- 흰색 배경
- 검은색 배경
- 하늘
- 임의의 잔디
- 임의의 도로
- 임의의 건물

## 예외

바닥 자체가 Asset인 경우에는 투명 배경이 필수가 아니다.

예:

- Road
- Grass
- Sidewalk
- Parking
- Water
- Ground

---

# 12. Sprite Padding

독립 Sprite는 이미지 경계에 붙지 않도록 한다.

권장:

**이미지 전체 크기의 5 ~ 10%**

정도의 안전 여백.

예:

256 × 256 px라면:

약 12 ~ 24 px 정도.

단:

- Road
- Ground
- Water
- Tile

처럼 정확한 연결이 필요한 Asset은
연결 방향에 Padding을 넣지 않는다.

---

# 13. 기준 방향

방향성이 있는 Asset은 기본 방향을 통일한다.

## 차량

기본 방향:

**North**

가능하면 하나의 Sprite를 Unity Transform 회전으로 재사용한다.

별도 방향 Sprite는 다음 경우에만 제작한다.

- 그림자 방향 문제가 있는 경우
- 비대칭 디자인
- 문자 / 마킹 방향 문제
- 회전 시 시각적으로 어색한 경우

## 건물

기본 입구 방향:

**South**

필요한 경우:

- North
- East
- South
- West

Variant를 제작한다.

## 시민

4방향 기준:

- North
- South
- East
- West

---

# 14. Sprite 기본 제작 해상도

아래 값은 **권장 제작 해상도**이다.

실제 Unity 월드 크기와 기존 Prefab 규격을 먼저 확인한다.

## Ground / Road

권장:

`512 × 512 px`

## 대형 교차로

권장:

`1024 × 1024 px`

## 나무 / 소형 Prop

권장:

`256 × 256 px`

## 차량

권장:

`256 × 256 px`

차량이 이미지 전체를 꽉 채우지 않도록 Padding을 유지한다.

## 시민

권장:

`128 × 128 px`

또는:

`256 × 256 px`

## 소형 건물

권장:

`512 × 512 px`

## 중형 건물

권장:

`768 × 768 px`

또는:

`1024 × 1024 px`

## 대형 건물

병원 / 구급대 / 슈퍼마켓:

권장:

`1024 × 1024 px`

필요하면 더 큰 원본으로 생성한 뒤 Unity에서 축소한다.

---

# 15. 도로 규격

도로는 가장 먼저 기준 규격을 확정해야 한다.

같은 종류의 도로는 다음 요소가 모두 동일해야 한다.

- 전체 도로 폭
- 차선 폭
- 중앙선 위치
- 연결 위치
- Road Center
- 인도 연결 위치

## 기준 타일

권장:

`512 × 512 px`

단:

현재 CodeBlueRush의 기존 Road Prefab 규격이 다르면:

**기존 Road Prefab의 실제 크기를 우선한다.**

---

# 16. 도로 연결 규칙

다음 Road Asset은 동일한 연결 규격을 공유한다.

```text
Road_Straight
      ↓
Road_Corner
      ↓
Road_TJunction
      ↓
Road_Intersection
```

모든 연결 지점의:

- 도로 폭
- 중심 위치
- 차선 위치

가 동일해야 한다.

## Road 끝부분

도로 Sprite 연결 부분에는 다음 요소를 넣지 않는다.

- 나무
- 차량
- 건물
- 화단
- 가로등
- 기타 장식

연결을 방해하는 요소를 제거한다.

---

# 17. 차선

차선 규격은 도로 전체에서 일정하게 유지한다.

통일 대상:

- 중앙선 굵기
- 흰색 차선 굵기
- 차선 간격
- 정지선 굵기
- 화살표 크기
- 횡단보도 간격

도로마다 임의로 크기를 변경하지 않는다.

---

# 18. Road Marking 분리

가능하면 다음 요소를 Road 본체와 분리하여
Sprite Overlay로 제작한다.

- 진행 방향 화살표
- 정지선
- 주차선
- 특수 도로 표시
- 버스 표시
- 일부 횡단보도

장점:

- 같은 Road 재사용 가능
- Prefab 수 감소
- Random Map 조립 용이

단:

교차로처럼 항상 같은 표시가 필요한 경우에는
Road Sprite에 포함할 수 있다.

---

# 19. 인도

인도도 Road와 연결되는 규격을 통일한다.

통일 대상:

- 인도 폭
- 연석 폭
- 코너 반경
- 색상
- 바닥 패턴
- 도로와의 경계

Road와 Sidewalk 사이에 틈이 생기지 않아야 한다.

---

# 20. 건물 규격

건물은 독립 Sprite로 제작한다.

건물 Sprite에는 기본적으로:

- 건물 본체
- 지붕
- 고정된 건물 장식
- 간단한 접지 그림자

만 포함한다.

가능하면 다음 요소는 분리한다.

- 주차장
- 차량
- 나무
- 벤치
- 가로등
- 도로
- 대형 화단
- 이동 가능한 오브젝트

이를 통해 같은 건물을 여러 위치에서 재사용한다.

---

# 21. 건물 디자인

Master Reference의 형태를 기준으로 한다.

## 병원

특징:

- White
- Blue
- 큰 건물
- 의료시설 느낌
- Rooftop Helipad 가능
- Emergency Entrance
- 명확한 출입구

## 구급대

특징:

- White
- Red
- Ambulance Garage
- 병원과 명확하게 구분

## 상업시설

- 밝은 색상
- 명확한 입구
- 서로 다른 지붕 형태
- 간단한 브랜드 느낌
- 테라스 사용 가능

실제 브랜드 로고는 복제하지 않는다.

## 주택

다양성은 다음 요소를 중심으로 만든다.

- 지붕 색상
- 건물 크기
- 마당
- 울타리
- 작은 수영장

전체 스타일은 바꾸지 않는다.

---

# 22. 텍스트 / 간판

Master Reference의 간판은
건물의 용도와 색상 디자인을 판단하기 위한 참고 요소이다.

게임용 Asset 생성 시 정확한 긴 텍스트를
이미지에 직접 생성하는 것은 피한다.

권장:

- H
- 의료 십자가
- Pharmacy Symbol
- Emergency Symbol
- Parking Symbol
- 간단한 아이콘
- 빈 간판 영역

정확한 건물명이나 긴 문자는 가능한 경우:

- Unity UI
- Text
- 별도 Overlay Sprite

로 처리한다.

Master Reference의 간판 색상과 위치감은 유지할 수 있다.

---

# 23. 식생

나무는 Master Reference의 Low Poly 형태를 따른다.

## 기본 구조

- 둥근 실루엣
- Polygon-like 면
- 밝은 초록
- 중간 초록
- 어두운 초록

약 2 ~ 4단계의 큰 색상 면으로 표현한다.

## 반복 방지

같은 나무 반복 배치 시:

- Scale 약간 변경
- Rotation 변경
- 2 ~ 4개 Variant 사용

으로 반복감을 줄인다.

---

# 24. 차량

차량은 Road보다 확실히 작아야 한다.

크기 관계:

```text
일반 승용차
<
밴
<
구급차 / 소형 트럭
<
버스 / 대형 트럭
```

차량마다 비율을 임의로 변경하지 않는다.

플레이어 구급차는 일반 차량보다
시각적으로 쉽게 구분되어야 한다.

---

# 25. 시민

시민은 모바일 화면에서도 구별 가능한
단순한 실루엣으로 제작한다.

과도한 얼굴 표현은 하지 않는다.

구분 요소:

- 머리 색
- 상의 색
- 하의 색
- 체형
- 작은 액세서리

환경 Asset보다 지나치게 튀지 않도록 한다.

---

# 26. Asset Naming Convention

파일 이름은 영어를 사용한다.

기본 형식:

`Category_Type_Variant.png`

예:

```text
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
```

금지:

- 공백
- 한글 파일명
- 의미 없는 숫자만 사용
- 같은 이름 중복

---

# 27. 권장 폴더 구조

```text
Assets/
└── Images/
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
```

필요 이상으로 폴더를 세분화하지 않는다.

---

# 28. Unity Import 기본 설정

다음 설정은 권장 기본값이다.

## Texture Type

`Sprite (2D and UI)`

## Sprite Mode

독립 이미지:

`Single`

Sprite Sheet:

`Multiple`

## Pixels Per Unit

현재 프로젝트의:

- Ambulance
- Road Prefab
- 기존 City Prefab

규격을 확인한 뒤 확정한다.

맵 Asset은 동일한 기준 PPU를 사용하는 것을 우선한다.

Sprite마다 임의의 PPU를 사용하지 않는다.

## Mesh Type

권장:

`Full Rect`

특히:

- Tile
- Road
- Ground
- Water

계열은 Full Rect를 사용한다.

## Filter Mode

Master Reference의 부드러운 Low Poly 표현을 위해:

`Bilinear`

권장.

Pixel Art용 Point Filter는 사용하지 않는다.

## Compression

원본 제작 및 확인 단계:

`None`

권장.

WebGL Build 크기가 문제가 될 경우:

- Unity Profiler
- Build Report

확인 후 압축 설정을 조정한다.

---

# 29. Pivot

## Ground / Road / Tile

`Center`

## 차량

`Center`

## 나무 / 소품

기본:

`Center`

배치 기준이 바닥 접점이어야 한다면:

`Bottom Center`

검토 가능.

## 건물

기본:

`Center`

단:

기존 City Generator Prefab 기준점이 있다면
기존 Prefab 설정을 우선한다.

---

# 30. Sprite 회전

같은 형태를 Unity Transform 회전으로 재사용할 수 있다면
불필요한 Sprite를 추가 생성하지 않는다.

예:

```text
Road_Straight
```

하나를 회전하여:

- Horizontal
- Vertical

로 사용할 수 있다면 별도 이미지를 만들지 않는다.

단 다음 문제가 있으면 별도 Sprite를 사용한다.

- 그림자 방향
- 텍스트
- 비대칭 구조
- 마킹 방향
- 회전 시 시각적 왜곡

---

# 31. Random City Generator 고려

CodeBlueRush Asset은
기존 Random City Generator에서 조립할 수 있어야 한다.

따라서:

- Road 연결 규격 통일
- Building 독립 배치
- Decoration 분리
- Sidewalk 분리
- Parking 분리
- Vehicle 분리
- Citizen 분리
- 불필요한 주변 요소 제거

를 우선한다.

한 Sprite 안에 지나치게 많은 요소를 합치지 않는다.

---

# 32. Collider 기준

이미지 제작 단계에서는 Collider를 이미지 형태에 억지로 맞추지 않는다.

Collider는 Unity Prefab 단계에서 설정한다.

예:

```text
Building Sprite
↓
Building Prefab
↓
Collider2D
```

Road / Decoration / Building 이미지는
Collider 때문에 시각 디자인을 변경하지 않는다.

---

# 33. Sorting Layer 고려

권장 렌더링 순서:

```text
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
```

실제 프로젝트에 이미 Sorting Layer가 존재한다면
기존 설정을 우선한다.

---

# 34. 제작 단계

새 카테고리를 제작할 때:

```text
Master Reference 확인
↓
Production Guide 확인
↓
Style Guide 확인
↓
기존 동일 카테고리 Asset 확인
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
Secondary Reference 지정
↓
같은 카테고리 Variant 제작
```

처음부터 카테고리 전체를 대량 생성하지 않는다.

---

# 35. Master Reference 확인 절차

Codex가 새로운 Map Asset을 제작할 경우
다음 순서를 반드시 따른다.

```text
Md/CodeBlueRush_MapAssetProductionGuide.md 확인
↓
Md/CodeBlueRush_MapAssetStyleGuide.md 확인
↓
Assets/Images/Maps/Reference/CodeBlueRush_Map_MasterReference.png 직접 확인
↓
기존 동일 카테고리 Asset 확인
↓
대표 Asset 1개 생성
↓
Master Reference와 비교
↓
통과한 경우에만 Variant 제작
```

Master Reference를 확인하지 않고
텍스트 설명만으로 스타일을 추측하지 않는다.

Master Reference 자체는:

- 수정하지 않는다
- 재생성하지 않는다
- 덮어쓰지 않는다

---

# 36. 이미지 생성 작업 규칙

새로운 Asset 생성 시 반드시 다음 기준을 사용한다.

```text
CodeBlueRush_Map_MasterReference.png를
Master Style Reference로 사용한다.

2D Top-Down / Near-Orthographic 시점을 사용한다.

Master Reference와 동일한:

- 색상
- Low Poly 면 표현
- 광원
- 명암
- 그림자
- 카메라
- 형태 단순화 수준
- 전체적인 밝기

를 유지한다.

독립 오브젝트는 Transparent PNG로 제작한다.

Unity에서 독립 Sprite로 사용할 수 있도록
불필요한 주변 건물 / 도로 / 차량 / 나무를 포함하지 않는다.
```

---

# 37. 카테고리 내부 통일

같은 카테고리의 첫 번째 확정 Asset을
Secondary Reference로 사용한다.

예:

```text
Master Reference
+
Road_Straight_2Lane
↓
나머지 Road Asset 제작
```

또는:

```text
Master Reference
+
Tree_Medium_01
↓
나머지 Tree Asset 제작
```

이 방법으로 생성 과정에서 발생하는
스타일 변화를 최소화한다.

---

# 38. 이미지 생성 우선순위

권장 순서:

```text
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
```

이전 단계에서 확정된 Asset을
다음 단계의 추가 Reference로 활용한다.

---

# 39. 도시 도로 디자인 규칙

CodeBlueRush의 일반 도시 구역은
기본적으로 **지상 도로**를 사용한다.

도로 생성 시 불필요하게 다음 요소를 추가하지 않는다.

- 고가도로
- 대형 교량
- 복층 도로
- 입체교차
- 과도한 램프 구조

Master Reference의 도시 내부 일반 도로 형태를 우선한다.

항만이나 물을 실제로 건너야 하는 경우처럼
게임 구조상 필요한 경우에만 별도 교량 Asset을 검토한다.

---

# 40. 하지 말아야 할 것

금지:

- Master Reference 없이 이미지 생성
- Asset마다 다른 카메라 시점
- 강한 Isometric 변환
- Asset마다 다른 광원
- 지나치게 사실적인 Texture
- Pixel Art 스타일 혼합
- Asset마다 다른 Low Poly 강도
- 같은 Road인데 폭이 다른 Sprite
- Road 연결 위치 불일치
- 불필요한 배경 포함
- 불필요한 문자 생성
- 건물과 차량을 한 Sprite에 합치기
- 나무와 건물을 불필요하게 합치기
- 한 이미지에 전체 지역을 통째로 제작
- Unity에서 재사용할 수 없는 구조 제작
- Master Reference 덮어쓰기

---

# 41. Asset 완료 조건

각 Asset은 다음 조건을 모두 만족해야 한다.

## Visual

- Master Reference와 같은 스타일인가
- Top-Down 시점이 일치하는가
- Low Poly 표현이 동일한가
- 광원 방향이 동일한가
- 채도와 명암이 동일한가
- 다른 Asset과 함께 배치해도 자연스러운가
- 크기 관계가 자연스러운가

## Technical

- 필요한 경우 Transparent PNG인가
- Padding이 적절한가
- 파일명이 규칙에 맞는가
- 독립 Sprite로 사용할 수 있는가
- 불필요한 배경이 없는가

## Road

도로 Asset이라면 추가 확인한다.

- 다른 Road Tile과 폭이 동일한가
- 연결 위치가 동일한가
- 중앙선이 이어지는가
- 차선이 이어지는가
- 횡단보도와 연결되는가
- 인도와 연결되는가

## Unity

- Sprite Import가 정상인가
- Pivot이 적절한가
- PPU가 프로젝트 기준과 맞는가
- Full Rect가 필요한 Asset에 적용됐는가
- Scene에 배치했을 때 크기가 정상인가

---

# 42. 최종 기준

CodeBlueRush Map Asset 제작의 우선순위:

```text
1. Master Reference와의 시각적 통일
2. 게임플레이 가독성
3. Asset 간 크기 통일
4. Road / Tile 연결 정확성
5. Unity 재사용성
6. City Generator 호환
7. 파일 / 폴더 관리
8. 불필요한 Asset 수 감소
```

각 Asset을 독립적인 작품처럼 제작하지 않는다.

최종 목표는:

**모든 Asset을 Unity에서 조립했을 때
CodeBlueRush_Map_MasterReference.png와 같은 하나의 도시로 보이도록 하는 것**이다.