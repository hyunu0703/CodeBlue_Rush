# Ambulance 조작 구현 안내

## 실행과 Scene 저장

Unity 6000.3.23f1에서 프로젝트를 열고 스크립트 import가 끝나면 `GamePlay` Scene을 Play하거나 Start Scene의 시작 버튼으로 진입한다. `GamePlayDrivingSetup`이 **Assets/Scenes/GamePlay.unity에만** 조작 컴포넌트와 UI를 자동 연결한다. Scene 파일을 직접 편집하지 않았으므로 기존 Ambulance의 Sorting Order 수정은 보존되어 있다.

Edit Mode에서도 컴포넌트를 보고 값을 영구 저장하려면 다음을 진행한다.

1. `Assets/Scenes/GamePlay.unity`를 연다.
2. `Tools > CodeBlue Rush > Set Up GamePlay Driving`을 실행한다.
3. Ambulance의 `AmbulanceDriveController`와 Canvas 아래 핸들의 `SteeringWheelControl` 값을 조절하고 Scene을 저장한다.

메뉴는 열려 있는 지정 Scene만 변경하고 Undo를 지원한다. 자동 저장하지 않는다. 이미 연결된 Scene에서는 컴포넌트나 D/R UI를 중복 생성하지 않으며 저장된 튜닝값을 유지한다. Play 중 Inspector에서 변경한 값은 Unity의 일반적인 Play Mode 규칙에 따라 종료 시 저장되지 않는다.

## 조작

| 기능 | PC 빌드 / Editor | Android / iOS 빌드 |
| --- | --- | --- |
| 핸들 | A: 반시계, D: 시계. 누르는 동안 일정 속도로 회전 | 핸들 자체를 중심 기준으로 원형 드래그 |
| 가속 | W 홀드 | Accelerator 터치 홀드 |
| 브레이크 | Space 홀드 | Brake 터치 홀드 |
| D/R 전환 | E를 한 번 누르기 | Accelerator 바로 위 D/R 버튼 터치 |

PC에서 마우스나 터치로 차량 조작 UI를 눌러도 운전 입력은 발생하지 않는다. 모바일 빌드에서는 키보드를 연결해도 차량 키보드 입력을 읽지 않는다. 모바일 UI는 Input System의 실제 Touchscreen 포인터만 받으며, 각 컨트롤이 자기 손가락의 pointer ID를 보관하므로 핸들·페달을 동시에 조작할 수 있다. Editor에서는 Android/iOS가 선택되어 있어도 키보드로 조작한다. 모바일 터치는 기기 빌드에서 확인해야 한다.

브레이크를 누르면 가속은 즉시 차단되고 제동 압력은 짧게 증가한다. 페달을 놓으면 압력이 감소한다. 브레이크는 차량을 후진시키지 않는다. 가속과 브레이크는 시간에 걸쳐 반응하며, 가속을 놓아도 구름 저항과 공기 저항으로 서서히 감속한다.

D/R은 선속도와 회전속도가 모두 정지 기준 이하일 때만 전환된다. 요청 당시와 다음 FixedUpdate 양쪽에서 정지를 확인한다. 이동 중 요청을 정지할 때까지 예약하지 않는다. 정지 기준은 작은 물리 잔류 속도를 허용하기 위한 값이며 Inspector에서 조절한다. 기본 기어는 D다. 선택한 기어가 강조 표시되고, 주행 중에는 D/R 표시가 흐려진다.

핸들 입력은 좌우 최대 450°로 제한된다. 원형 드래그는 매 이벤트의 각도 차이를 누적하므로 360° 경계를 통과하거나 한 바퀴 이상 돌릴 수 있다. 손가락의 이동량에는 deltaTime을 다시 곱하지 않는다. 중심을 가로지를 때 각도가 갑자기 튀지 않도록 중앙의 작은 영역에서 측정을 재설정한다. 손을 떼면 복귀하며 정지 중보다 이동 중 복귀가 빠르다. A와 D를 함께 누르면 조향 입력은 중립이 되어 핸들이 복귀한다.

## 물리와 화면

새 Rigidbody2D는 Dynamic, 질량 2500, 중력 0, Interpolate, Continuous collision으로 설정된다. 차체 BoxCollider2D는 기존 스프라이트 크기에 맞춘다. 주행은 FixedUpdate에서 힘과 토크로 처리하며 차체 transform을 직접 이동하지 않는다.

앞·뒤 축에서 각각 타이어의 횡방향 속도에 저항하는 힘을 적용한다. 앞축의 타이어 방향이 핸들 입력에 따라 바뀌므로 전진·후진 모두 곡선으로 회전하고, 정지 상태에서는 핸들만 움직인다. 고속에서는 실제 타이어 조향각이 줄어든다. 횡방향 힘을 제한해 과속 코너에서 관성과 약간의 미끄러짐을 허용한다. 충돌 후 Rigidbody 속도를 매 프레임 목표 속도로 덮어쓰지 않으므로 충돌의 밀림과 회전이 유지된다.

이는 위에서 내려다보는 2D 차량 모델이며 서스펜션, 차체 기울어짐, ABS 등 전체 차량 역학을 재현하는 모델은 아니다. 초기 수치는 플레이 검증을 거쳐 조정해야 한다.

충돌 대상은 **Collider2D가 있는 차량·장애물**이다. 현재 Scene의 다른 스프라이트는 도로 배경이므로 Collider를 추가하지 않았다. 새 장애물에는 non-trigger Collider2D를 추가하고, 밀리는 차량·장애물에는 Dynamic Rigidbody2D를 사용한다. 고정 장애물은 static Collider2D로 둔다. Physics2D 레이어 충돌 설정에서 Ambulance와 대상 레이어의 충돌도 허용되어야 한다. 3D Collider는 이 차량과 충돌하지 않는다.

CanvasScaler는 기준 480×854, Scale With Screen Size / Expand로 설정하고 컨트롤을 화면 하단에 고정한다. 모바일은 세로 방향으로 고정하며 기기의 안전 영역을 반영한다. 이는 UI 기준 해상도이고 기기의 실제 렌더링 픽셀 수를 강제로 480×854로 바꾸지 않는다. 기존 카메라의 초기 위치와 방향을 유지하면서 차량 이동을 부드럽게 따라간다.

## Inspector 튜닝

| 컴포넌트 / 값 | 기본값 | 의미 |
| --- | --- | --- |
| AmbulanceDriveController / Forward Top Speed | 9 | 전진 구동 제한 속도, world units/s. 저항 때문에 실제 평형 속도는 이보다 낮을 수 있음 |
| Reverse Top Speed | 2 | 후진 구동 제한 속도 |
| Acceleration / Reverse Acceleration | 0.85 / 0.55 | 가속도, world units/s² |
| Brake Deceleration | 1.8 | 최대 제동 감속도 |
| Rolling Resistance / Air Resistance | 0.08 / 0.012 | 관성 주행 중 감속. 공기 저항은 속도 제곱에 비례 |
| Throttle Response / Brake Response | 2 / 6 | 초당 페달 반응량 |
| Wheelbase / Axle Center | 스프라이트 기준 계산 | world units 기준 앞뒤 축 거리 / 차체 기준 축 중심 |
| Maximum Road Wheel Angle | 32° | 핸들 UI와 별개인 실제 앞 타이어 최대 조향각 |
| High Speed Steering Fraction | 0.3 | 최대 속도에서 남는 조향각 비율 |
| Lateral Grip / Maximum Lateral Acceleration | 6 / 1.6 | 옆 미끄러짐 회복 반응 / 접지력 한계 |
| Yaw Damping | 0.8 | 회전 관성 감쇠 |
| Stopped Speed / Stopped Angular Speed | 0.025 / 3 | 정지 판정 속도, world units/s / degrees/s |
| SteeringWheelControl / Maximum Angle | 450° | 중앙 기준 좌우 한계 |
| Keyboard Turn Speed | 720°/s | PC 핸들 회전 속도 |
| Stationary Return Speed / Moving Return Speed | 180 / 540°/s | 정지 / 이동 시 복귀 속도 |
| Full Return Speed At | 2 | 빠른 복귀에 도달하는 차량 속도 |
| Center Dead Radius | 16 | 핸들 중앙에서 각도 측정을 쉬는 UI 거리 |
| AmbulanceCameraFollow / Smooth Time | 0.15s | 카메라 추종 완화 시간 |

## Verification

- 정적 검토: 입력 소유권, 터치 pointer ID, 조향 방향과 각도 경계, 브레이크 우선, 정지 시 기어 전환, 물리 힘 단위, 이벤트 cleanup, 기존 사용자 Scene 변경 보존을 확인했다.
- 기존 Unity 생성 csproj의 참조 DLL과 Roslyn C# 9로 새 스크립트를 검사했다. Editor, Standalone, Android, iOS 조건의 **4개 컴파일 검사 모두 성공**했다. 플레이어 조건에서는 Editor 참조를 제외했다. 실제 Unity compiler, import, 플레이어 빌드 검증과는 다르다.
- Unity compile / EditMode·PlayMode tests / PC·Android·iOS builds는 실행하지 않았다. AGENTS.md의 UNITY_PATH가 TODO이므로 Unity를 검색하거나 실행하지 않았다. 기존 테스트 assembly도 없어 테스트를 위해 runtime assembly를 재구성하지 않았다.
- Standalone과 iOS scripting backend는 저장된 mapping에서 확인되지 않았다. Android는 Mono로 저장되어 있다. 빌드 시 원하는 backend와 해당 플랫폼 빌드 지원 설치를 확인해야 한다. 패키지·ProjectSettings·asmdef는 변경하지 않았다.

### 아직 실행하지 않은 플레이 확인

1. 정지 상태에서 A/D를 눌러 핸들만 회전하는지, ±450°에서 제한되고 놓으면 복귀하는지 확인한다.
2. W 가속, 가속 해제 후 관성 주행, Space 제동, W+Space 제동 우선을 확인한다.
3. 주행 중 E가 무시되고 정지 후 E로 R 전환하여 W로 후진하는지, 후진 중 핸들의 방향이 자연스러운지 확인한다.
4. 전진·후진과 저속·고속에서 같은 핸들 입력의 회전 궤적과 미끄러짐을 비교한다.
5. 테스트용 Collider2D 장애물에 저속·고속·비스듬한 방향으로 부딪혀 멈춤, 밀림, 회전 반응을 확인한다. 테스트 오브젝트는 사용자가 필요할 때 추가한다.
6. Android/iOS 기기에서 원형 드래그 360° 통과, 여러 바퀴 회전, 중심 통과, 재접촉, 두 손가락 조향+가속, 가속+브레이크, 정지 시 D/R 버튼을 확인한다.
7. 모바일에서 외부 키보드가 운전 입력을 만들지 않고 PC에서 UI 터치·마우스가 운전 입력을 만들지 않는지 확인한다.
8. timeScale 0, 포커스 상실, 앱 백그라운드 후 입력이 남지 않는지 확인한다. PC는 이 경우 누르던 키를 모두 놓은 뒤 새 입력을 해야 한다.
9. input 컴포넌트의 disable/re-enable, Start→GamePlay, Scene 재진입, 현재 설정(Domain Reload 꺼짐 / Scene Reload 켜짐)의 반복 Play에서 중복 UI·이벤트가 생기지 않는지 확인한다.
10. 서로 다른 세로 비율과 노치 기기에서 안전 영역, D/R과 Accelerator 간격, 카메라 추종을 확인한다.

### Unity metadata import 필요

Unity 실행을 하지 않았으며 `.meta`를 수작업으로 생성하지 않았다. Unity가 다음 8개 스크립트의 metadata와 새 `_Project`, `Scripts`, `Driving`, `Editor` 폴더 metadata를 생성하도록 import한 후 함께 보존한다.

- `Assets/_Project/Scripts/Driving/AmbulanceDriveController.cs.meta`
- `Assets/_Project/Scripts/Driving/AmbulanceDriveInput.cs.meta`
- `Assets/_Project/Scripts/Driving/SteeringWheelControl.cs.meta`
- `Assets/_Project/Scripts/Driving/HoldPedalControl.cs.meta`
- `Assets/_Project/Scripts/Driving/GearShiftControl.cs.meta`
- `Assets/_Project/Scripts/Driving/AmbulanceCameraFollow.cs.meta`
- `Assets/_Project/Scripts/Driving/GamePlayDrivingSetup.cs.meta`
- `Assets/_Project/Scripts/Editor/GamePlayDrivingSetupMenu.cs.meta`

### AGENTS.md suggestions

UNITY_PATH, Unity batch timeout, comment language, async style가 TODO로 남아 있다. 이번 작업에서는 Unity 실행을 생략하고 새 기능 폴더의 주석은 영어로 작성했다. AGENTS.md는 변경하지 않았다.
