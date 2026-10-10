# AGENTS.md — Unity C# Rules

Scope: C# scripts (runtime, Editor, tests) and project, package, or asset changes needed to complete a task in this Unity project. Apply these rules to new or changed code; MUST NOT reformat or refactor unrelated code.

Rule levels:
- MUST / NEVER = correctness or work-safety requirement. Exceptions must be stated in the rule. User confirmation cannot make an unsupported API valid or authorize loss of unrelated user work.
- Prefer = default; report a material deviation with a one-line reason.
- [STYLE] = team convention. Follow for new code; preserve existing conventions and APIs unless their migration is requested.
- An unmarked directive is Prefer. Facts and examples describe behavior; they do not weaken a marked requirement.

User requests and authorization:
- Follow explicit user requests over Prefer and [STYLE] rules without asking again.
- Where a rule explicitly permits a requested change, the user's clear request is authorization for that change. Ask only about unresolved scope, behavior, migration, or material risk; do not require a second confirmation merely because an asset or package is involved.
- For a MUST/NEVER rule without an applicable exception, investigate alternatives first. Explain the constraint and ask for the missing decision if necessary; do not imply confirmation can remove an engine limitation.
- NEVER commit, push, create branches, or stash unless requested. NEVER discard unrelated user work.
- This file is maintained by humans. Edit AGENTS.md only when the user explicitly requests it. Otherwise report incorrect rules and detected TODO values under AGENTS.md suggestions.

---

## 1. Environment Facts and Project Policy

### Environment facts (human-maintained; verify facts relevant to the task)
- Unity version: `6000.3.23f1`
- activeInputHandler: `1` — Input System.
- Target platforms / scripting backend: PC (Standalone) / **unknown — confirmation required**. The repository does not explicitly specify Mono or IL2CPP for Standalone; do not infer either backend from an absent mapping.
- Packages actually used:
  - TMP: used; included in `com.unity.ugui` version `2.0.0`.
  - Input System: used; version `1.20.0`.
  - UniTask: not used; no package registration or code references were found in the reported repository check.
  - Addressables: not used; no package registration or code references were found in the reported repository check.

These facts reflect the repository-check results supplied by the user. Recheck relevant settings and package usage in the actual Unity project when a task depends on them; do not present this document update as an independent repository verification.

Use minimal relevant reads. If a value is missing or contradicted, report the detected value or mark it unknown; MUST NOT guess an environment fact.
- Version: `ProjectSettings/ProjectVersion.txt`. `6000.x` identifies Unity 6; use documentation for the exact release, not a blanket assumption about all 6000.x versions.
- Packages: `Packages/manifest.json` and `Packages/packages-lock.json`; inspect the resolved package's assembly/API information when needed. Installed does not mean referenced, supported on the target, or already used.
- Input: search `activeInputHandler` in `ProjectSettings/ProjectSettings.asset`.
- Backend: read the complete relevant `scriptingBackend` mapping, not a fixed number of lines. Report backend per known target; do not infer the target list from this mapping alone.
- Targets: use the human policy and existing build/CI configuration; inspect saved build configuration or Editor build settings when available. Distinguish intended shipping targets from the currently selected Editor target. If still unknown, ask only when the task depends on it.
- Enter Play Mode settings: inspect domain/scene reload settings when changing static state, initialization, events, pooling, or ExecuteAlways/ExecuteInEditMode code.
- Use an available search tool (prefer `rg`; otherwise `grep` or PowerShell `Select-String`). Search instructions are patterns, not a requirement to install a shell tool.

Per task:
- Resolve the owning assembly from the nearest applicable `.asmdef` or `.asmref`, including the current folder. Inspect references, precompiled-plugin references, auto-reference behavior, platform constraints, define constraints, and version defines as relevant. Package presence alone does not grant access from every assembly.
- Read 2–3 relevant existing scripts. If conventions conflict, prefer the file being modified, then the feature folder, then the owning assembly. Ask only if the difference materially affects behavior or structure.
- MUST use APIs supported by the detected Editor and installed package versions. Examples: `Rigidbody.linearVelocity` in 6000.0; `Awaitable` from 2023.1; `destroyCancellationToken` from 2022.2. Unity 2022.3 uses `Rigidbody.velocity`, `drag`, and `angularDrag`.

Package policy:
- Input: 0 → legacy Input; 1 → Input System (legacy input reads can throw); 2 → match the relevant existing input implementation, or ask if none exists. If the setting is unreadable, do not select a backend silently. Verify package availability and assembly access separately.
- TMP: Unity 6 uGUI 2.x includes TMP; verify the package is available in this project. Unity 2022.3 normally uses the separate TMP package. Prefer TMP for new uGUI text. Add `Unity.TextMeshPro` assembly access only if needed and report it. Check required TMP settings, default font, and supporting resources; an arbitrary TMP asset is not proof that Essential Resources are complete. If unavailable, list the import/setup under Manual Editor steps.
- Prefer existing async/resource APIs. UniTask or Addressables installation or first use is allowed when explicitly requested. Otherwise do not introduce them merely because they are installed.
- For a requested package installation, check compatibility, choose an explicit source and pinned version/revision, preserve unrelated manifest entries, and report manifest/lock/assembly changes. Ask if source/version choice has a material unresolved tradeoff. Let Unity resolve the lock file; do not fabricate it. Installation alone does not authorize unrelated code migration.

### Project policy
- Scripts root: `Assets/_Project/Scripts/`. Put new runtime scripts here unless the task or existing structure requires otherwise. Modify existing scripts in place, including outside this root. Match existing Editor/test folders; otherwise use `Assets/_Project/Scripts/Editor/` and `Assets/_Project/Tests/EditMode/` or `PlayMode/`.
- Comment language: `TODO`. If TODO, match the file, then folder; if none, English.
- Async style: `TODO (Coroutine / Task / Awaitable / UniTask)`. If TODO, match the relevant feature; if none, coroutines. A specified but unavailable style does not authorize installation or unsupported APIs: use a compatible alternative if behavior is equivalent, otherwise ask.
- `UNITY_PATH` (Unity executable): `TODO`. If TODO or nonexistent, skip Unity compile/tests/import/build without searching for Unity, unless the user requests discovery. Static review and an available approximate .NET build remain applicable.
- Unity batch timeout: `TODO` minutes. If TODO, 30 minutes per Unity subprocess, including tests and builds. Stop only the process started for this task; never kill the user's Editor.

---

## 2. Change Safety

- MUST preserve existing staged, unstaged, and untracked user work. Record a task-start baseline before edits (Git status/diffs or file snapshots as available). Do not delete or overwrite existing scripts, generated code, or third-party sources unless that change is requested. If concurrent edits overlap, preserve them and resolve the overlap before proceeding.
- NEVER directly edit `Library/` or `Temp/`. NEVER hand-create or hand-edit `.meta` files. Unity generates metadata on import. Every new asset and newly imported folder needs its generated metadata retained with it; for new scripts, report missing `.meta` generation as a required manual step if Unity was not run.
- Do not modify `ProjectSettings/`, `Packages/`, or serialized assets (`.unity`, `.prefab`, `.asset`, `.mat`, `.anim`, `.controller`, etc.) unless the user requests that change or it is a necessary, clearly scoped part of the requested implementation. Reading is allowed. This restriction also applies to writes through generated code, tests, import hooks, and Editor tools.
- For authorized serialized-asset changes, prefer an existing safe Editor workflow/API, limit the operation to named assets, preserve references and unrelated values, and inspect the resulting diff. Do not run automatic broad migrations. If no safe editing/verification route is available, explain the limitation and provide precise manual steps; do not substitute a code default change for a prefab change.
- `.asmdef` / `.asmref`: change only to provide required access or an authorized Editor/test assembly. Report every change. Do not restructure runtime assemblies solely to enable tests without agreement on that migration.
- Do not rename/move existing scripts, rename Unity component/data types, or change an existing namespace unless requested. Preserve metadata GUIDs and assess type identity, file/class matching, assembly membership, serialized references, and managed-reference migration. Prefer Editor moves; a filename move alone is not proof that all references survive.
- [STYLE] For new concrete MonoBehaviour/ScriptableObject types intended to be attached or represented by script assets, use one per file with a matching filename. Preserve existing valid layouts unless restructuring is requested.

Serialization changes (MUST):
- Rename serialized fields with `[FormerlySerializedAs("oldName")]` and `using UnityEngine.Serialization;`. Preserve useful older rename attributes. Update C# references and custom-editor property paths; inspect affected prefab/scene overrides when available.
- Before retyping/removing a field, removing serialization attributes, changing enum numeric values, or changing nested/SerializeReference types, assess existing assets and saved-data compatibility. If the migration or data loss is unresolved, ask before making the breaking change. These operations may affect values; loss is not identical for every type conversion.
- Changing a code initializer does not update existing serialized prefab/scene values. Report the distinction and modify the requested asset through the authorized workflow instead.

Public API changes (MUST):
- Before changing/removing a public method, property, event, or field, search relevant C# callers, inheritance/interface contracts, UnityEvent names (`m_MethodName`), AnimationEvent names (`functionName`, including imported animation metadata), reflection, string-based calls, and `nameof` uses.
- Search custom-editor serialized paths (e.g., `FindProperty("oldName")`) when changing serialized members. Dynamic strings, binary assets, generated/external callers, and unloaded content may not be exhaustively searchable; report those scopes as unverified, not absent.
- Update C# callers. Do not leave known asset callers broken merely because they are listed in a report. Migrate authorized assets, preserve a compatible entry point where its semantics are known, or stop before the breaking change and ask for the unresolved migration. Do not invent a DamageType or other new argument with material behavior merely to keep callers compiling.
- Preserve existing public-field APIs unless their migration is requested; private-field style is not a reason to break them.
- NEVER add unrelated packages, features, abstractions, or formatting changes. A compatibility adapter necessary to preserve known callers is within the migration scope; report it.

---

## 3. Naming and Layout [STYLE]

- PascalCase: types, methods, properties, constants, enum values; interfaces use `I` prefix.
- camelCase: private fields, locals, parameters. Match existing field prefixes in the relevant file/folder; do not rename merely to change style.
- Prefer `is` / `has` / `can` for boolean variables/properties where natural; predicate methods follow PascalCase (e.g., `IsGrounded`). Preserve override/interface names.
- Events: `OnHealthChanged`; handlers: `HandleHealthChanged`. Match an existing alternative event/raiser convention.
- Avoid names that hide inherited members or conflict with types used in the same scope (e.g., `transform`, `Random`, `Object`). Such names are not universally illegal; preserve required contracts and use qualification/aliases when appropriate.
- Use `this.field = field` for name collisions. Prefer Allman braces, braces on every branch/loop, and no new `#region` blocks.
- Group fields, properties/events, Unity callbacks, public methods, and helpers consistently with the file. Suffixes such as Manager, Controller, Config, and View describe roles; not every type needs one.
- Prefer cohesive methods and clear parameters. Do not add wrappers or split straightforward logic solely to meet arbitrary line/parameter limits.
- Remove unnecessary empty Unity message methods in code being changed; per-frame callbacks can incur dispatch overhead. Do not alter unrelated code solely for this cleanup.

---

## 4. Fields, Serialization, Methods

- [STYLE] Prefer `[SerializeField] private` fields in new MonoBehaviour/ScriptableObject code. Add read-only properties only for needed external access. Plain serializable data containers may use public fields.
- Unity's normal field serialization does not directly support Dictionary, static, readonly, or const fields, or properties themselves. Backing fields can be serialized; auto-property backing fields also have hot-reload behavior. Use `[field: SerializeField]` only when consistent with the project.
- `[SerializeReference]` supports eligible managed polymorphic/interface data, not UnityEngine.Object component references. For component interfaces, serialize a compatible Unity object reference and validate/cast it, or use the project's dependency injection pattern. Use custom serialization only when needed; assess migration and thread restrictions in serialization callbacks.
- [STYLE] Use explicit access modifiers; use `var` when the right-hand type is clear. Prefer early returns. Use Try-patterns for expected recoverable failure, not to hide programmer errors or exceptions.
- Give meaningful tuning values a named constant or serialized field. Mathematical identities, indices, and standard formula coefficients need no artificial constant solely for style.
- Tags: named constants plus `CompareTag`; verify the tag exists. Animator/shader IDs: `static readonly int` via the corresponding hash API. Layers: explicit LayerMask/filter policy.

---

## 5. Unity Correctness

### Null checks (MUST)
- UnityEngine.Object equality includes native-object liveness: a destroyed wrapper can compare equal to null while still being a non-null managed reference.
- Use `== null`, `!= null`, or Unity's boolean conversion for Unity-object liveness. Do not use `?.`, `??`, `??=`, or C# null patterns as liveness checks.
- Unity objects stored as interfaces, object, or generic references need both managed-null and Unity-liveness checks before accessing Unity state. Example for an interface/object reference:
  ```csharp
  if (target is null || (target is UnityEngine.Object unityObject && unityObject == null))
  {
      return;
  }
  ```
- `ReferenceEquals`/C# null checks may deliberately test managed-reference existence for managed-only cleanup, such as removing a field-like C# event handler from a destroyed publisher wrapper. Do not access native properties or a custom event accessor that touches Unity state through that exception.
- C# null operators are fine for plain managed objects, delegates, and events. State in an interface's documentation if implementations must be Unity components.

### Missing references and initialization
- Validate required references at the earliest point they are expected to exist. For an invalid component, log an actionable error once with context, disable it, and return. Disabling does not block public calls or external callbacks: guard those paths too.
- If a factory injects dependencies after creation, initialize explicitly before enabling/using the component. Revalidate if references can change or the component can be re-enabled; Awake does not rerun on re-enable.
- Same-object dependency: prefer RequireComponent plus GetComponent/TryGetComponent in Awake. Validate existing objects too; an attribute is not proof that every existing instance is correctly configured.
- Optional references: validate at use and define fallback behavior.

### Lifecycle
- Awake precedes OnEnable on the same object. Ordering across different objects is not guaranteed.
- Scene-loaded active objects complete their Awake/OnEnable calls before Start calls; runtime-created or later-activated objects are not covered by that scene-wide guarantee. Another object's Start initialization is not guaranteed to precede yours.
- Start runs once when the instance becomes eligible, not on each re-enable. Inactive GameObjects defer Awake; disabling only a component is different from deactivating its GameObject. OnDestroy is documented for objects that were previously active; do not make indispensable cleanup depend on an object that never activates.
- OnDisable may occur on deactivation, destruction, or script reload. Do not assume every OnDestroy has a fresh paired OnDisable or that every shutdown environment delivers all callbacks.
- Prefer Awake for own components/state, Start for dependencies known to be ready, and explicit readiness/injection for services initialized later. Avoid reading other objects' initialization-dependent state in Awake/OnEnable without a readiness contract.
- LateUpdate is suitable for camera follow after normal Update; ordering among peers or custom PlayerLoop phases still needs an explicit contract.
- Pooled/reusable objects reset state on pool get/release or appropriate enable/disable callbacks, not only Awake/Start. Ensure exactly one owner performs each reset and cleanup.

### Event subscriptions (MUST)
- Every subscription needs matching cleanup on the same publisher/delegate identity. Default active-time lifetime: OnEnable/OnDisable. Instance lifetime: explicit initialization/OnDestroy only when activation and cleanup are guaranteed; otherwise use owner-controlled disposal.
- Store the publisher actually subscribed to. Make binding idempotent. For state notifications, subscribe and then synchronize current state so late-created UI does not wait for the next change.
- A singleton reference is not proof of readiness. The publisher must complete the state initialization needed by listeners before reporting ready. Define readiness, replacement, and removal when the publisher can appear later, change, or disappear.
- For a stable publisher initialized in Awake, the following pattern is sufficient for binding and initial state. It assumes `Score` is ready when Instance becomes visible, `OnScoreChanged` is a field-like `event Action<int>`, and callbacks run on the main thread. Adapt names to the project; this is not a complete ScoreView class.
  ```csharp
  private ScoreManager subscribedManager;

  private void OnEnable()
  {
      TrySubscribe();
  }

  private void Start()
  {
      TrySubscribe();
  }

  private void OnDisable()
  {
      Unsubscribe();
  }

  private void Unsubscribe()
  {
      // Managed-only field-like event cleanup; no native publisher access.
      if (!ReferenceEquals(subscribedManager, null))
      {
          subscribedManager.OnScoreChanged -= HandleScoreChanged;
      }
      subscribedManager = null;
  }

  private void TrySubscribe()
  {
      if (!isActiveAndEnabled)
      {
          return;
      }

      ScoreManager manager = ScoreManager.Instance;
      if (manager == null)
      {
          Unsubscribe();
          return;
      }
      if (ReferenceEquals(subscribedManager, manager))
      {
          return;
      }

      Unsubscribe();
      subscribedManager = manager;
      subscribedManager.OnScoreChanged += HandleScoreChanged;
      HandleScoreChanged(subscribedManager.Score);
  }
  ```
- OnEnable + Start alone does not detect later readiness or replacement. Use the project's ready/instance-changed registry notification (including removal), explicit factory injection, or another documented binding trigger. Active subscribers register for that notification before the initial binding attempt and unregister on disable; callbacks must not rebind disabled subscribers. Reset owned static notifications per §7. Do not create a global event system for a task that only needs factory injection.
- DefaultExecutionOrder can order participating scripts; it does not create missing publishers or guarantee readiness of future objects.

### Time and pause (MUST)
- Integrate rates using the appropriate delta time (position += velocity * dt, countdowns, MoveTowards steps). Time.deltaTime inside FixedUpdate reports the fixed step.
- Do not multiply direct velocity assignments or Force/Acceleration AddForce inputs by deltaTime. Impulse/VelocityChange inputs represent instantaneous changes; multiply a force by a simulation step only when deliberately converting it to impulse, with units made clear.
- Standard mouse/pointer deltas are displacements; do not apply deltaTime again. Stick look commonly represents a rate and needs deltaTime; respect the actual input API/processor contract.
- Work that continues at timeScale = 0 needs a callback that still runs and unscaled timing/realtime waits. unscaledDeltaTime alone does not make FixedUpdate, WaitForFixedUpdate, or WaitForSeconds progress normally during pause.

### Input
- Enable/disable/dispose only actions or maps your component owns: created privately or obtained from an owned clone. InputActionReference does not confer ownership of shared actions. PlayerInput/central-manager actions are controlled by their owner; consumers only manage their own callbacks.
- Capture one-shot physics input in Update or a callback and consume at the simulation boundary. A bool deliberately coalesces multiple presses; use a bounded queue/timestamp only when separate presses or buffering are required. Define expiration and clear pending input on disable/pool return; avoid carrying paused input into resume unless requested.
- Handle non-physics input at the appropriate event boundary. Do not add global Input Manager mappings or .inputactions assets without the scoped authorization in §2.

### Physics (MUST unless explicitly marked Prefer)
- With automatic fixed-step simulation, apply continuous Rigidbody/Rigidbody2D control in FixedUpdate. With manual simulation, use the project's simulation boundary. One-off queries/setup may run elsewhere when consistent with the simulation state.
- For 3D dynamic Rigidbody, prefer forces or deliberate velocity control; for kinematic motion, prefer MovePosition/MoveRotation. Check the exact 2D API/body-type contract separately. Do not write transform.position for ordinary simulated-body motion.
- Teleports use an explicit Rigidbody position/rotation workflow suitable for that version, with collision/interpolation/synchronization consequences considered. Do not apply continuous movement rules blindly to instantaneous relocation.
- Inspect body type and simulation mode first. If unknown and the choice changes behavior, ask (§12); do not silently assume dynamic.
- 3D queries: explicit LayerMask and QueryTriggerInteraction. Trigger defaults depend on Physics settings.
- 2D queries: use a supported ContactFilter2D (SetLayerMask enables filtering; set useTriggers explicitly) or a supported overload with a documented trigger policy.
- Use the exact query/overload's ordering contract. 3D NonAlloc queries do not guarantee nearest-first results; some 2D casts explicitly return distance-sorted results (e.g., 6000.0 Physics2D.Raycast with ContactFilter2D and array/List). Do not generalize one API's contract to all queries.
- A full fixed-size result buffer can be truncated. Use a justified design bound or grow and re-query; do not claim the retained hits are the nearest unless that exact API guarantees it. Lists may resize and allocate. Process only the returned count, not stale buffer elements.
- Prefer collision.GetContact/GetContacts and contactCount over allocating collision.contacts.

### Async and coroutine lifetime (MUST)
- Choose and document the state/resource owner and operation lifetime. Disable/pool-return lifetime commonly uses a CTS created on enable and cancelled on disable. Destroy lifetime can use a destroy token where supported; capture it before destruction. Dispose owned CTS instances at a safe boundary after consumers needing registration have completed; cancellation and disposal are not interchangeable.
- Capture the operation token once and pass it to supported APIs. Do not reread a replaceable CTS field after await. Check lifetime/liveness before applying results; for reusable objects capture a generation and increment it at the owner's invalidation boundary. Checks guard writes to owner/session state, not every pure calculation.
- Define concurrent-start policy for operations writing the same state: reject, cancel previous, or apply latest. Older continuations must not clear or dispose a newer operation's state/CTS.
- Cancellation does not stop token-ignoring work or release results. Observe completion and clean up late results/handles even when their state update is rejected (§8).
- Expected cancellation must follow the operation/API's documented contract. token.IsCancellationRequested alone does not establish the source of an exception. Check exception tokens where meaningful; explicitly account for linked or token-less cancellation APIs. Propagate unexpected cancellation/failures to observing callers.
- Propagate failures from awaited/observed operations. At top-level fire-and-forget boundaries, catch expected cancellation and log unexpected failures once with valid context. Do not swallow failures or log them repeatedly at every layer.
- async void only for required event signatures, with top-level exception handling. Otherwise use Task, supported Awaitable, or installed UniTask according to §1; coroutines remain a valid fallback.
- Awaitable instances are single-consumer and must not be awaited repeatedly/concurrently. Prefer local ownership; do not cache completed pooled instances. UniTask re-awaitability depends on its source/API; do not assume Task semantics.
- Coroutines stop on GameObject deactivation/component destruction, not merely component disable. Stop them explicitly if disable ends their lifetime; do not depend solely on iterator cleanup for critical external resource release.
- Main-thread-only Unity APIs must not run on background threads. Documented thread-safe APIs/Jobs are exceptions within their contracts. Switch to the main thread before scene-object access; define termination on Play Mode exit for background work.

### API replacements (Prefer; only when supported)
- FindObjectOfType → FindFirstObjectByType/FindAnyObjectByType; choose ordering/active-object semantics deliberately.
- FindObjectsOfType → FindObjectsByType; use FindObjectsSortMode.None only when ordering is unnecessary.
- String StartCoroutine, SendMessage, BroadcastMessage → typed calls, references, interfaces, or events. Preserve existing string callers during requested API migration (§2).
- In 6000.0, prefer Rigidbody.linearVelocity/linearDamping/angularDamping over the older names. Use supported names in 2022.3; verify other releases.

---

## 6. Performance

- Hot paths are repeatedly executed frame/physics/input-move work and helpers actually reached at that frequency, including conditional helpers whose condition is commonly true. Discrete events are not automatically performance-critical; consider their measured/expected rate and workload.
- Prefer caching stable component/look-up results, avoiding recurring allocations, updating collections/UI on change, and pooling frequently reused objects when lifecycle complexity is justified. Do not mandate Dictionary, pooling, or distance micro-optimizations for small non-hot workloads.
- Every cached external object needs an invalidation/refresh policy for replacement, destruction, or scene changes. Camera.main performance depends on version and usage; cache recurring use when useful, with refresh rules.
- For deliberate hot-path exceptions, describe a verified bound or label an estimate as such. Report material Prefer deviations; do not fabricate a profile or call-rate guarantee. Profile when the required tools are available and the task warrants it.
- MUST NOT add recurring diagnostic logs in release hot paths. Guard them with UNITY_EDITOR/DEVELOPMENT_BUILD or an existing diagnostic policy. A one-time actionable error for invalid state is allowed; stop/throttle repeated errors. Never log sensitive data (§10).
- TMP SetText numeric-format overloads can reduce formatting allocation; do not claim all text updates/Editor paths allocate nothing. Change text only when its displayed value changes.
- Cache WaitForSeconds only for fixed repeated durations when appropriate. Do not optimize unrelated code or introduce runtime options merely to satisfy this section.

---

## 7. Architecture

- Prefer small cohesive components and existing project patterns. One specific dependency: reference/injection; multiple state listeners: C# event; Inspector-designed response: UnityEvent.
- Prefer ScriptableObject assets for shared designer-authored configuration when consistent with the project. MUST NOT mutate persistent assets for transient runtime/save state. Owned runtime instances/clones may hold runtime state; shallow clones can still share referenced assets, so ownership matters.
- [STYLE] View components display data and emit user-intent events. Prefer controllers/services for authoritative game-state writes. Local visual/interaction state is not game state; preserve existing architecture unless migration is requested.
- Prefer an explicit state model for mutually exclusive modes with transitions; independent flags need not become a state machine.

### Singletons (requirements for singletons added/changed)
Use for services shared across a scene/application only when already established or requested. MUST define:
- Scope: scene-local or app-lifetime; readiness: which state is valid when Instance is published.
- Duplicates: follow the established policy; default rejects the duplicate in Awake, returns immediately, and avoids publishing/subscribing duplicate state. Define whether destroying the component or GameObject is appropriate.
- Cleanup: clear Instance only when it refers to this publisher; notify dependents of removal/replacement where needed. Do not use a lazy-creation getter during cleanup.
- Quit: never create/find replacement services during shutdown; use an established quitting flag/event lifecycle and reset owned flags per Play session.
- Access from Awake/OnEnable requires a valid readiness contract or deferred binding (§5), not merely a particular callback name.

### Static state (MUST)
- Domain Reload disabled preserves mutable static fields and event handlers between Play sessions. Reset owned per-session state and owned events; do not zero immutable constants or readonly hash/ID caches.
- Clear mutable readonly collections if they are session state; dispose owned resources before discarding them. Remove handlers from external static events rather than assigning those events. Only a declaring type can assign its own event.
- Example inside the declaring singleton type, assuming these members exist and registration is restored by the project's lifecycle:
  ```csharp
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  private static void ResetStatics()
  {
      Instance = null;
      OnGlobalEvent = null;
  }
  ```
- This reset alone is not a complete initialization system. Define re-registration and instance-state reset under the configured Domain/Scene Reload combination; do not rely only on Awake/Start when those callbacks can be skipped or state can persist. Include ExecuteAlways/ExecuteInEditMode behavior where applicable.
- Do not depend on unspecified ordering between reset methods in different types. Verify repeated Play sessions, relevant reload-setting combinations, and restoration of singleton references/listeners (§13).

---

## 8. Resource Lifetime (MUST)

- Define one owner for each runtime resource/handle, the cleanup boundary, and any explicit ownership transfer. Resources loaded from persistent project assets are not automatically owned/destroyable by the consumer.
- Renderer material/materials access can create owned material instances. Retain and destroy instances you own. sharedMaterial is suitable for reading/assigning shared materials, not transient mutation of persistent shared assets. Use an owned instance or MaterialPropertyBlock according to shader/pipeline needs; MPBs can prevent SRP Batcher compatibility.
- Destroy owned runtime Texture2D, RenderTexture, Mesh, Material, and ScriptableObject instances when no longer needed. Release owned RenderTexture GPU resources as appropriate; do not call Destroy on a temporary texture obtained through a pool API—return it through that API.
- Addressables: follow the installed version's handle/release contract, including failed operations and trackHandle options. Pair asset loads with Release and instantiated instances with the appropriate ReleaseInstance/handle workflow. Do not substitute Destroy for the required ownership release.
- Cancellation does not release a handle or transfer ownership. The initiating owner must observe late completion and release/transfer resources whose results can no longer be used. Prevent double release and use after release.
- Prefer existing resource loading conventions. New Resources folders/Resources.Load calls require an explicit requested need rather than convenience alone.

---

## 9. C# Limits

- Unity 2022.3 and 6000.0 use C# 9 with documented limits. These include covariant return types, module initializers, suppression of localsinit emission, and extensible calling conventions for unmanaged function pointers. Unmanaged function pointers themselves are not universally unsupported. Verify the exact version for other releases; older Unity does not universally mean C# 8.
- [STYLE] Avoid new record/init-based project models unless requested and supported by the project. IsExternalInit support may require a project shim in these versions; record types are not supported by Unity's normal serializer. This is separate from whether a managed nonserialized model can compile.
- MUST NOT introduce language features unsupported by the actual Unity compiler; default C# 9 configurations do not support C# 10 global using, file-scoped namespaces, or record struct. A successful build with a different SDK/compiler is not proof of Unity support.
- Prefer existing nullable-reference policy; do not enable annotations across unrelated code.
- Prefer typed APIs over reflection. If reflection is required, protect the actually referenced members from stripping (Preserve/link.xml as appropriate) and verify the target. Preservation alone does not generate every IL2CPP generic AOT instantiation; use the project's supported AOT strategy for required generic code. Treat link.xml edits as scoped project changes and report them.

---

## 10. Comments, Debug, Editor Code, Security

### Comments and debug
- Comment intent, units, ownership, or a non-obvious contract rather than restating code. Prefer summary comments on non-obvious public APIs/interfaces. Remove temporary logs and commented-out code in changed code; use `// TODO: ...` for actionable deferred work.
- Use a valid Unity context for warnings/errors when available. Static methods and destroyed objects may not have a usable `this`; never access a destroyed object's native state just to format a log.

### Editor code (MUST)
- Exclude Editor-only code from players using UNITY_EDITOR and/or an Editor-only assembly. An Editor folder under a runtime asmdef is not automatically isolated; use a suitable Editor asmdef/asmref or correctly guard all Editor dependencies. Runtime assemblies must not reference Editor-only assemblies.
- Tools/tests that write assets obey §2; generating a tool is not authorization to run broad changes. Prefer explicit user-invoked, narrowly scoped operations over automatic import/initialization writes.
- Use Undo.RecordObject before ordinary object edits; structural operations need the appropriate Undo APIs. Use serialized-property workflows and prefab-instance modification recording as appropriate; mark assets/scenes dirty through the correct API. Undo alone is not a backup or prefab migration policy. Save only authorized changes and inspect the diff.
- Use AssetDatabase.StartAssetEditing/StopAssetEditing only when needed for supported batch operations, with try/finally. Do not wait for imports while import processing is suspended.
- OnValidate may run off the main thread: limit it to local data validation. Do not call non-thread-safe Unity APIs or modify scenes there. Use a controlled Editor action for structural changes; any deferred work needs main-thread execution, liveness/context checks, duplicate prevention, and correct Play/Edit Mode targeting.

### Security (MUST)
- Never hardcode/log credentials, payment data, tokens, or personal data. Keep server secrets off distributed clients; moving them into a client config or serialized asset does not protect them. Public service endpoints/client identifiers are not automatically secrets; follow the project's documented classification.
- Use established secure configuration for server-side secrets and appropriate platform storage for required user credentials. If no mechanism exists, ask before inventing one.
- Do not treat PlayerPrefs, plaintext, or client-side encryption as authoritative protection for currency/progress. Offline local progress can use the established save format, but must not become trusted server state without server validation.
- Validate untrusted saves, network responses, and deep links against expected schema, size/range limits, allowed destinations/identifiers, and authorization before acting. Scope validation to the actual inputs; do not add unrelated security systems.

---

## 11. Tests

- Add/update tests when a relevant existing test assembly can access the changed code, or the user requests tests. Do not create assemblies or restructure runtime code solely because an unrelated test assembly exists.
- Match the installed Test Framework's assembly format and existing folders. EditMode assemblies are Editor-only. Do not require UnityEditor.TestRunner in player-capable PlayMode assemblies; use the installed package's appropriate TestAssemblies/references configuration, including NUnit and the assembly under test.
- Test asmdefs cannot directly reference predefined Assembly-CSharp. If access requires runtime assembly migration, agree on that scope first; otherwise report the limitation and use available/manual checks.
- Prefer NUnit Test for synchronous logic, including synchronous PlayMode tests. Use UnityTest/IEnumerator when yielding frames or supported test instructions is required. Match package-version support for other test forms.
- [STYLE] Name tests `Method_Condition_ExpectedResult`; verify behavior rather than duplicating implementation.
- Clean up only test-owned objects/resources/assets on failure too. Use EditMode DestroyImmediate for owned objects when appropriate; PlayMode cleanup must respect delayed destruction. Do not delete user assets as teardown.
- Control random seeds/time inputs and avoid execution-order dependencies. Use bounded waits rather than arbitrary real-time sleeps. For lifecycle/physics behavior, test the actual simulation/lifecycle boundary.

---

## 12. When to Ask

- Investigate relevant code, settings, manifest, and read-only asset data first. Ask when an unresolved choice changes behavior, ownership, migration, or structure: input backend with Both and no existing usage, unknown dynamic/kinematic requirements, unknown new API argument semantics, save format, singleton scope, or destructive serialized-data migration.
- An explicit asset/package/file change request authorizes the named change under §2. Do not ask the user to repeat that request. Ask only for missing details or a material risk not determined by the request.
- Otherwise match the project, record material assumptions, and proceed. Continue independent investigation while a necessary question is pending; do not implement dependent behavior by treating silence as consent.
- If tools/environment prevent execution, finish available work and report the exact limitation with concrete remaining steps. Do not claim the requested behavior is complete when a necessary asset migration or verification remains unresolved.

---

## 13. Verification and Report

### Protect existing work and preflight (MUST)
1. Keep the task-start baseline from §2. Before every Unity run, record whole-repository status and relevant diffs/content (including existing dirty files and untracked files). Without Git, use available snapshots and report comparison limits. Status alone cannot identify who wrote a change.
2. Verify the executable version matches ProjectVersion.txt, the working directory/projectPath is the project root, and the project is not already open in another Editor. Do not open with another version to obtain verification or close the user's Editor. Missing path, version mismatch, open-project lock, or unavailable license means that check is blocked/not run; explain it.
3. Consider existing import/initialization hooks before running Unity. Do not run known broad migrations or external side effects merely to compile. Prefer a safe isolated copy when needed; do not create Git branches/stashes without a request.
4. Use unique output paths per run and retain existing logs/results. Record start/completion and enforce the §1 timeout for compile, tests, and builds. On timeout, stop only the owned subprocess safely; inspect changes and classify the check as not verified.
5. Compare changes after the run. NEVER automatically revert shared-checkout changes based on pre-run cleanliness or git status. Preserve the diffs; restore only explicitly authorized, attributable changes through a scoped workflow that cannot overwrite concurrent user work. Otherwise leave them and report them.
6. Keep generated metadata for assets/folders created by this task. Report other reserialization, settings, package, or metadata changes. Do not silently revert or accept unrelated changes as part of the implementation.

### Checks (run applicable checks when available)

- Static review: inspect changed code, API/serialization callers, assembly/package access, and the final diff. State its limits; it is not a compile or runtime test.
- Unity compile: use the matching executable with batchmode, nographics, quit, projectPath, and a fresh log. Pass requires normal exit code 0, a completed log from this run, and no compiler, fatal, license, or package-resolution errors. Review all relevant compile errors, not only the literal string `error CS`. A launch/import failure is not evidence that the changed code compiled.
- Tests: run relevant EditMode tests when available; run relevant PlayMode tests for lifecycle, pooling, input, or physics changes. Use runTests without quit, fresh per-run XML/log paths, and the installed Test Framework CLI contract.
- Test pass requires normal process completion according to that CLI contract, fresh completed XML with a successful final result (normally `result="Passed"`), `failed="0"`, and `passed > 0`. Report skipped/inconclusive counts and unexecuted intended coverage; zero total or skipped-only execution is not a pass. Timeout/crash/missing XML is not verified even if partial results exist.
- If Unity cannot run, an available dotnet build of an existing current generated solution may provide approximate checking. It can use stale references or different defines/compiler support; do not treat it as Unity/player verification. Do not generate or repair the solution by unrelated project changes solely for this fallback.
- For target-specific APIs, IL2CPP/AOT, stripping, or reflection, run the relevant target build when the environment and established build command are available. Otherwise list the target build/runtime checks still required. Editor compilation does not prove player correctness.
- For pure logic, add/update meaningful tests under §11. For style-only changes, no new tests are required. Avoid broad unrelated testing unless a failure or unresolved concern justifies it.

PowerShell invocation examples (illustrative; use absolute validated project/output paths and a timeout-capable process runner). Set `$unityExecutable`, `$projectRoot`, `$compileLog`, `$testXml`, and `$testLog` from the facts/run paths above. These are not commands to execute blindly:
```powershell
& $unityExecutable -batchmode -nographics -quit -projectPath $projectRoot -logFile $compileLog
& $unityExecutable -batchmode -nographics -projectPath $projectRoot -runTests -testPlatform EditMode -testResults $testXml -logFile $testLog
```
For PlayMode use `-testPlatform PlayMode` with separate fresh outputs. Other shells require their own valid invocation syntax. These examples do not implement process timeout enforcement.

### Report

MUST always include **Verification**: what actually ran, its result/counts, and material checks not run with reasons (e.g., `Unity compile/tests not run — UNITY_PATH is TODO`). NEVER report a planned/manual check as completed or claim a build that did not run.

Include other sections only when relevant:
- **Changes:** requested behavior delivered and any remaining implementation/migration work.
- **Play-mode checks for the user:** concrete pending scenarios, clearly labeled as not run: disable/re-enable, destruction, pool reuse, missing references, initial UI state, publisher replacement, pause, scene changes, repeated Play sessions and configured reload combinations.
- **Manual Editor steps:** exact components/assets/references, settings, metadata import, TMP resource setup, or target checks needed. With three new scripts and no Unity run, identify all three missing metadata files; do not claim generation occurred.
- **API / serialization changes:** signatures, fields/defaults/types, enum values, assembly changes, callers inspected/updated, compatibility paths, migrated assets and unverified external/binary scopes. Known broken asset calls are unresolved work, not a completed migration.
- **Files changed by Unity during verification:** task-owned metadata, other changes preserved for review, and any explicitly authorized restoration. If no Unity run occurred, do not imply Unity changed files.
- **Rule deviations:** material Prefer deviations and applicable authorized exceptions; never imply an unsupported operation was made valid by approval.
- **Assumptions:** material implementation choices, clearly separated from detected facts.
- **AGENTS.md suggestions:** detected TODO facts and remaining policy corrections. Do not fill project-specific unknowns with guesses.

Example (illustrative, not an execution claim):
```text
Changes: Renamed PlayerController.speed to moveSpeed; preserved serialized values through FormerlySerializedAs.
Verification: Static review. Unity compile/tests not run — UNITY_PATH is TODO; no current generated solution was available.
API / serialization changes: C# usages and custom-editor property paths updated; prefab override behavior not verified in the Editor.
Manual Editor steps: Compile in the matching Editor and confirm existing prefab values/overrides are preserved.
```
