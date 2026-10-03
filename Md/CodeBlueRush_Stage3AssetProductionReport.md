# CodeBlueRush Stage 3 Asset Production Report

## 기준

- `Md/CodeBlueRush_MapAssetProductionGuide.md`
- `Md/CodeBlueRush_MapAssetStyleGuide.md`
- `Md/CodeBlueRush_WebGLAssetOptimizationGuide.md`
- `Md/RoadSystem_Stage2.md`
- `Assets/Images/Maps/Reference/CodeBlueRush_Map_MasterReference.png`
- 승인된 `Road_Straight_2Lane.png`와 `Sidewalk_Straight_01.png`

## 생성 방식

Image generation was used for the arrow, line, bus, parking, corner sidewalk, intersection sidewalk, T-junction sidewalk, and crosswalk ramp source images. Prompts specified the Master Reference as the visual authority, approved Road/Sidewalk files as secondary references, a near-orthographic top-down view, transparent backgrounds for independent overlays, and the minimum intended Gameplay footprint. Deterministic post-processing was used only to crop transparent padding, calibrate road/sidewalk masks, set the 4.08 m road and 0.6 m sidewalk connection widths, normalize paint color, and write optimized PNG metadata.

## Unity 적용

`Assets/Editor/Map/MapAssetStage3.cs` adds the reusable Overlay and Sidewalk Prefab library, replaces the old City Prefab surface renderers while preserving RoadChunk, TrafficLane, VehicleStopZone, and EnvironmentSlot references, adjusts sidewalk points to the calibrated road edge, and adds a tiled approved grass background to Gameplay. The generator and road connection data are unchanged.

All new textures use Sprite/Full Rect, Bilinear, Mip Maps off, Read/Write off, centered pivot, and WebGL/Standalone DXT5 settings. Markings use 40 PPU; 20 m Sidewalk tiles use 25.6 PPU. The 512 px Sidewalk corner/intersection assets were retained after the 256 px comparison because their curb edge was visibly cleaner in the 480×854 capture.

## 검증

- Representative Unity review: `STAGE3_REVIEW_PASS checks=4`, no console errors.
- 480×854 captures compared 256 DXT5, 512 DXT5, and 512 RGBA for the representative corner; 512 DXT5 was selected.
- Static checks found no Master Reference dependency in Gameplay, no temporary-path references, all generated texture GUIDs referenced by a library or City Prefab, and no non-English or spaced filenames.
- A later full headless review reached Unity AssetDatabase refresh but could not complete because the local Unity Licensing IPC repeatedly disconnected. The applied Prefab/Scene files remain from the successful setup pass.
