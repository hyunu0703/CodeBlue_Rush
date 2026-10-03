# CodeBlueRush WebGL Asset Optimization Guide

## 1. 문서 목적

이 문서는 CodeBlueRush의 이미지 Asset을 제작하고 Unity에 적용할 때
GamePlay 품질을 유지하면서 WebGL 다운로드 용량과 Texture 사용량을 최소화하기 위한 기준이다.

프로젝트 환경:

- Unity 6000.3.23f1
- 2D Top-Down
- WebGL
- Portrait 480 × 854
- Single Player

가장 중요한 기준:

> GamePlay Scene에서 시각적 품질이 깨지지 않는 최소 해상도와 최소 파일 용량을 사용한다.

원본 이미지가 클수록 좋은 Asset이라고 판단하지 않는다.

최종 판단 기준은 항상 실제 GamePlay 화면이다.

---

# 2. 작업 전 확인 문서

Map Asset 작업 전 반드시 다음 문서를 확인한다.

1. `Md/CodeBlueRush_MapAssetProductionGuide.md`
2. `Md/CodeBlueRush_MapAssetStyleGuide.md`
3. `Md/CodeBlueRush_WebGLAssetOptimizationGuide.md`
4. `Assets/Images/Maps/Reference/CodeBlueRush_Map_MasterReference.png`

우선순위:

```text
게임 정상 동작
↓
Master Reference와의 시각적 통일
↓
GamePlay 가독성
↓
필요 최소 해상도
↓
파일 용량
↓
Asset 재사용

품질을 눈에 띄게 떨어뜨리면서 용량만 줄이지 않는다.
3. 이미지 해상도 기준
이미지를 무조건 고해상도로 생성하지 않는다.
작업 전에 해당 Asset이 실제 GamePlay Scene에서
화면에 어느 정도 크기로 표시되는지 먼저 판단한다.
그 크기를 기준으로 필요한 최소 해상도를 선택한다.
권장 기준
시민 / 작은 소품
권장:
128 × 128
필요한 경우:
256 × 256
나무 / 차량 / 중형 소품
기본 최대:
256 × 256
실제 화면에서 더 작은 해상도로 충분하다면 낮춘다.
일반 Road Tile / Sidewalk
기본 최대:
512 × 512
256에서도 연결부와 차선 품질이 유지된다면
256 사용을 검토한다.
일반 건물
기본:
512 × 512
1024를 기본값으로 사용하지 않는다.
병원 / 구급대 / 대형 건물
우선:
512 × 512
512에서 실제 GamePlay 품질이 부족한 경우에만:
1024 × 1024
사용을 허용한다.
대형 교차로 / 넓은 Tile
필요한 경우에만:
1024 × 1024
를 사용한다.
512로 충분한 경우 1024를 사용하지 않는다.
4. 해상도 감소 검사
기존 Asset이 필요 이상으로 큰 경우 다음 순서로 검사한다.
현재 해상도 확인
↓
GamePlay Scene 실제 표시 크기 확인
↓
한 단계 해상도 감소
↓
GamePlay Scene 비교
↓
품질 차이 확인
↓
차이가 없으면 낮은 해상도 채택

예:
1024
↓
512
↓
256

512에서 1024와 육안 차이가 없다면
1024를 유지하지 않는다.
256에서 512와 육안 차이가 없다면
256을 사용한다.
5. 실제 GamePlay 크기 우선
원본 이미지 해상도를 기준으로 최적화하지 않는다.
반드시:
480 × 854 GamePlay 화면에서 실제 보이는 크기
를 기준으로 판단한다.
예:
차량이 GamePlay 화면에서 약 60 × 100px 수준으로 보인다면
1024 × 1024 Texture를 유지할 필요가 없다.
필요 이상의 해상도는:
- 파일 용량 증가
- WebGL 다운로드 용량 증가
- Texture Memory 증가
원인이 된다.
6. PNG 최적화
최종 원본 Asset은 PNG를 사용한다.
저장 시 다음을 검토한다.
- 불필요한 Metadata 제거
- 불필요한 투명 영역 제거
- PNG 무손실 최적화
- 필요 이상의 Canvas 크기 제거
- 시각적 차이가 없는 범위에서 파일 크기 최소화
Low Poly 스타일 특성상
불필요하게 복잡한 색상 정보를 유지하지 않는다.
7. PNG Quantization
색상 수 감소 또는 PNG Quantization은
품질을 확인한 경우에만 적용한다.
반드시 확인:
- 색상 변화
- Low Poly 명암 손실
- Alpha 가장자리 깨짐
- Gradient Banding
- 윤곽선 깨짐
눈에 띄는 품질 저하가 발생하면 사용하지 않는다.
파일 크기 감소보다 시각적 통일성을 우선한다.
8. 투명 영역 최적화
독립 Sprite는 필요한 Padding만 유지한다.
StyleGuide에서 정한 Padding 이상으로
Canvas를 불필요하게 크게 만들지 않는다.
잘못된 예:
실제 오브젝트: 150 × 180
Canvas: 1024 × 1024

이처럼 대부분이 투명 영역인 이미지는 피한다.
권장:
오브젝트 실제 크기
+
필요한 Padding
=
최종 Canvas 크기

Road / Ground / Tile처럼 연결이 중요한 Asset은
연결 방향 Padding을 사용하지 않는다.
9. Asset 재사용 우선
새 PNG를 생성하기 전에
기존 Asset으로 해결할 수 있는지 먼저 확인한다.
Rotation
다음은 가능하면 Rotation으로 재사용한다.
- 직선 도로 방향
- 일부 차량 방향
- 대칭 소품
- 일부 Road Marking
Flip
다음은 가능하면 Flip으로 재사용한다.
- 좌우 대칭 건물
- 일부 소품
- 방향 차이만 있는 장식
단:
광원이나 그림자가 반전되어 어색하면
별도 Sprite를 제작한다.
Scale
다음은 가능하면 Scale로 변형한다.
- 나무 크기
- 관목 크기
- 일부 장식 오브젝트
과도한 Scale 변경으로 이미지가 깨지는 경우에는 사용하지 않는다.
Tint
동일한 형태에서 색상만 다른 경우
새 PNG 생성보다 Tint를 먼저 검토한다.
예:
Car_01
↓
Red
Blue
Yellow
Gray

단:
Tint로 인해 Master Reference의 명암이나 색감이 깨지면
별도 Variant를 사용한다.
10. 중복 이미지 생성 금지
다음과 같이 동일 역할의 Asset을
불필요하게 여러 PNG로 생성하지 않는다.
예:
Road_Straight_Horizontal.png
Road_Straight_Vertical.png

회전으로 해결 가능하다면:
Road_Straight.png

하나만 사용한다.
또한:
Tree_Small
Tree_Medium
Tree_Large

가 단순 크기 차이뿐이라면
별도 PNG보다 Scale 사용을 우선 검토한다.
11. Unity Texture Import 최적화
생성된 이미지의 실제 GamePlay 사용 크기를 확인한 뒤
Texture Import 설정을 적용한다.
Texture Type
Sprite (2D and UI)
Mip Maps
기본:
Off
2D Top-Down 게임에서 명확한 필요가 있는 경우에만 사용한다.
Filter Mode
기본:
Bilinear
Master Reference의 부드러운 Low Poly 스타일을 유지한다.
Read / Write
필요하지 않은 경우:
Disabled
로 유지한다.
CPU에서 Texture Pixel 데이터를 직접 읽거나 수정하는 기능이 필요한 경우에만 활성화한다.
Mesh Type
Road / Ground / Tile:
Full Rect
권장.
다른 독립 Sprite는 프로젝트 구조를 확인해 결정한다.
12. Max Size
Texture Import의 Max Size도 필요 이상으로 크게 설정하지 않는다.
예:
원본:
512 × 512
게임에서 필요한 품질:
256 × 256
이라면:
Max Size = 256
을 검토한다.
기준:
GamePlay 품질 유지
+
가장 작은 Max Size

를 선택한다.
13. Compression
Compression을 무조건 None으로 고정하지 않는다.
제작 과정에서는 원본 품질 확인을 위해
Compression None을 사용할 수 있다.
최종 GamePlay Asset에서는 WebGL 기준으로:
눈에 띄는 품질 저하가 없는 범위에서 Compression 사용을 우선한다.
검사:
압축 전
VS
압축 후

실제 480 × 854 GamePlay Scene에서 비교한다.
육안으로 차이를 구별하기 어렵다면
압축된 설정을 사용한다.
14. Reference 이미지
CodeBlueRush_Map_MasterReference.png는
게임에 직접 사용하는 Texture가 아니다.
목적:
Asset 제작을 위한 시각 Reference
이다.
따라서 게임 Scene, Prefab, Runtime Script에서
직접 Reference하지 않는다.
WebGL Build에 불필요하게 포함되지 않도록 한다.
Reference 이미지는 수정하거나 덮어쓰지 않는다.
15. Asset 생성 전 검사
새로운 이미지를 만들기 전에 다음을 확인한다.
1. 기존 Asset으로 해결 가능한가
2. Rotation으로 해결 가능한가
3. Flip으로 해결 가능한가
4. Scale로 해결 가능한가
5. Tint로 해결 가능한가
6. 새로운 PNG가 정말 필요한가
새 PNG가 필요한 경우에만 생성한다.
16. Asset 생성 후 검사
각 Asset 생성 후 반드시 확인한다.
1. GamePlay Scene에서 이미지가 깨지지 않는가
2. 한 단계 더 낮은 해상도로 사용할 수 없는가
3. 불필요한 투명 영역이 없는가
4. PNG 용량을 더 줄일 수 없는가
5. 기존 Sprite와 중복되지 않는가
6. Rotation / Flip / Tint / Scale 재사용이 가능한가
7. Unity Max Size가 필요 이상으로 큰가
8. Compression 적용이 가능한가
9. Read / Write가 불필요하게 활성화되어 있지 않은가
10. WebGL Build에 불필요한 Texture가 포함되지 않는가
17. 최종 선택 규칙
두 Asset 설정의 GamePlay 품질 차이가 없다면:
항상 파일 용량과 Texture 크기가 더 작은 쪽을 선택한다.
예:
1024와 512 품질 차이 없음
→ 512 선택

512와 256 품질 차이 없음
→ 256 선택

단:
눈에 띄는 품질 저하가 있다면
한 단계 높은 해상도를 유지한다.
18. 대량 생성 금지
Asset을 한 번에 대량 생성하지 않는다.
권장:
대표 Asset 생성
↓
GamePlay Scene 테스트
↓
해상도 확정
↓
Compression 확인
↓
해당 카테고리 기준 확정
↓
나머지 Variant 제작

이를 통해 같은 카테고리에서
불필요하게 고해상도 이미지를 반복 생성하는 것을 방지한다.
19. WebGL 최적화 우선순위
최적화 우선순위:
1. 불필요한 Asset 제거
2. 기존 Asset 재사용
3. 불필요한 Variant 제거
4. 필요 최소 해상도 사용
5. 투명 Canvas 최소화
6. 적절한 Max Size 적용
7. Texture Compression 적용
8. 불필요한 Runtime Reference 제거

Compression만으로 문제를 해결하려 하지 않는다.
가장 먼저:
Asset 자체의 수와 해상도를 줄인다.
20. 최종 목표
CodeBlueRush Asset 최적화의 목표는
가장 작은 파일을 만드는 것이 아니다.
최종 목표:
480 × 854 GamePlay 화면에서 Master Reference의 시각 품질을 유지하면서
WebGL 다운로드 용량과 Texture 사용량을 최소화하는 것이다.
항상:
GamePlay 품질
+
Asset 재사용
+
최소 필요 해상도
+
최소 파일 용량