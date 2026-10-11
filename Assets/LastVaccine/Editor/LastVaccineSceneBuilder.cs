#if UNITY_EDITOR
using System.Collections.Generic;
using LastVaccine;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class LastVaccineSceneBuilder
{
    const string SourceScene = "Assets/Scenes/SampleScene.unity";
    const string GameplayScene = "Assets/Scenes/ZombieLastVaccine.unity";

    [MenuItem("Tools/Last Vaccine/Build Gameplay Scene")]
    public static void BuildGameplayScene()
    {
        if (!AssetDatabase.IsValidFolder("Assets/LastVaccine")) AssetDatabase.CreateFolder("Assets", "LastVaccine");
        Scene scene = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single);
        EditorSceneManager.SaveScene(scene, GameplayScene, true);

        RemoveExistingPrototype();
        ConfigureEnvironment();
        GameObject root = new GameObject("=== ZOMBIE THE LAST VACCINE ===");
        GameObject gameplay = new GameObject("Gameplay Systems"); gameplay.transform.SetParent(root.transform);
        GameObject markers = new GameObject("Level Markers"); markers.transform.SetParent(root.transform);
        GameObject templates = new GameObject("Enemy Templates (Runtime Only)"); templates.transform.SetParent(root.transform);

        FpsSurvivor player = CreatePlayer(gameplay.transform);
        CreateSafetyBounds(markers.transform);
        Transform[] spawns = CreateSpawnPoints(markers.transform);
        VaccineZone[] zones = CreateZones(markers.transform);
        VaccinePickup vaccine = CreateVaccine(gameplay.transform);
        ZombieAgent bunny = CreateZombieTemplate("Zombunny Template", ZombieKind.Bunny, templates.transform);
        ZombieAgent bear = CreateZombieTemplate("ZomBear Template", ZombieKind.Bear, templates.transform);

        LastVaccineGame manager = new GameObject("Game Manager").AddComponent<LastVaccineGame>();
        manager.transform.SetParent(gameplay.transform);
        manager.player = player;
        manager.bunnyTemplate = bunny;
        manager.bearTemplate = bear;
        manager.enemySpawnPoints = spawns;
        manager.vaccine = vaccine;
        manager.zones = zones;

        BakeNavigation(root);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, GameplayScene);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(GameplayScene, true), new EditorBuildSettingsScene(SourceScene, true) };
        Selection.activeGameObject = root;
        SceneView.lastActiveSceneView?.FrameSelected();
        Debug.Log("[Last Vaccine] Gameplay scene built successfully: " + GameplayScene);
    }

    static void RemoveExistingPrototype()
    {
        GameObject old = GameObject.Find("=== ZOMBIE THE LAST VACCINE ===");
        if (old != null) Object.DestroyImmediate(old);
    }

    static void ConfigureEnvironment()
    {
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.012f, .008f, .022f);
        RenderSettings.fog = true;
        RenderSettings.fogColor = new Color(.006f, .004f, .012f);
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = .018f;
        Light directional = GameObject.Find("Directional Light")?.GetComponent<Light>();
        if (directional != null) { directional.intensity = .035f; directional.color = new Color(.35f, .42f, .65f); directional.shadows = LightShadows.None; }
        GameObject environment = GameObject.Find("Environment");
        if (environment == null)
        {
            GameObject environmentPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ShooterAsset/Prefabs/Environment.prefab");
            environment = PrefabUtility.InstantiatePrefab(environmentPrefab) as GameObject;
            if (environment != null)
            {
                environment.name = "Environment";
                environment.transform.position = new Vector3(-1.44528f, 0f, 1.36822f);
            }
        }
        if (environment != null)
        {
            foreach (Renderer r in environment.GetComponentsInChildren<Renderer>(true)) r.gameObject.isStatic = true;
            foreach (Collider c in environment.GetComponentsInChildren<Collider>(true)) c.gameObject.isStatic = true;
        }
    }

    static FpsSurvivor CreatePlayer(Transform parent)
    {
        GameObject go = new GameObject("Player FPS");
        go.tag = "Player";
        go.transform.SetParent(parent);
        go.transform.position = new Vector3(0f, .15f, -2f);
        CharacterController cc = go.AddComponent<CharacterController>();
        cc.height = 1.8f; cc.radius = .38f; cc.center = new Vector3(0f, .9f, 0f); cc.stepOffset = .35f;
        FpsSurvivor player = go.AddComponent<FpsSurvivor>();

        GameObject playerModel = PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ShooterAsset/Models/Characters/Player.fbx")) as GameObject;
        if (playerModel != null)
        {
            playerModel.name = "Player Model (Shadows Only)";
            playerModel.transform.SetParent(go.transform, false);
            playerModel.transform.localPosition = Vector3.zero;
            foreach (Renderer renderer in playerModel.GetComponentsInChildren<Renderer>()) renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        }

        Camera cam = GameObject.Find("Main Camera")?.GetComponent<Camera>();
        if (cam == null) { GameObject cameraObject = new GameObject("Main Camera"); cameraObject.tag = "MainCamera"; cam = cameraObject.AddComponent<Camera>(); cameraObject.AddComponent<AudioListener>(); }
        cam.transform.SetParent(go.transform, false);
        cam.transform.localPosition = new Vector3(0f, 1.58f, 0f);
        cam.transform.localRotation = Quaternion.identity;
        cam.fieldOfView = 72f; cam.nearClipPlane = .05f;
        player.playerCamera = cam;

        GameObject visibility = new GameObject("Player Visibility Light (4.5m)");
        visibility.transform.SetParent(cam.transform, false);
        visibility.transform.localPosition = Vector3.zero;
        Light visibilityLight = visibility.AddComponent<Light>();
        visibilityLight.type = LightType.Point;
        visibilityLight.range = 4.5f;
        visibilityLight.intensity = 2.8f;
        visibilityLight.color = new Color(.62f, .76f, 1f);
        visibilityLight.shadows = LightShadows.Soft;

        GameObject weapon = GameObject.CreatePrimitive(PrimitiveType.Cube);
        weapon.name = "Emergency Sidearm";
        Object.DestroyImmediate(weapon.GetComponent<Collider>());
        weapon.transform.SetParent(cam.transform, false);
        weapon.transform.localPosition = new Vector3(.32f, -.28f, .62f);
        weapon.transform.localRotation = Quaternion.Euler(3f, 0f, 0f);
        weapon.transform.localScale = new Vector3(.16f, .18f, .55f);
        weapon.GetComponent<Renderer>().sharedMaterial = CreateRuntimeMaterial("WeaponMaterial", new Color(.06f, .08f, .12f), new Color(0f, .12f, .18f));
        GameObject muzzle = new GameObject("Muzzle"); muzzle.transform.SetParent(weapon.transform, false); muzzle.transform.localPosition = new Vector3(0f, 0f, .62f);
        Light flash = muzzle.AddComponent<Light>(); flash.type = LightType.Point; flash.range = 4f; flash.intensity = 4f; flash.color = new Color(.3f, .8f, 1f); flash.shadows = LightShadows.None; flash.enabled = false;
        player.muzzle = muzzle.transform; player.muzzleLight = flash;
        AudioSource audio = go.AddComponent<AudioSource>(); audio.spatialBlend = 0f; player.audioSource = audio;
        player.gunClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/ShooterAsset/Audio/Effects/Player GunShot.wav");
        player.hurtClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/ShooterAsset/Audio/Effects/Player Hurt.wav");
        player.deathClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/ShooterAsset/Audio/Effects/Player Death.wav");
        player.hitEffectPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ShooterAsset/Prefabs/HitParticles.prefab");
        return player;
    }

    static Transform[] CreateSpawnPoints(Transform parent)
    {
        Vector3[] positions = { new(-24f,.1f,-18f), new(23f,.1f,-18f), new(-26f,.1f,10f), new(26f,.1f,12f), new(-12f,.1f,27f), new(14f,.1f,28f), new(-4f,.1f,-24f), new(28f,.1f,-2f) };
        Transform[] result = new Transform[positions.Length];
        GameObject group = new GameObject("Enemy Spawn Points"); group.transform.SetParent(parent);
        for (int i = 0; i < positions.Length; i++) { GameObject p = new GameObject($"Enemy Spawn {i + 1:00}"); p.transform.SetParent(group.transform); p.transform.position = positions[i]; result[i] = p.transform; }
        return result;
    }

    static void CreateSafetyBounds(Transform parent)
    {
        GameObject group = new GameObject("Invisible Gameplay Bounds");
        group.transform.SetParent(parent);
        CreateBound("Walkable Floor", group.transform, new Vector3(0f, -.2f, 4f), new Vector3(66f, .4f, 66f));
        CreateBound("North Boundary", group.transform, new Vector3(0f, 4f, 36f), new Vector3(68f, 8f, 1f));
        CreateBound("South Boundary", group.transform, new Vector3(0f, 4f, -28f), new Vector3(68f, 8f, 1f));
        CreateBound("East Boundary", group.transform, new Vector3(33f, 4f, 4f), new Vector3(1f, 8f, 66f));
        CreateBound("West Boundary", group.transform, new Vector3(-33f, 4f, 4f), new Vector3(1f, 8f, 66f));
    }

    static void CreateBound(string name, Transform parent, Vector3 position, Vector3 size)
    {
        GameObject bound = new GameObject(name);
        bound.transform.SetParent(parent);
        bound.transform.position = position;
        BoxCollider collider = bound.AddComponent<BoxCollider>();
        collider.size = size;
        bound.isStatic = true;
    }

    static VaccineZone[] CreateZones(Transform parent)
    {
        string[] ids = { "A", "B", "C", "D", "E", "F" };
        string[] names = { "BLOCK FORT", "DOLLHOUSE", "TOY TRACK", "DRAWERS", "FIRETRUCK", "CLOCK CORNER" };
        string[] landmarks = { "scattered building blocks", "the painted dollhouse", "the wooden train", "the tall purple drawers", "the red firetruck", "the old clock" };
        Vector3[] positions = { new(-8f,2.5f,-7f), new(-21f,2.5f,8f), new(-15f,2.5f,-1f), new(4f,2.5f,24f), new(20f,2.5f,4f), new(10f,2.5f,18f) };
        VaccineZone[] result = new VaccineZone[positions.Length];
        GameObject group = new GameObject("Light Zones and Vaccine Points"); group.transform.SetParent(parent);
        for (int i = 0; i < positions.Length; i++)
        {
            GameObject zone = new GameObject($"Zone {ids[i]} - {names[i]}"); zone.transform.SetParent(group.transform); zone.transform.position = positions[i];
            Light light = zone.AddComponent<Light>(); light.type = LightType.Point; light.range = 17f; light.intensity = 8f; light.color = new Color(.35f, .72f, 1f); light.shadows = LightShadows.None; light.enabled = false;
            GameObject point = new GameObject("Vaccine Spawn Point"); point.transform.SetParent(zone.transform); point.transform.localPosition = new Vector3(0f, -2.05f, 0f);
            result[i] = new VaccineZone { id = ids[i], displayName = names[i], landmark = landmarks[i], vaccinePoint = point.transform, zoneLight = light };
        }
        return result;
    }

    static VaccinePickup CreateVaccine(Transform parent)
    {
        GameObject root = new GameObject("Vaccine (Hidden Until Final Search)"); root.transform.SetParent(parent);
        CapsuleCollider trigger = root.AddComponent<CapsuleCollider>(); trigger.isTrigger = true; trigger.height = 1.2f; trigger.radius = .55f;
        VaccinePickup pickup = root.AddComponent<VaccinePickup>();
        GameObject vial = GameObject.CreatePrimitive(PrimitiveType.Cylinder); vial.name = "Cyan Vaccine Vial"; Object.DestroyImmediate(vial.GetComponent<Collider>()); vial.transform.SetParent(root.transform, false); vial.transform.localScale = new Vector3(.32f, .65f, .32f); vial.transform.localPosition = new Vector3(0f, .65f, 0f);
        vial.GetComponent<Renderer>().sharedMaterial = CreateRuntimeMaterial("VaccineMaterial", new Color(.04f, .9f, 1f), new Color(0f, 3f, 4f));
        GameObject cap = GameObject.CreatePrimitive(PrimitiveType.Cylinder); cap.name = "Vaccine Cap"; Object.DestroyImmediate(cap.GetComponent<Collider>()); cap.transform.SetParent(root.transform, false); cap.transform.localScale = new Vector3(.38f, .12f, .38f); cap.transform.localPosition = new Vector3(0f, 1.38f, 0f); cap.GetComponent<Renderer>().sharedMaterial = CreateRuntimeMaterial("VaccineCapMaterial", new Color(.15f, .18f, .25f), Color.black);
        Light glow = root.AddComponent<Light>(); glow.type = LightType.Point; glow.range = 5f; glow.intensity = 2.2f; glow.color = Color.cyan; glow.shadows = LightShadows.None;
        root.SetActive(false);
        return pickup;
    }

    static ZombieAgent CreateZombieTemplate(string name, ZombieKind kind, Transform parent)
    {
        GameObject root = new GameObject(name); root.transform.SetParent(parent); root.transform.position = new Vector3(0f, -50f, 0f);
        string modelPath = kind == ZombieKind.Bunny ? "Assets/ShooterAsset/Models/Characters/Zombunny.fbx" : "Assets/ShooterAsset/Models/Characters/ZomBear.fbx";
        GameObject model = PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath)) as GameObject;
        if (model != null) { model.name = kind + " Visual"; model.transform.SetParent(root.transform, false); }
        CapsuleCollider capsule = root.AddComponent<CapsuleCollider>(); capsule.height = kind == ZombieKind.Bunny ? 1.7f : 2.2f; capsule.radius = kind == ZombieKind.Bunny ? .42f : .62f; capsule.center = new Vector3(0f, capsule.height * .5f, 0f);
        NavMeshAgent nav = root.AddComponent<NavMeshAgent>(); nav.radius = capsule.radius; nav.height = capsule.height; nav.baseOffset = 0f; nav.acceleration = 24f; nav.autoBraking = true; nav.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
        AudioSource audio = root.AddComponent<AudioSource>(); audio.playOnAwake = false;
        ZombieAgent agent = root.AddComponent<ZombieAgent>(); agent.kind = kind;
        if (kind == ZombieKind.Bunny)
        {
            agent.maxHealth = 50; agent.moveSpeed = 5.5f; agent.attackDamage = 10; agent.attackCooldown = 1f;
            agent.hurtClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/ShooterAsset/Audio/Effects/ZomBunny Hurt.wav");
            agent.deathClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/ShooterAsset/Audio/Effects/ZomBunny Death.wav");
        }
        else
        {
            agent.maxHealth = 150; agent.moveSpeed = 2.5f; agent.attackDamage = 20; agent.attackCooldown = 1.5f;
            agent.hurtClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/ShooterAsset/Audio/Effects/ZomBear Hurt.wav");
            agent.deathClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/ShooterAsset/Audio/Effects/ZomBear Death.wav");
        }
        nav.speed = agent.moveSpeed;
        root.SetActive(false);
        return agent;
    }

    static void BakeNavigation(GameObject root)
    {
        NavMeshSurface surface = root.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
        surface.layerMask = ~0;
        surface.BuildNavMesh();
    }

    static Material CreateRuntimeMaterial(string name, Color color, Color emission)
    {
        string folder = "Assets/LastVaccine/Materials";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/LastVaccine", "Materials");
        string path = folder + "/" + name + ".mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            mat = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.color = color;
        if (emission.maxColorComponent > 0f) { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", emission); }
        EditorUtility.SetDirty(mat);
        return mat;
    }
}
#endif
