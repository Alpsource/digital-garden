using System;
using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace ImmersiveVideo
{
    public enum GeometryTier
    {
        None,
        SceneMesh,
        Planes,
        Proxy
    }

    // TASK-10: resolves which real-world geometry to reveal during the scan intro,
    // falling back through three tiers (real mesh -> planes -> procedural proxy room).
    // Must live on a GameObject under the XR Origin (AR Foundation requirement for
    // ARMeshManager). Owns the AR* manager components it creates and is responsible
    // for fully releasing them in ReleaseAll().
    public class SceneGeometryProvider : MonoBehaviour
    {
        const string k_UseScenePermission = "com.oculus.permission.USE_SCENE";

        [SerializeField] Material m_RevealMaterial;
        [SerializeField] XROrigin m_XROrigin;

        public event Action<GeometryTier> TierResolved;
        public GeometryTier Tier { get; private set; } = GeometryTier.None;
        public IReadOnlyList<Renderer> Surfaces => m_Surfaces;

        readonly List<Renderer> m_Surfaces = new();
        readonly List<GameObject> m_OwnedObjects = new();

        ARMeshManager m_MeshManager;
        ARPlaneManager m_PlaneManager;
        ARBoundingBoxManager m_BoundingBoxManager;
        GameObject m_ProxyRoom;

        public IEnumerator AcquireAsync(ImmersiveVideoConfig config)
        {
            // Defensive: if a previous acquire's geometry was never released (e.g. the
            // experience was restarted), tear it down before building a new set.
            ReleaseAll();
            Tier = GeometryTier.None;

            var permissionGranted = true;
            yield return EnsurePermission(granted => permissionGranted = granted);

            if (!permissionGranted)
            {
                if (config.verboseLogging)
                    Debug.Log($"[ImmersiveVideo] {k_UseScenePermission} not granted - falling back to proxy room.");
                ResolveProxy(config);
                yield break;
            }

            EnsureManagers();

            m_MeshManager.meshPrefab = BuildMeshTemplate();
            m_MeshManager.enabled = true;
            yield return new WaitForSecondsRealtime(config.sceneDataTimeoutSeconds);

            if (config.verboseLogging)
                Debug.Log($"[ImmersiveVideo] ARMeshManager after {config.sceneDataTimeoutSeconds}s: " +
                    $"{m_MeshManager.meshes.Count} meshes, subsystem running={m_MeshManager.subsystem?.running}.");

            if (m_MeshManager.meshes.Count > 0)
            {
                foreach (var meshFilter in m_MeshManager.meshes)
                {
                    var renderer = meshFilter.GetComponent<Renderer>();
                    if (renderer != null)
                        m_Surfaces.Add(renderer);
                }
                ResolveTier(GeometryTier.SceneMesh, config);
                yield break;
            }
            m_MeshManager.enabled = false;

            if (m_PlaneManager == null)
            {
                if (config.verboseLogging)
                    Debug.Log("[ImmersiveVideo] No ARPlaneManager (missing XR Origin ref?) - falling back to proxy room.");
                ResolveProxy(config);
                yield break;
            }

            m_PlaneManager.planePrefab = BuildPlaneTemplate();
            m_PlaneManager.enabled = true;
            yield return new WaitForSecondsRealtime(config.sceneDataTimeoutSeconds);

            if (config.verboseLogging)
                Debug.Log($"[ImmersiveVideo] ARPlaneManager after {config.sceneDataTimeoutSeconds}s: " +
                    $"{m_PlaneManager.trackables.count} planes (need >= {config.minPlanesForTierB}), " +
                    $"subsystem running={m_PlaneManager.subsystem?.running}.");

            if (m_PlaneManager.trackables.count >= config.minPlanesForTierB)
            {
                foreach (var plane in m_PlaneManager.trackables)
                {
                    var renderer = plane.GetComponent<Renderer>();
                    if (renderer != null)
                        m_Surfaces.Add(renderer);
                }

                if (config.useBoundingBoxes && m_BoundingBoxManager != null)
                    yield return AcquireBoundingBoxes();

                ResolveTier(GeometryTier.Planes, config);
                yield break;
            }
            m_PlaneManager.enabled = false;

            ResolveProxy(config);
        }

        IEnumerator AcquireBoundingBoxes()
        {
            m_BoundingBoxManager.boundingBoxPrefab = BuildBoundingBoxTemplate();
            m_BoundingBoxManager.enabled = true;
            // Bounding boxes come from the same on-device Scene Model as planes, but still
            // populate over a few frames once the subsystem starts - give it a short beat.
            yield return new WaitForSecondsRealtime(0.5f);
            foreach (var box in m_BoundingBoxManager.trackables)
            {
                var renderer = box.GetComponent<Renderer>();
                if (renderer != null)
                    m_Surfaces.Add(renderer);
            }
        }

        void ResolveProxy(ImmersiveVideoConfig config)
        {
            if (!config.allowProxyRoomFallback)
            {
                Tier = GeometryTier.None;
                TierResolved?.Invoke(Tier);
                return;
            }

            BuildProxyRoom(config);
            ResolveTier(GeometryTier.Proxy, config);
        }

        void ResolveTier(GeometryTier tier, ImmersiveVideoConfig config)
        {
            Tier = tier;

            // TASK-11 Warmup: geometry is captured once as a static snapshot (m_Surfaces),
            // so none of the AR managers need to keep running/updating afterward.
            if (m_MeshManager != null)
                m_MeshManager.enabled = false;
            if (m_PlaneManager != null)
                m_PlaneManager.enabled = false;
            if (m_BoundingBoxManager != null)
                m_BoundingBoxManager.enabled = false;

            if (config.verboseLogging)
                Debug.Log($"[ImmersiveVideo] Scene geometry tier resolved: {tier} ({m_Surfaces.Count} surfaces)");
            TierResolved?.Invoke(tier);
        }

        // TASK-11: toggles the already-built reveal surfaces without destroying/reacquiring
        // them, so Warmup can run once and each Intro cycle just shows/hides the same
        // snapshot (unless config.reacquireGeometryEachCycle opts into a fresh scan).
        public void SetSurfacesEnabled(bool enabled)
        {
            foreach (var renderer in m_Surfaces)
            {
                if (renderer != null)
                    renderer.enabled = enabled;
            }
        }

        public void ReleaseAll()
        {
            if (m_MeshManager != null)
                m_MeshManager.enabled = false;
            if (m_PlaneManager != null)
                m_PlaneManager.enabled = false;
            if (m_BoundingBoxManager != null)
                m_BoundingBoxManager.enabled = false;

            foreach (var go in m_OwnedObjects)
            {
                if (go != null)
                    Destroy(go);
            }
            m_OwnedObjects.Clear();

            foreach (var component in m_OwnedXROriginComponents)
            {
                if (component != null)
                    Destroy(component);
            }
            m_OwnedXROriginComponents.Clear();
            m_MeshManager = null;
            m_PlaneManager = null;
            m_BoundingBoxManager = null;

            m_Surfaces.Clear();

            if (m_ProxyRoom != null)
            {
                Destroy(m_ProxyRoom);
                m_ProxyRoom = null;
            }
        }

        IEnumerator EnsurePermission(Action<bool> onResult)
        {
#if UNITY_ANDROID
            if (Permission.HasUserAuthorizedPermission(k_UseScenePermission))
            {
                onResult(true);
                yield break;
            }

            var done = false;
            var granted = false;
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => { granted = true; done = true; };
            callbacks.PermissionDenied += _ => { granted = false; done = true; };
            Permission.RequestUserPermission(k_UseScenePermission, callbacks);

            while (!done)
                yield return null;

            onResult(granted);
#else
            onResult(true);
            yield break;
#endif
        }

        // ARPlaneManager and ARBoundingBoxManager both carry [RequireComponent(typeof(XROrigin))],
        // so unlike ARMeshManager they cannot live on a separate child GameObject - they must be
        // added directly to the XR Origin's own GameObject. Tracked separately from
        // m_OwnedObjects so ReleaseAll destroys just the component, not the XR Origin itself.
        readonly List<Component> m_OwnedXROriginComponents = new();

        void EnsureManagers()
        {
            if (m_MeshManager == null)
                m_MeshManager = GetOrAddChildManager<ARMeshManager>("SceneMeshes");

            if (m_XROrigin == null)
            {
                Debug.LogError("[ImmersiveVideo] SceneGeometryProvider needs an XR Origin reference for " +
                    "ARPlaneManager/ARBoundingBoxManager (both require living on the XR Origin GameObject).", this);
            }
            else
            {
                if (m_PlaneManager == null)
                    m_PlaneManager = GetOrAddOnXROrigin<ARPlaneManager>();
                if (m_BoundingBoxManager == null)
                    m_BoundingBoxManager = GetOrAddOnXROrigin<ARBoundingBoxManager>();
            }

            m_MeshManager.enabled = false;
            if (m_PlaneManager != null)
                m_PlaneManager.enabled = false;
            if (m_BoundingBoxManager != null)
                m_BoundingBoxManager.enabled = false;
        }

        T GetOrAddChildManager<T>(string name) where T : Component
        {
            var child = new GameObject(name);
            child.transform.SetParent(transform, false);
            m_OwnedObjects.Add(child);
            return child.AddComponent<T>();
        }

        T GetOrAddOnXROrigin<T>() where T : Component
        {
            var existing = m_XROrigin.GetComponent<T>();
            if (existing != null)
                return existing;

            var added = m_XROrigin.gameObject.AddComponent<T>();
            m_OwnedXROriginComponents.Add(added);
            return added;
        }

        MeshFilter BuildMeshTemplate()
        {
            var go = new GameObject("SceneMeshSurface");
            go.SetActive(false);
            m_OwnedObjects.Add(go);
            var meshFilter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = m_RevealMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return meshFilter;
        }

        GameObject BuildPlaneTemplate()
        {
            var go = new GameObject("ScenePlaneSurface");
            go.SetActive(false);
            m_OwnedObjects.Add(go);
            go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = m_RevealMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<ARPlane>();
            go.AddComponent<ARPlaneMeshVisualizer>();
            return go;
        }

        GameObject BuildBoundingBoxTemplate()
        {
            var go = new GameObject("SceneBoundingBoxSurface");
            go.SetActive(false);
            m_OwnedObjects.Add(go);

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(go.transform, false);
            Destroy(cube.GetComponent<Collider>());
            var renderer = cube.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = m_RevealMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var boundingBox = go.AddComponent<ARBoundingBox>();
            var sizeSync = go.AddComponent<BoundingBoxSizeSync>();
            sizeSync.Init(boundingBox, cube.transform);
            return go;
        }

        void BuildProxyRoom(ImmersiveVideoConfig config)
        {
            if (m_ProxyRoom != null)
                Destroy(m_ProxyRoom);

            var camera = m_XROrigin != null ? m_XROrigin.Camera : Camera.main;
            var headPos = camera != null ? camera.transform.position : transform.position;
            var headYaw = camera != null ? camera.transform.eulerAngles.y : 0f;

            var floorY = ResolveFloorY(headPos);
            var size = config.proxyRoomSize;
            var center = new Vector3(headPos.x, floorY + size.y * 0.5f, headPos.z);

            m_ProxyRoom = GameObject.CreatePrimitive(PrimitiveType.Cube);
            m_ProxyRoom.name = "ProxyRoom";
            Destroy(m_ProxyRoom.GetComponent<Collider>());
            m_ProxyRoom.transform.position = center;
            m_ProxyRoom.transform.rotation = Quaternion.Euler(0f, headYaw, 0f);
            m_ProxyRoom.transform.localScale = size;

            var renderer = m_ProxyRoom.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = m_RevealMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            m_Surfaces.Add(renderer);
        }

        float ResolveFloorY(Vector3 headPos)
        {
            if (m_XROrigin != null && m_XROrigin.CurrentTrackingOriginMode == TrackingOriginModeFlags.Floor)
                return m_XROrigin.Origin.transform.position.y;

            // Device-relative tracking origin: no authoritative floor height available.
            // Approximate using a typical standing eye height - acceptable per spec,
            // this fallback only affects the Tier C proxy room's visual placement.
            const float assumedEyeHeight = 1.6f;
            return headPos.y - assumedEyeHeight;
        }
    }

    // Keeps a bounding-box reveal cube's visual scale matched to the trackable's
    // detected size, which AR Foundation updates independently of the transform.
    class BoundingBoxSizeSync : MonoBehaviour
    {
        ARBoundingBox m_BoundingBox;
        Transform m_Cube;

        public void Init(ARBoundingBox boundingBox, Transform cube)
        {
            m_BoundingBox = boundingBox;
            m_Cube = cube;
        }

        void Update()
        {
            if (m_BoundingBox != null && m_Cube != null)
                m_Cube.localScale = m_BoundingBox.size;
        }
    }
}
