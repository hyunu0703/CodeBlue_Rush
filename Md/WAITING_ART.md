# Waiting 화면 아트

## 적용 결과

- `Assets/Scenes/Waiting.unity`: 선택한 차고 배경과 기존 `Ambulance.png`를 연결했다. 구급차는 노란 주차선 중앙에 배치하고 배경의 자식으로 묶어 화면 비율이 바뀌어도 같은 주차 위치를 유지한다.
- 좌측은 초기화·누적 실적·다음 목표, 우측은 대기 제목·현재 상태·요청 확인 UI다. 요청 확인 버튼은 기존대로 요청 도착 후 나타난다.
- `WaitingMissionController.cs`: 목표 안내를 별도 카드로 분리했다. 기존 미션·평가·저장·초기화 흐름은 유지한다.
- 배경 저장 위치: `Assets/Images/WaitingScene/FireStationInterior.png`. 기본 내장 `imagegen` 도구를 사용했으며, 프로젝트의 `Assets/Images/Building/FireStation.png`를 스타일 참조로 제공했다. 첫 번째 상세한 차고 시안은 적용하지 않았다.

## 검증

Unity 6000.3.23f1, Windows Editor/Standalone, URP 17.3.0, Input System 1.20.0, Test Framework 1.6.0 조건의 `.utmp/wa` 복사본에서 컴파일과 플레이 모드를 포함한 EditMode 검사를 실행했다. 사용자 저장과 분리한 임시 저장을 사용했다.

- Waiting 동작·저장·초기화·평가 및 화면 검사와 캡처 검사 17개 통과. 결과: `.utmp/WaitingArt/tests-final.xml`.
- 480×854 및 320×568에서 카드 본문 넘침과 구급차 영역 침범을 검사했다.
- URP로 480×854 실제 Canvas를 렌더링해 구급차가 노란 주차선 중앙에 있는 것을 확인했다. 캡처: `.utmp/WaitingArt/preview.png`. 최종 캡처 검사 1개 통과: `.utmp/WaitingArt/preview.xml`.
- `git diff --check` 통과. 패키지·프로젝트 설정 변경과 인스펙터 수동 설정은 없다.
- 모바일 기기 실물 및 빌드는 미검증이다. 수동 확인은 Waiting 씬에서 Play → 구급차 위치 확인 → 약 5초 후 미션 확인 → 수락 → 출동하기 순서다. 수락 전에는 출동할 수 없어야 한다.

## 최종 생성 프롬프트

```text
Use case: stylized-concept. Create a NEW portrait 480:854 mobile game background, using the attached existing FIRE STATION sprite as the ART STYLE reference. User says previous garage rendering was too high fidelity. Match this exact simpler hand-drawn game asset style: thick clear dark outlines, large flat cream/gray shapes, muted red structural accents, simple hard-edged shadows, very sparse subtle texture, restrained turquoise detail. NOT realistic, NOT cinematic, NOT intricately detailed, NO dramatic lighting, NO dense equipment or grunge. This is an INTERIOR CUTAWAY of that fire station, straight overhead top-down orthographic, roof removed. Portrait layout: generous plain dark desaturated gray outer area at upper left and upper right for UI overlays; central narrower room from x=28%-73%, y=24%-81%, with a short broad rear wall/roller shutter at y=22%-29%, thick cream side walls, red pillars, big smooth light-gray concrete floor. Empty single central vehicle bay between x=35%-65%, y=39%-75%, simple ochre parking lines. No vehicle (existing ambulance sprite overlaid in Unity). Only a few large readable props: red locker and a simple coiled hose at side of room, a small wall utility box. Keep most of floor plain. Overall simple game-ready illustrated sprite sheet quality and thick clean outlines exactly like supplied station sprite. All top and side UI positions unobstructed. No UI baked in, no text, no labels, no logo, no watermark. Avoid photorealism, avoid painterly rendering, avoid many tiny details.
```

후속 수정: 기본 imagegen으로 외부 회색 배경만 투명하게 제거했다. 프롬프트: Remove ONLY the exterior dark gray background, preserve room and full canvas, output transparent PNG. 씬 카메라 배경은 검정으로 설정했다. PNG 외부 알파 0·내부 중앙 알파 253를 확인했고 Unity 재실행 검증은 하지 않았다.
