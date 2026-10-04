# CodeBlueRush 전체 건물·랜드마크 배치 가이드 — Maps.png 최우선 개정판

> 기준 프로젝트: Unity 6000.3.23f1 / 2D / WebGL / 모바일 / 480×854
>
> 최우선 참고 이미지: `Assets/Image/Maps/Reference/Maps.png`
>
> **설계 우선순위: Maps.png의 블록 구성과 랜드마크 위치 → 도로/보도 침범 방지 → 구역별 시각적 역할 → 재사용/최적화**
>
> 최종 웹사이트 100 MB 제한은 유지한다. 단, 용량 최적화를 이유로 Maps.png의 핵심 랜드마크, 주차장, 광장, 항만, 공원 구조를 삭제하지 않는다.

---

# 1. 문서 목적

이 문서는 `Maps.png`의 도로망이 만든 블록 안에 건물, 주차장, 광장, 녹지, 시설을 배치하기 위한 기준이다.

핵심 원칙:

```text
Maps.png 도로/보도 구조 확정
↓
블록 크기 확정
↓
랜드마크 위치 확정
↓
일반 건물 배치
↓
주차장/광장/녹지 배치
↓
소품 배치
```

건물을 맞추기 위해 도로를 이동하지 않는다.

---

# 2. 다른 문서와의 책임 분리

| 문서 | 책임 |
| --- | --- |
| `CodeBlueRush_CityRoadDesignGuide.md` | 도로, 교차로, 다리, Sidewalk, Crosswalk, 하천/해안과의 연결 |
| `CodeBlueRush_BuildingPlacementGuide.md` | 블록 내부 건물, 랜드마크, 주차장, 광장, 녹지 배치 |
| `CodeBlueRush_MapAssetStyleGuide.md` | 모든 리소스의 색감, 시점, 명암, 형태, 이미지 제작 기준 |

충돌 시 `Maps.png`를 먼저 본다.

---

# 3. Maps.png 기준 도시 구성

기존의 6개 기능 구역은 큰 분류로 유지할 수 있다.

다만 실제 `Maps.png`에는 구역 사이에 혼합 블록이 있으므로 **정확히 6개의 사각형 구역으로 강제 분할하지 않는다.**

Maps.png를 다음처럼 읽는다.

| 영역 | 대략 위치 | 주요 구성 |
| --- | --- | --- |
| 주거 블록 | 좌측 상단 | 단독주택, 중층 주거/업무형 건물, 작은 공원, 분수, 조경 |
| 상업·업무 블록 | 중앙 상단 | 중층 업무/상업 건물, 작은 상가, 광장, 분수, 조경 |
| 병원 캠퍼스 | 우측 상단 | 대형 병원 2개 축, 보조동, 대형 주차장, 원형 회차부, 녹지 |
| 산업·물류 | 좌측 중앙 | 창고, 물류시설, 주차/적재장, 트럭 공간 |
| 항만 | 좌측 하단 | 부두, 항만 수역, 컨테이너, 크레인, 선박, 탱크/사일로 |
| 하천·공원 축 | 중앙 세로 | 하천, 돌 경계, 산책로, 나무, 작은 섬/정자, 다리 |
| 혼합 업무·공공 블록 | 우측 중앙 | 중층 건물, 주차장, 광장/분수, 녹지 |
| 소방서 구역 | 우측 하단 | 소방서 본관, 차고, 타워, 넓은 출동 야드, 응급차량 |
| 외곽 녹지 Belt | 도시 외곽 | 조밀한 수목, 바위, 꽃나무, 경계 녹지 |

---

# 4. Maps.png에서 반드시 유지할 랜드마크

## 4.1 병원 캠퍼스

Maps.png 우측 상단의 가장 강한 랜드마크다.

구성:

- 대형 병원 본관
- 두 번째 대형 의료동
- 소형 보조동
- 넓은 주차장
- 원형 회차부
- 차량 진입 공간
- 녹지와 나무
- 빨간 의료 마크

병원은 단일 건물 1개로 끝내지 않고 **캠퍼스 세트**로 본다.

---

## 4.2 산업·항만

좌측 하단은 두 부분으로 나눈다.

### 산업·물류 블록

- 창고
- 물류센터
- 작업장
- 트럭/화물차 공간
- 주차장
- 적재장
- 탱크/사일로

### 항만 블록

- 항만 수역
- 부두
- 컨테이너 적재 구역
- 컨테이너 크레인
- 선박
- 항만 작업 공간

항만은 단순한 “창고 지역”으로 축소하지 않는다.

---

## 4.3 하천 공원 축

기존의 `호수 중앙 섬` 표현보다 Maps.png에 맞게 **하천 공원**으로 정의한다.

구성:

- 남북으로 이어지는 하천
- 굴곡진 물 경계
- 돌/바위 제방
- 잔디
- 조밀한 나무
- 밝은 산책로
- 작은 섬 또는 돌출 녹지
- 정자/파빌리온
- 여러 다리

큰 호수 하나를 만드는 구조가 아니다.

---

## 4.4 소방서

Maps.png 우측 하단의 대표 랜드마크다.

구성:

- 긴 본관
- 여러 개의 차고 문
- 중앙 또는 후면 타워
- 넓은 전면 출동 야드
- 소방차/구급차 주차
- 시설 경계 녹지

소방서 앞 공간을 일반 상업 블록처럼 건물로 채우지 않는다.

---

## 4.5 분수 광장

Maps.png에는 여러 블록에서 분수/광장이 시각적 반복 요소로 사용된다.

따라서 공용 리소스로 준비한다.

- `Plaza_Base`
- `Fountain_Small`
- `Fountain_Medium`
- `Plaza_Path`
- `Plaza_Planting`

분수마다 완전히 다른 대형 Texture를 만들 필요는 없다.

---

# 5. 블록 단위 배치 원칙

모든 건물은 도로가 만든 블록 안에 배치한다.

```text
도로
→ Sidewalk
→ 녹지/Setback
→ 건물/주차장/광장
```

금지:

- 건물 Sprite가 Sidewalk 위로 침범
- 건물 Collider가 도로 위로 침범
- Maps.png의 블록 경계를 무시하고 건물을 크게 확장
- 랜드마크를 넣기 위해 도로를 이동

---

# 6. 건물 크기 기준

기존 Small / Medium / Large / Landmark 분류는 유지한다.

다만 Unity Unit 수치는 **시작값**일 뿐, Maps.png의 블록 비율보다 우선하지 않는다.

| 등급 | 초기 배치 기준 | 용도 |
| --- | ---: | --- |
| Small | 약 5×5 Unit | 단독주택, 작은 상점, 관리소 |
| Medium | 약 9×7 Unit | 빌라, 중형 상가, 창고, 소형 업무동 |
| Large | 약 15×11 Unit | 아파트, 대형 업무동, 공장 |
| Landmark | 전용 | 병원, 소방서, 항만 크레인/부두 세트 |

Maps.png와 비율이 다르면 건물 크기를 조정하고 도로는 유지한다.

---

# 7. 도로와 건물 사이 거리

기존의 `주거 3 Unit`, `상업 2 Unit` 같은 고정 숫자를 절대값으로 사용하지 않는다.

Maps.png에서는 같은 구역 안에서도:

- 작은 앞마당
- 넓은 녹지
- 주차장
- 광장
- 시설 야드

에 따라 이격 거리가 달라진다.

따라서 기준은 다음과 같다.

```text
도로 끝선
→ Sidewalk
→ 필요한 녹지/주차/광장
→ 건물 Collider
```

필수:

- Sidewalk 최소 폭 유지
- 건물과 도로 사이 시각적 여유 유지
- 병원/소방서/항만은 일반 건물보다 넓은 진입 공간 확보

---

# 8. 주거 블록 배치

Maps.png 좌측 상단을 기준으로 한다.

특징:

- 단독주택과 중층 건물이 혼합됨
- 지붕색이 다양함
- 건물 사이 녹지 비율이 높음
- 작은 내부 공원/분수 공간 존재
- 나무와 꽃나무가 많음
- 도로와 건물 사이에 보도/녹지 완충이 있음

필수 기본 리소스:

- House_A
- House_B
- House_C
- ResidentialMidrise_A
- ResidentialMidrise_B
- SmallApartment_A

다양성:

- 지붕 Tint
- 90° 회전
- 좌우 반전
- 정원/나무 조합

한 블록을 동일한 건물만 반복해서 채우지 않는다.

---

# 9. 상업·업무 블록 배치

Maps.png 중앙 상단과 우측 중앙 일부를 기준으로 한다.

특징:

- 흰색/회색 본체
- 푸른 유리 면
- 중층 건물 중심
- 건물 사이 조경
- 작은 광장과 분수
- 주차장이 간헐적으로 포함

필수 기본 리소스:

- Office_A
- Office_B
- Commercial_A
- Commercial_B
- SmallShop_Base
- CivicMidrise_A

간판은 Overlay를 우선한다.

- Convenience_Sign
- Pharmacy_Sign
- Cafe_Sign
- Restaurant_Sign

단, Maps.png에서 간판 글자가 핵심 시각 요소는 아니므로 너무 크게 만들지 않는다.

---

# 10. 병원 캠퍼스 배치

병원은 일반 건물 배치 규칙보다 `Maps.png`의 캠퍼스 구성을 우선한다.

권장 Hierarchy:

```text
HospitalCampus
├─ Hospital_Main
├─ Hospital_Secondary
├─ Hospital_Support
├─ EmergencyEntrance
├─ Roundabout
├─ Parking_A
├─ Parking_B
└─ Landscaping
```

규칙:

- 큰 본관이 시각적으로 가장 강해야 함
- 두 번째 의료동도 독립적으로 읽혀야 함
- 주차장을 충분히 확보
- 원형 회차부와 건물 출입부가 연결
- 조경으로 캠퍼스 경계를 정리
- 도로 쪽 출입 공간을 건물로 막지 않음

---

# 11. 산업·물류 블록 배치

Maps.png 좌측 중앙을 기준으로 한다.

필수 리소스:

- Warehouse_A
- Warehouse_B
- LogisticsCenter
- Workshop
- Tank
- Silo
- LoadingYard
- TruckParking

성격:

- 건물 크기가 큼
- 건물 사이 빈 작업 공간이 큼
- 주차/적재 공간 비중이 높음
- 장식보다 기능적인 배치가 우선

---

# 12. 항만 배치

필수 리소스:

- Dock_Straight
- Dock_Corner
- ContainerCrane
- Container_Base
- CargoShip
- SmallServiceBoat
- HarborBarrier
- PortYard

규칙:

- 부두가 물과 육지를 자연스럽게 연결
- 컨테이너는 소수 기본 Sprite + Tint 재사용
- 크레인은 큰 랜드마크로 유지
- 선박이 수역 안에서 잘리지 않음
- 항만 수역을 일반 파란 Ground처럼 처리하지 않음

---

# 13. 하천·공원 배치

건물 수를 최소화한다.

사용 리소스:

- Pavilion_A
- ParkShelter
- ParkPath
- Bench
- Tree / Bush / Flower
- Rock_Edge
- SmallIsland

핵심:

- 자연 지형이 주인공
- 건물은 보조
- 산책로는 굽어도 됨
- 공공 도로와 ParkPath를 혼동하지 않음
- 정자/파빌리온은 너무 많이 반복하지 않음

---

# 14. 혼합 업무·공공 블록

Maps.png 우측 중앙에는 병원과 소방서 사이에 별도 중형 블록들이 있다.

구성:

- 중층 업무 건물
- 주차장
- 분수 광장
- 조경
- 서비스 차량 공간

이 영역을 병원이나 소방서 구역으로 억지로 합치지 않는다.

상업·업무 공용 Sprite를 재사용하되 배치 밀도로 차이를 만든다.

---

# 15. 소방서 배치

권장 Hierarchy:

```text
FireStationCampus
├─ FireStation_Main
├─ FireStation_Tower
├─ GarageDoors
├─ DispatchYard
├─ EmergencyVehicleParking
└─ Landscaping
```

규칙:

- 본관은 Maps.png처럼 가로로 넓은 실루엣
- 차고 문이 여러 개 보임
- 전면에 넓은 빈 출동 야드 확보
- 소방차/구급차 배치 가능
- 출동 방향을 나무/장식으로 막지 않음

---

# 16. 주차장 규칙

Maps.png에서 주차장은 중요한 블록 구성 요소다.

주차장을 단순 소품으로 취급하지 않는다.

기본 모듈:

- Parking_Asphalt
- ParkingLine
- ParkingCar
- ParkingTreeIsland
- ParkingEntrance

사용 위치:

- 병원
- 산업/물류
- 혼합 업무 블록
- 소방서

규칙:

- 주차 선 방향 통일
- 차량 크기 일관
- 진입부 확보
- 나무섬/녹지섬을 필요에 따라 반복

---

# 17. 녹지와 외곽 수목 Belt

Maps.png의 완성도는 건물 수보다 녹지 밀도에서 크게 나온다.

특히:

- 도시 외곽
- 하천 양쪽
- 병원 캠퍼스
- 주거 블록
- 공원
- 소방서 경계

에 조밀한 수목을 사용한다.

기본 리소스:

- Tree_Small
- Tree_Medium
- Tree_Large
- Bush
- Hedge
- Flower
- FlowerTree

회전/Scale/Tint로 반복감을 줄인다.

---

# 18. 건물 텍스처 제작 원칙

- 한 건물은 가능한 Color Sprite 1장
- Normal Map 기본 사용 안 함
- Mask Map 기본 사용 안 함
- 별도 AO 기본 사용 안 함
- 그림자는 Sprite에 Bake
- 큰 투명 여백 금지
- 방향별 중복 PNG 금지

건물 다양성 우선순위:

```text
1. 배치 위치
2. 회전
3. 좌우 반전
4. Color Tint
5. 간판 Overlay
6. 주변 나무/주차장/광장 조합
7. 그래도 부족할 때 신규 Sprite
```

---

# 19. 권장 고유 리소스 수

고유 Texture 수는 목표값이지 Maps.png 재현보다 우선하는 하드 제한이 아니다.

권장 1차 목표:

- 일반 건물 본체: 약 20~28개
- 랜드마크 세트: 병원 / 항만 / 소방서 중심
- 작은 Overlay: 64~128 px 중심
- 주차/광장/녹지 모듈: 적극 재사용

Maps.png의 중요한 실루엣이 사라질 정도로 종류를 줄이지 않는다.

---

# 20. Sprite 해상도

모바일 화면 기준 권장:

- Small: 128~256 px
- Medium: 256 px 중심
- Large: 256~512 px
- Landmark: 512 px 중심
- 1024 px: 512 px에서 실제 품질 부족이 확인될 때만

2048 / 4096 px 단일 건물은 기본적으로 사용하지 않는다.

---

# 21. Sprite Atlas

권장:

- `BuildingCommonAtlas`
- `BuildingLandmarkAtlas`
- `IndustrialPortAtlas`
- `VegetationAtlas`
- `PropAtlas`

한 Sprite를 여러 Atlas에 중복 포함하지 않는다.

---

# 22. GameObject 구조

일반 정적 건물:

```text
Transform
SpriteRenderer
필요 시 단순 Collider2D
```

규칙:

- 건물마다 MonoBehaviour 추가 금지
- 건물마다 Update 금지
- 동일 계열 Material 공유
- Collider는 BoxCollider2D 우선
- 시각적 돌출부를 Collider로 완벽하게 따라갈 필요 없음

---

# 23. Unity 권장 Hierarchy

```text
City
├─ Buildings
│  ├─ Residential
│  ├─ CommercialOffice
│  ├─ HospitalCampus
│  ├─ Industrial
│  ├─ Port
│  ├─ MixedCivic
│  └─ FireStationCampus
├─ Parking
├─ Plazas
├─ ParkStructures
├─ Vegetation
└─ StaticProps
```

랜드마크는 개별 Root로 관리해 배치 수정이 쉽도록 한다.

---

# 24. 건물 배치 작업 순서

도로/보도 파트가 Maps.png와 대조 QA를 통과한 후 진행한다.

```text
1. 병원/항만/소방서 랜드마크 위치 고정
↓
2. 주거 블록 큰 실루엣 배치
↓
3. 상업·업무 블록 배치
↓
4. 산업·물류 배치
↓
5. 혼합 업무·공공 블록 배치
↓
6. 주차장/광장 배치
↓
7. 하천 공원 구조물 배치
↓
8. 녹지/수목 배치
↓
9. 작은 소품 배치
↓
10. Maps.png 대조 QA
```

---

# 25. Maps.png 대조 QA

## 블록

- [ ] 건물이 도로 블록 안에 들어가 있는가
- [ ] 블록 크기와 건물 밀도가 Maps.png와 비슷한가
- [ ] 건물 때문에 도로/Sidewalk가 변경되지 않았는가

## 랜드마크

- [ ] 우측 상단 병원 캠퍼스가 즉시 보이는가
- [ ] 좌측 하단 항만 크레인/부두가 즉시 보이는가
- [ ] 중앙 하천 공원의 자연 비율이 유지되는가
- [ ] 우측 하단 소방서가 즉시 보이는가

## 보조 공간

- [ ] 병원 주차장과 원형 회차부가 있음
- [ ] 산업지역에 적재/주차 빈 공간이 있음
- [ ] 분수 광장이 적절한 블록에 있음
- [ ] 외곽 수목 Belt가 유지됨

---

# 26. 최적화 우선순위

Maps.png 구조가 맞는 상태에서 다음 순서로 줄인다.

```text
1. 동일 건물 회전/반전
2. Tint 사용
3. 간판 Overlay 재사용
4. 주차/광장 모듈 재사용
5. Texture Max Size 조정
6. Atlas
7. Web 압축
8. 작은 장식 축소
```

삭제 우선순위는 작은 장식부터다.

병원/항만/소방서/하천과 같은 큰 구조를 먼저 삭제하지 않는다.

---

# 27. WebGL Gate

최종 웹사이트 배포 크기는 100 MB를 초과하지 않는다.

현재 빌드 크기를 문서 안에 고정 숫자로 저장하지 않는다.

작업 시점의 Release Build를 기준으로 확인한다.

필요하면:

- 해상도 감소
- 중복 Sprite 통합
- Tint/Overlay 전환
- 사용하지 않는 Variation 삭제
- 작은 소품 축소

순서로 조정한다.

---

# 28. 완료 조건

- [ ] Maps.png의 주요 블록 구성이 유지됨
- [ ] 병원은 단일 건물이 아니라 캠퍼스로 재현됨
- [ ] 산업과 항만이 구분되어 표현됨
- [ ] 하천 공원은 호수가 아니라 남북 하천 축으로 표현됨
- [ ] 소방서 전면 출동 야드가 확보됨
- [ ] 주차장/광장이 블록 구조의 일부로 구현됨
- [ ] 외곽 수목 Belt가 유지됨
- [ ] 도로와 Sidewalk를 침범하는 건물이 없음
- [ ] 건물 리소스가 회전/Tint/Overlay로 재사용됨
- [ ] 모바일 화면에서 각 랜드마크가 쉽게 구분됨
- [ ] 최종 배포 100 MB Gate를 통과함

---

# 29. 최종 판단 원칙

```text
Maps.png에서 큰 형태가 보이는가?
→ 반드시 유지한다.

Maps.png에서 반복되는 작은 건물인가?
→ 공용 Sprite로 재사용한다.

용량이 부족한가?
→ 작은 Variation과 디테일부터 줄인다.

도로와 충돌하는가?
→ 건물을 조정하고 도로는 유지한다.
```

**건물 배치의 최종 기준은 `Maps.png`의 블록 크기, 랜드마크 위치, 빈 공간 비율이다.**
