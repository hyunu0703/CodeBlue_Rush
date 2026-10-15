#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>저장된 차종별 충돌 영역과 런타임 복제, 중심 보정 및 풀 재사용을 검증한다.</summary>
public sealed class TrafficPrefabColliderTests
{
    [TestCase("Avante")]
    [TestCase("Grandeur")]
    [TestCase("SonataTaxi")]
    [TestCase("Ray")]
    [TestCase("Sorento")]
    [TestCase("Sportage")]
    [TestCase("Carnival")]
    [TestCase("PorterII")]
    [TestCase("Bus")]
    public void Prefab_SavedCollider_IsEditableWithoutRuntimeComponents(string name)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Car/" + name + ".prefab");
        Assert.IsNotNull(prefab);
        var collider = prefab.GetComponent<BoxCollider2D>();
        Assert.IsNotNull(collider, name);
        Assert.AreEqual(1, prefab.GetComponentsInChildren<Collider2D>(true).Length);
        Assert.IsTrue(collider.enabled);
        Assert.IsFalse(collider.isTrigger);
        Assert.Greater(collider.size.x, 0f);
        Assert.Greater(collider.size.y, 0f);
        Assert.IsEmpty(prefab.GetComponentsInChildren<Rigidbody2D>(true));
        Assert.IsEmpty(prefab.GetComponentsInChildren<MonoBehaviour>(true));
    }

    [UnityTest]
    public IEnumerator GamePlay_CustomSizeAndOffset_PersistThroughMovementAndPoolReuse()
    {
        yield return new EnterPlayMode(!EditorSettings.enterPlayModeOptionsEnabled
            || (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) == 0);
        SceneManager.LoadScene("GamePlay");
        yield return null;
        var traffic = Object.FindFirstObjectByType<RoadTrafficController>();
        var generator = Object.FindFirstObjectByType<MissionMapGenerator>();
        Assert.IsNotNull(traffic);
        Assert.IsNotNull(generator);
        traffic.enabled = false;
        // 원본 에셋을 수정하지 않고 임시 프리팹 복제본에 큰 중심 보정을 적용한다.
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Car/Avante.prefab");
        var template = Object.Instantiate(prefab, new Vector3(10000f, 10000f, 0f), Quaternion.identity);
        template.name = "TrafficColliderTestPrefab";
        var tuned = template.GetComponent<BoxCollider2D>();
        tuned.size = new Vector2(1.1f, 2.2f);
        tuned.offset = new Vector2(1.2f, -1.6f);
        var data = new SerializedObject(traffic);
        var prefabs = data.FindProperty("vehiclePrefabs");
        prefabs.arraySize = 1;
        prefabs.GetArrayElementAtIndex(0).objectReferenceValue = template;
        data.ApplyModifiedPropertiesWithoutUndo();
        // 잘못된 트리거 설정과 누락된 콜라이더는 생성 전에 거부한다.
        tuned.isTrigger = true;
        Assert.IsFalse(traffic.ValidateConfiguration(out _));
        tuned.isTrigger = false;
        tuned.enabled = false;
        Assert.IsFalse(traffic.ValidateConfiguration(out _));
        tuned.enabled = true;
        Assert.IsTrue(traffic.ValidateConfiguration(out var error), error);
        traffic.enabled = true;
        generator.LaneMapRoot.rotation = Quaternion.Euler(0f, 0f, 90f);
        int[] pooledIds = null;
        foreach (var seed in new[] { 50505050, 50505055 })
        {
            Assert.IsTrue(generator.TryGenerate(seed, out error), error);
            for (var frame = 0; frame < 10; frame++)
            {
                yield return new WaitForFixedUpdate();
            }
            Assert.IsTrue(traffic.enabled);
            Assert.IsNotNull(traffic.Simulation);
            Assert.Greater(traffic.Simulation.Vehicles.Count, 0);
            var copies = generator.LaneMapRoot.GetComponentsInChildren<BoxCollider2D>(true)
                .Where(box => box.name == "TrafficColliderTestPrefab(Clone)").ToArray();
            Assert.AreEqual(3, copies.Length);
            var ids = copies.Select(box => box.GetInstanceID()).OrderBy(id => id).ToArray();
            if (pooledIds != null)
            {
                CollectionAssert.AreEqual(pooledIds, ids);
            }
            pooledIds = ids;
            foreach (var box in copies)
            {
                Assert.AreEqual(tuned.size, box.size);
                Assert.AreEqual(tuned.offset, box.offset);
                Assert.AreEqual(1, box.GetComponents<Collider2D>().Length);
                var body = box.GetComponent<Rigidbody2D>();
                Assert.AreEqual(RigidbodyType2D.Kinematic, body.bodyType);
                if (!box.gameObject.activeInHierarchy)
                {
                    continue;
                }
                var center = (Vector3)body.position + Quaternion.Euler(0f, 0f, body.rotation)
                    * Vector3.Scale((Vector3)box.offset, box.transform.lossyScale);
                var nearest = traffic.Simulation.Vehicles.OrderBy(vehicle =>
                    Vector3.SqrMagnitude(generator.LaneMapRoot.TransformPoint(vehicle.Position) - center)).First();
                Assert.Less(Vector3.Distance(generator.LaneMapRoot.TransformPoint(nearest.Position), center), 0.03f);
                Assert.AreEqual(tuned.size.x * Mathf.Abs(template.transform.localScale.x), nearest.Width, 0.0001f);
                Assert.AreEqual(tuned.size.y * Mathf.Abs(template.transform.localScale.y), nearest.Length, 0.0001f);
            }
        }
        traffic.enabled = false;
        Object.Destroy(template);
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator ExitRuntimeTest()
    {
        if (Application.isPlaying)
        {
            yield return new ExitPlayMode();
        }
    }
}
#endif
