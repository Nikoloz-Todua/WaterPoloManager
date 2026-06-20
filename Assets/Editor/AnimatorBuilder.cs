using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.SceneManagement;

// Editor tooling for the human red-team swimmer's dual-body (front/back) SPRITE-SWAP animation.
//
//   Tools > Setup All Players          <-- run this FIRST (builds controllers + plain bodies)
//   Tools > Wire Animation Clips       <-- then this (fixes idle/hold/defend clips + assigns all)
//   Tools > Build Player Animator Controllers
//   Tools > Setup Player GameObjects
//   Tools > Setup BoneBody All Players <-- adds the bone-rigged idle-float body to all six players
//   Tools > Setup HoldBody All Players <-- adds the bone-rigged ball-holding body to all six players
//
// Architecture: each red swimmer (PlayerAnimator) has two PLAIN SpriteRenderer + Animator children,
// FrontBody and BackBody, shown one at a time by velocity.y. FrontBody runs PlayerFrontAnimation,
// BackBody runs PlayerBackAnimation. Every swim/etc clip is sprite-swap (animates m_Sprite).
// Two more SpriteSkin bone children are shown instead of the flat sprites in specific states:
// BoneBody (test_0 prefab / BoneBodyAnimation) while floating, and HoldBody (hold_0 prefab /
// HoldBodyAnimation) while holding the ball. (Bots/blue use BotAnimator.)
public static class AnimatorBuilder
{
    const string PlayersDir = "Assets/Sprites/Players";
    const string AnimDir = PlayersDir + "/Animations";
    const string PartsDir = PlayersDir + "/Parts";
    const string FrontControllerPath = AnimDir + "/PlayerFrontAnimation.controller";
    const string BackControllerPath = AnimDir + "/PlayerBackAnimation.controller";
    const string BonePrefabPath = PlayersDir + "/test_0.prefab";
    const string BoneControllerPath = AnimDir + "/BoneBodyAnimation.controller";
    const string HoldPrefabPath = PlayersDir + "/hold_0.prefab";
    const string HoldControllerPath = AnimDir + "/HoldBodyAnimation.controller";
    const string BackBonePrefabPath = PlayersDir + "/test-back_0.prefab";
    const string BackBoneControllerPath = AnimDir + "/BackBoneBodyAnimation.controller";
    const string BackHoldPrefabPath = PlayersDir + "/back-side_0.prefab";
    const string BackHoldControllerPath = AnimDir + "/BackHoldBodyAnimation.controller";

    // The six human red-team swimmers in SampleScene, by GameObject name.
    static readonly string[] PlayerNames =
        { "Player", "Player2", "Player3", "Player4", "Player5", "Player6" };

    // Plain-body scale (the part sprites are authored large, so 0.07 fits them to the pool).
    static readonly Vector3 BodyScale = new Vector3(0.07f, 0.07f, 1f);

    // The seven animation states, shared by BOTH controllers so PlayerAnimator drives them
    // identically; only the assigned clip differs (front clip vs *_back clip).
    static readonly string[] States =
        { "floating", "swimming", "sprinting", "holding", "throwing", "defending", "stealing" };

    // Every parameter the runtime PlayerAnimator drives.
    static readonly (string name, AnimatorControllerParameterType type)[] Parameters =
    {
        ("Speed",       AnimatorControllerParameterType.Float),
        ("IsHolding",   AnimatorControllerParameterType.Bool),
        ("IsSprinting", AnimatorControllerParameterType.Bool),
        ("IsDefending", AnimatorControllerParameterType.Bool),
        ("IsExcluded",  AnimatorControllerParameterType.Bool),
        ("IsShooting",  AnimatorControllerParameterType.Trigger),
        ("IsStealing",  AnimatorControllerParameterType.Trigger),
    };

    // ======================================================================================
    //  ONE-BUTTON ENTRY POINT
    // ======================================================================================

    [MenuItem("Tools/Setup All Players")]
    public static void SetupAllPlayers()
    {
        BuildControllers();
        SetupPlayerGameObjects();
        Debug.Log("[AnimatorBuilder] Setup All Players done. NEXT: run Tools > Wire Animation Clips, " +
                  "then CHECK each Player's PlayerAnimator slots, SAVE the scene (Ctrl+S) and press Play.");
    }

    // ======================================================================================
    //  Tools > Wire Animation Clips — fix the idle/hold/defend clips + assign every state's clip
    // ======================================================================================
    [MenuItem("Tools/Wire Animation Clips")]
    public static void WireAnimationClips()
    {
        // STEPS 4 & 5 — floating/holding/defending (+ back) are leftover BONE clips; convert them to
        // static looping sprite-swap so they work on plain SpriteRenderer bodies. The other states
        // (swimming/sprinting/throwing/stealing) are already sprite-swap and are left untouched.
        ConvertToStaticSwap("floating",       "test");
        ConvertToStaticSwap("floating_back",  "test-back");
        ConvertToStaticSwap("holding",        "hold");
        ConvertToStaticSwap("hold-back",      "hold-back");
        ConvertToStaticSwap("defending",      "defend");
        ConvertToStaticSwap("defending_back", "defend-back");

        // STEPS 2 & 3 — assign each state's Motion (transitions/parameters left untouched).
        WireClips(FrontControllerPath, new (string state, string[] clips)[]
        {
            ("floating",  new[] { "floating"  }),
            ("swimming",  new[] { "swimming"  }),
            ("sprinting", new[] { "sprinting" }),
            ("holding",   new[] { "holding"   }),
            ("throwing",  new[] { "throwing"  }),
            ("defending", new[] { "defending" }),
            ("stealing",  new[] { "stealing"  }),
        });
        WireClips(BackControllerPath, new (string state, string[] clips)[]
        {
            ("floating",  new[] { "floating_back"  }),
            ("swimming",  new[] { "swimming_back"  }),
            ("sprinting", new[] { "sprinting_back" }),
            ("holding",   new[] { "hold-back"      }),
            ("throwing",  new[] { "throwing_back"  }),
            ("defending", new[] { "defending_back" }),
            ("stealing",  new[] { "stealing_back"  }),
        });

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[AnimatorBuilder] Wire Animation Clips done. SAVE the scene (Ctrl+S) and press Play.");
    }

    // Assign each state's Motion from the first existing clip candidate. Only the motion changes.
    static void WireClips(string controllerPath, (string state, string[] clips)[] map)
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (controller == null)
        {
            Debug.LogError($"[AnimatorBuilder] Controller not found: {controllerPath} — run " +
                           "Tools > Build Player Animator Controllers first.");
            return;
        }

        string ctrlName = System.IO.Path.GetFileName(controllerPath);
        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        foreach (var (stateName, clips) in map)
        {
            AnimatorState state = FindState(sm, stateName);
            if (state == null)
            {
                Debug.LogError($"[AnimatorBuilder] {ctrlName}: state '{stateName}' not found — skipped.");
                continue;
            }
            AnimationClip clip = LoadClip(clips);
            if (clip == null) continue; // LoadClip already logged the miss

            state.motion = clip;
            Debug.Log($"[AnimatorBuilder] {ctrlName}: '{stateName}' -> {clip.name}.anim");
        }
        EditorUtility.SetDirty(controller);
    }

    // Strip a clip's curves (bone euler/position from the old rig, or an old sprite curve) and set a
    // single sprite held for 1s (frame 0 + frame 60 @ 60fps), looping — a static sprite-swap pose.
    static void ConvertToStaticSwap(string clipName, string spriteName)
    {
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{AnimDir}/{clipName}.anim");
        if (clip == null) { Debug.LogError($"[AnimatorBuilder] Clip not found: {clipName}.anim in {AnimDir}"); return; }
        Sprite sprite = LoadSprite(spriteName);
        if (sprite == null) return; // LoadSprite logged it

        clip.ClearCurves();
        foreach (EditorCurveBinding b in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            AnimationUtility.SetObjectReferenceCurve(clip, b, null);

        clip.frameRate = 60f;
        var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
        var keys = new[]
        {
            new ObjectReferenceKeyframe { time = 0f, value = sprite },
            new ObjectReferenceKeyframe { time = 1f, value = sprite }, // 60 frames @ 60fps
        };
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

        clip.wrapMode = WrapMode.Loop;
        AnimationClipSettings s = AnimationUtility.GetAnimationClipSettings(clip);
        s.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, s);

        EditorUtility.SetDirty(clip);
        Debug.Log($"[AnimatorBuilder] '{clipName}' -> static sprite-swap ({spriteName}).");
    }

    // ======================================================================================
    //  Build the two controllers (states + parameters + transition priorities)
    // ======================================================================================

    [MenuItem("Tools/Build Player Animator Controllers")]
    public static void BuildControllers()
    {
        EnsureFolder(AnimDir);
        BuildController(FrontControllerPath);
        BuildController(BackControllerPath);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[AnimatorBuilder] Controllers built. NEXT: Tools > Setup Player GameObjects.");
    }

    static void BuildController(string controllerPath)
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            Debug.Log($"[AnimatorBuilder] Created new controller at {controllerPath}");
        }

        foreach (var (name, type) in Parameters) EnsureParameter(controller, name, type);

        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        // Ensure each state exists; the clip (Motion) is assigned by Tools > Wire Animation Clips.
        foreach (string stateName in States) EnsureState(sm, stateName);

        AnimatorState floating = FindState(sm, "floating");
        if (floating != null) sm.defaultState = floating; // start idle/floating

        WireTransitions(sm);
        EditorUtility.SetDirty(controller);
        Debug.Log($"[AnimatorBuilder] Wired {controllerPath}");
    }

    // All seven transitions come from Any State (hasExitTime=false, duration=0.05). Unity evaluates
    // Any State transitions TOP-DOWN, first match wins — so order is priority. Triggers go first
    // (else the generic "swimming" rule would shadow them on shot release); holding sits above
    // sprinting/swimming so a carrier is caught first; floating is the catch-all idle fallback.
    // Every rule also carries its explicit conditions so it's correct regardless of order.
    static void WireTransitions(AnimatorStateMachine sm)
    {
        AnimatorState floating  = FindState(sm, "floating");
        AnimatorState swimming  = FindState(sm, "swimming");
        AnimatorState sprinting = FindState(sm, "sprinting");
        AnimatorState holding   = FindState(sm, "holding");
        AnimatorState throwing  = FindState(sm, "throwing");
        AnimatorState defending = FindState(sm, "defending");
        AnimatorState stealing  = FindState(sm, "stealing");

        if (floating == null || swimming == null || sprinting == null || holding == null ||
            throwing == null || defending == null || stealing == null)
        {
            Debug.LogError("[AnimatorBuilder] One or more states missing — aborting transition wiring.");
            return;
        }

        // Clear only OUR any-state transitions (to these states) first, so re-running is idempotent.
        var targets = new HashSet<AnimatorState>
            { floating, swimming, sprinting, holding, throwing, defending, stealing };
        foreach (AnimatorStateTransition t in sm.anyStateTransitions.ToArray())
            if (t.destinationState != null && targets.Contains(t.destinationState))
                sm.RemoveAnyStateTransition(t);

        // throwing: IsShooting trigger AND IsHolding = false
        AnimatorStateTransition toThrow = AnyTo(sm, throwing);
        toThrow.AddCondition(AnimatorConditionMode.If,    0f, "IsShooting");
        toThrow.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsHolding");

        // stealing: IsStealing trigger
        AnyTo(sm, stealing).AddCondition(AnimatorConditionMode.If, 0f, "IsStealing");

        // holding: IsHolding = true (wins over swimming and sprinting)
        AnyTo(sm, holding).AddCondition(AnimatorConditionMode.If, 0f, "IsHolding");

        // defending: IsDefending = true AND IsHolding = false
        AnimatorStateTransition toDefend = AnyTo(sm, defending);
        toDefend.AddCondition(AnimatorConditionMode.If,    0f, "IsDefending");
        toDefend.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsHolding");

        // sprinting: IsSprinting = true AND IsHolding = false
        AnimatorStateTransition toSprint = AnyTo(sm, sprinting);
        toSprint.AddCondition(AnimatorConditionMode.If,    0f, "IsSprinting");
        toSprint.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsHolding");

        // swimming: Speed > 0.1 AND IsHolding = false AND IsSprinting = false
        AnimatorStateTransition toSwim = AnyTo(sm, swimming);
        toSwim.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
        toSwim.AddCondition(AnimatorConditionMode.IfNot,   0f,   "IsHolding");
        toSwim.AddCondition(AnimatorConditionMode.IfNot,   0f,   "IsSprinting");

        // floating: Speed < 0.05 AND IsHolding = false (catch-all idle)
        AnimatorStateTransition toFloat = AnyTo(sm, floating);
        toFloat.AddCondition(AnimatorConditionMode.Less,  0.05f, "Speed");
        toFloat.AddCondition(AnimatorConditionMode.IfNot, 0f,    "IsHolding");
    }

    static AnimatorStateTransition AnyTo(AnimatorStateMachine sm, AnimatorState to)
    {
        AnimatorStateTransition t = sm.AddAnyStateTransition(to);
        t.hasExitTime = false;
        t.exitTime = 0f;
        t.hasFixedDuration = true;
        t.duration = 0.05f;
        t.canTransitionToSelf = false;
        return t;
    }

    // ======================================================================================
    //  Setup each player's FrontBody / BackBody (plain SpriteRenderer bodies) + wire slots
    // ======================================================================================

    [MenuItem("Tools/Setup Player GameObjects")]
    public static void SetupPlayerGameObjects()
    {
        AnimatorController frontCtrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(FrontControllerPath);
        AnimatorController backCtrl  = AssetDatabase.LoadAssetAtPath<AnimatorController>(BackControllerPath);
        if (frontCtrl == null || backCtrl == null)
        {
            Debug.LogError("[AnimatorBuilder] Controllers missing — run Tools > Build Player " +
                           "Animator Controllers FIRST.");
            return;
        }

        // Default/rest sprites so a body isn't invisible before the first clip frame plays.
        Sprite frontRest = LoadSprite("test");
        Sprite backRest  = LoadSprite("test-back");

        // Only the human red-team swimmers carry PlayerAnimator (bots/blue use BotAnimator).
        PlayerAnimator[] players = Object.FindObjectsByType<PlayerAnimator>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (players.Length == 0)
        {
            Debug.LogWarning("[AnimatorBuilder] No PlayerAnimator in the open scene. Open " +
                             "Assets/Scenes/SampleScene.unity and run again.");
            return;
        }

        var dirtyScenes = new HashSet<Scene>();
        foreach (PlayerAnimator pa in players)
        {
            SetupOne(pa, frontCtrl, backCtrl, frontRest, backRest);
            dirtyScenes.Add(pa.gameObject.scene);
        }

        // Mark each touched scene dirty so YOU review + save it. We never auto-save the scene.
        foreach (Scene scene in dirtyScenes)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log($"[AnimatorBuilder] Set up {players.Length} player(s) with plain bodies. CHECK the " +
                  "FrontBody/BackBody slots on each Player's PlayerAnimator, run Wire Animation Clips, then SAVE.");
    }

    static void SetupOne(PlayerAnimator pa, AnimatorController frontCtrl, AnimatorController backCtrl,
                         Sprite frontRest, Sprite backRest)
    {
        GameObject root = pa.gameObject;

        SpriteRenderer front = EnsureBody(root, "FrontBody", frontCtrl, frontRest);
        SpriteRenderer back  = EnsureBody(root, "BackBody",  backCtrl,  backRest);

        // Wire the four serialized (private) slots on PlayerAnimator via SerializedObject so the
        // change is recorded for Undo and marks the component dirty.
        var so = new SerializedObject(pa);
        SetRef(so, "frontAnimator", front.GetComponent<Animator>());
        SetRef(so, "backAnimator",  back.GetComponent<Animator>());
        SetRef(so, "frontRenderer", front);
        SetRef(so, "backRenderer",  back);
        so.ApplyModifiedProperties();

        // Disable the parent's own body renderer (and now-orphaned Animator) so it doesn't overlap
        // the children. Reversible; PlayerAnimator no longer drives the parent.
        SpriteRenderer parentSr = root.GetComponent<SpriteRenderer>();
        if (parentSr != null && parentSr.enabled)
        {
            Undo.RecordObject(parentSr, "Disable parent body renderer");
            parentSr.enabled = false;
            EditorUtility.SetDirty(parentSr);
        }
        Animator parentAnim = root.GetComponent<Animator>();
        if (parentAnim != null && parentAnim.enabled)
        {
            Undo.RecordObject(parentAnim, "Disable parent body animator");
            parentAnim.enabled = false;
            EditorUtility.SetDirty(parentAnim);
        }
    }

    // Delete any existing body child of this name (an old bone-rig prefab instance OR a previous
    // plain body) and build a fresh plain SpriteRenderer + Animator body.
    static SpriteRenderer EnsureBody(GameObject root, string childName, AnimatorController controller,
                                     Sprite restSprite)
    {
        Transform existing = root.transform.Find(childName);
        if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

        GameObject body = new GameObject(childName);
        Undo.RegisterCreatedObjectUndo(body, $"Create {childName}");
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = Vector3.zero;
        body.transform.localRotation = Quaternion.identity;
        body.transform.localScale = BodyScale;

        SpriteRenderer sr = Undo.AddComponent<SpriteRenderer>(body);
        if (restSprite != null) sr.sprite = restSprite;
        // Copy sorting + material off the parent's old body renderer so the body renders in the same
        // layer/order (not behind the pool).
        SpriteRenderer parentSr = root.GetComponent<SpriteRenderer>();
        if (parentSr != null)
        {
            sr.sortingLayerID = parentSr.sortingLayerID;
            sr.sortingOrder = parentSr.sortingOrder;
            sr.sharedMaterial = parentSr.sharedMaterial;
        }

        Animator anim = Undo.AddComponent<Animator>(body);
        anim.runtimeAnimatorController = controller;
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        EditorUtility.SetDirty(body);
        return sr;
    }

    static void SetRef(SerializedObject so, string prop, Object value)
    {
        SerializedProperty p = so.FindProperty(prop);
        if (p == null) { Debug.LogError($"[AnimatorBuilder] PlayerAnimator has no serialized field '{prop}'."); return; }
        p.objectReferenceValue = value;
    }

    // ======================================================================================
    //  Setup the bone-rigged BoneBody child (idle float body) on all six players + wire slots
    // ======================================================================================

    [MenuItem("Tools/Setup BoneBody All Players")]
    public static void SetupBoneBodyAllPlayers()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BonePrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[AnimatorBuilder] BoneBody prefab not found at {BonePrefabPath}.");
            return;
        }
        AnimatorController boneCtrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(BoneControllerPath);
        if (boneCtrl == null)
        {
            Debug.LogError($"[AnimatorBuilder] BoneBodyAnimation controller not found at {BoneControllerPath}.");
            return;
        }

        // Match the six named players against the PlayerAnimators in the open scene (includes inactive).
        PlayerAnimator[] all = Object.FindObjectsByType<PlayerAnimator>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        int added = 0, skipped = 0, missing = 0;
        var dirtyScenes = new HashSet<Scene>();
        foreach (string name in PlayerNames)
        {
            PlayerAnimator pa = all.FirstOrDefault(p => p.gameObject.name == name);
            if (pa == null)
            {
                Debug.LogWarning($"[AnimatorBuilder] Player '{name}' (with PlayerAnimator) not found in " +
                                 "the open scene — skipped.");
                missing++;
                continue;
            }

            GameObject player = pa.gameObject;
            if (player.transform.Find("BoneBody") != null) { skipped++; continue; } // already has one

            // Instantiate the test_0 prefab as a child named BoneBody at the rest transform.
            GameObject bone = (GameObject)PrefabUtility.InstantiatePrefab(prefab, player.scene);
            Undo.RegisterCreatedObjectUndo(bone, "Create BoneBody");
            bone.name = "BoneBody";
            bone.transform.SetParent(player.transform, false);
            bone.transform.localPosition = Vector3.zero;
            bone.transform.localRotation = Quaternion.identity;
            bone.transform.localScale = BodyScale;

            Animator anim = bone.GetComponent<Animator>();
            if (anim == null) anim = Undo.AddComponent<Animator>(bone);
            anim.runtimeAnimatorController = boneCtrl;
            SpriteRenderer sr = bone.GetComponent<SpriteRenderer>();

            // Wire the bone slots on PlayerAnimator (recorded for Undo, marks the component dirty).
            var so = new SerializedObject(pa);
            SetRef(so, "boneAnimator", anim);
            SetRef(so, "boneRenderer", sr);
            so.ApplyModifiedProperties();

            EditorUtility.SetDirty(bone);
            dirtyScenes.Add(player.scene);
            added++;
        }

        // Mark each touched scene dirty so YOU review + save it. We never auto-save the scene.
        foreach (Scene scene in dirtyScenes)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log($"[AnimatorBuilder] Setup BoneBody: added {added}, skipped {skipped} (already had one), " +
                  $"missing {missing}. CHECK the boneAnimator/boneRenderer slots, then SAVE the scene (Ctrl+S).");
    }

    // ======================================================================================
    //  Setup the bone-rigged HoldBody child (ball-holding body) on all six players + wire slots
    // ======================================================================================

    [MenuItem("Tools/Setup HoldBody All Players")]
    public static void SetupHoldBodyAllPlayers()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HoldPrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[AnimatorBuilder] HoldBody prefab not found at {HoldPrefabPath}. Drag the " +
                           "rigged 'hold_0' object from the CharacterRig scene into Assets/Sprites/Players/ " +
                           "to create hold_0.prefab, then run this again.");
            return;
        }
        AnimatorController holdCtrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(HoldControllerPath);
        if (holdCtrl == null)
        {
            Debug.LogError($"[AnimatorBuilder] HoldBodyAnimation controller not found at {HoldControllerPath}.");
            return;
        }

        // Match the six named players against the PlayerAnimators in the open scene (includes inactive).
        PlayerAnimator[] all = Object.FindObjectsByType<PlayerAnimator>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        int added = 0, skipped = 0, missing = 0;
        var dirtyScenes = new HashSet<Scene>();
        foreach (string name in PlayerNames)
        {
            PlayerAnimator pa = all.FirstOrDefault(p => p.gameObject.name == name);
            if (pa == null)
            {
                Debug.LogWarning($"[AnimatorBuilder] Player '{name}' (with PlayerAnimator) not found in " +
                                 "the open scene — skipped.");
                missing++;
                continue;
            }

            GameObject player = pa.gameObject;
            if (player.transform.Find("HoldBody") != null) { skipped++; continue; } // already has one

            // Instantiate the hold_0 prefab as a child named HoldBody at the rest transform.
            GameObject hold = (GameObject)PrefabUtility.InstantiatePrefab(prefab, player.scene);
            Undo.RegisterCreatedObjectUndo(hold, "Create HoldBody");
            hold.name = "HoldBody";
            hold.transform.SetParent(player.transform, false);
            hold.transform.localPosition = Vector3.zero;
            hold.transform.localRotation = Quaternion.identity;
            hold.transform.localScale = BodyScale;

            Animator anim = hold.GetComponent<Animator>();
            if (anim == null) anim = Undo.AddComponent<Animator>(hold);
            anim.runtimeAnimatorController = holdCtrl;
            SpriteRenderer sr = hold.GetComponent<SpriteRenderer>();

            // Wire the hold slots on PlayerAnimator (recorded for Undo, marks the component dirty).
            var so = new SerializedObject(pa);
            SetRef(so, "holdAnimator", anim);
            SetRef(so, "holdRenderer", sr);
            so.ApplyModifiedProperties();

            EditorUtility.SetDirty(hold);
            dirtyScenes.Add(player.scene);
            added++;
        }

        // Mark each touched scene dirty so YOU review + save it. We never auto-save the scene.
        foreach (Scene scene in dirtyScenes)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log($"[AnimatorBuilder] Setup HoldBody: added {added}, skipped {skipped} (already had one), " +
                  $"missing {missing}. CHECK the holdAnimator/holdRenderer slots, then SAVE the scene (Ctrl+S).");
    }

    // ======================================================================================
    //  Setup the bone-rigged BackBoneBody child (back-facing idle float body) on all six players
    // ======================================================================================

    [MenuItem("Tools/Setup BackBoneBody All Players")]
    public static void SetupBackBoneBodyAllPlayers()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BackBonePrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[AnimatorBuilder] BackBoneBody prefab not found at {BackBonePrefabPath}.");
            return;
        }
        AnimatorController backBoneCtrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(BackBoneControllerPath);
        if (backBoneCtrl == null)
        {
            Debug.LogError($"[AnimatorBuilder] BackBoneBodyAnimation controller not found at {BackBoneControllerPath}.");
            return;
        }

        // Match the six named players against the PlayerAnimators in the open scene (includes inactive).
        PlayerAnimator[] all = Object.FindObjectsByType<PlayerAnimator>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        int added = 0, skipped = 0, missing = 0;
        var dirtyScenes = new HashSet<Scene>();
        foreach (string name in PlayerNames)
        {
            PlayerAnimator pa = all.FirstOrDefault(p => p.gameObject.name == name);
            if (pa == null)
            {
                Debug.LogWarning($"[AnimatorBuilder] Player '{name}' (with PlayerAnimator) not found in " +
                                 "the open scene — skipped.");
                missing++;
                continue;
            }

            GameObject player = pa.gameObject;
            if (player.transform.Find("BackBoneBody") != null) { skipped++; continue; } // already has one

            // Instantiate the test-back_0 prefab as a child named BackBoneBody at the rest transform.
            GameObject bone = (GameObject)PrefabUtility.InstantiatePrefab(prefab, player.scene);
            Undo.RegisterCreatedObjectUndo(bone, "Create BackBoneBody");
            bone.name = "BackBoneBody";
            bone.transform.SetParent(player.transform, false);
            bone.transform.localPosition = Vector3.zero;
            bone.transform.localRotation = Quaternion.identity;
            bone.transform.localScale = BodyScale;

            Animator anim = bone.GetComponent<Animator>();
            if (anim == null) anim = Undo.AddComponent<Animator>(bone);
            anim.runtimeAnimatorController = backBoneCtrl;
            SpriteRenderer sr = bone.GetComponent<SpriteRenderer>();

            // Wire the back-bone slots on PlayerAnimator (recorded for Undo, marks the component dirty).
            var so = new SerializedObject(pa);
            SetRef(so, "backBoneAnimator", anim);
            SetRef(so, "backBoneRenderer", sr);
            so.ApplyModifiedProperties();

            EditorUtility.SetDirty(bone);
            dirtyScenes.Add(player.scene);
            added++;
        }

        // Mark each touched scene dirty so YOU review + save it. We never auto-save the scene.
        foreach (Scene scene in dirtyScenes)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log($"[AnimatorBuilder] Setup BackBoneBody: added {added}, skipped {skipped} (already had one), " +
                  $"missing {missing}. CHECK the backBoneAnimator/backBoneRenderer slots, then SAVE the scene (Ctrl+S).");
    }

    // ======================================================================================
    //  Setup the bone-rigged BackHoldBody child (back-facing ball-holding body) on all six players
    // ======================================================================================

    [MenuItem("Tools/Setup BackHoldBody All Players")]
    public static void SetupBackHoldBodyAllPlayers()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BackHoldPrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[AnimatorBuilder] BackHoldBody prefab not found at {BackHoldPrefabPath}.");
            return;
        }
        AnimatorController backHoldCtrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(BackHoldControllerPath);
        if (backHoldCtrl == null)
        {
            Debug.LogError($"[AnimatorBuilder] BackHoldBodyAnimation controller not found at {BackHoldControllerPath}.");
            return;
        }

        // Match the six named players against the PlayerAnimators in the open scene (includes inactive).
        PlayerAnimator[] all = Object.FindObjectsByType<PlayerAnimator>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        int added = 0, skipped = 0, missing = 0;
        var dirtyScenes = new HashSet<Scene>();
        foreach (string name in PlayerNames)
        {
            PlayerAnimator pa = all.FirstOrDefault(p => p.gameObject.name == name);
            if (pa == null)
            {
                Debug.LogWarning($"[AnimatorBuilder] Player '{name}' (with PlayerAnimator) not found in " +
                                 "the open scene — skipped.");
                missing++;
                continue;
            }

            GameObject player = pa.gameObject;
            if (player.transform.Find("BackHoldBody") != null) { skipped++; continue; } // already has one

            // Instantiate the back-side_0 prefab as a child named BackHoldBody at the rest transform.
            GameObject hold = (GameObject)PrefabUtility.InstantiatePrefab(prefab, player.scene);
            Undo.RegisterCreatedObjectUndo(hold, "Create BackHoldBody");
            hold.name = "BackHoldBody";
            hold.transform.SetParent(player.transform, false);
            hold.transform.localPosition = Vector3.zero;
            hold.transform.localRotation = Quaternion.identity;
            hold.transform.localScale = BodyScale;

            Animator anim = hold.GetComponent<Animator>();
            if (anim == null) anim = Undo.AddComponent<Animator>(hold);
            anim.runtimeAnimatorController = backHoldCtrl;
            SpriteRenderer sr = hold.GetComponent<SpriteRenderer>();

            // Wire the back-hold slots on PlayerAnimator (recorded for Undo, marks the component dirty).
            var so = new SerializedObject(pa);
            SetRef(so, "backHoldAnimator", anim);
            SetRef(so, "backHoldRenderer", sr);
            so.ApplyModifiedProperties();

            EditorUtility.SetDirty(hold);
            dirtyScenes.Add(player.scene);
            added++;
        }

        // Mark each touched scene dirty so YOU review + save it. We never auto-save the scene.
        foreach (Scene scene in dirtyScenes)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log($"[AnimatorBuilder] Setup BackHoldBody: added {added}, skipped {skipped} (already had one), " +
                  $"missing {missing}. CHECK the backHoldAnimator/backHoldRenderer slots, then SAVE the scene (Ctrl+S).");
    }

    // ======================================================================================
    //  shared helpers
    // ======================================================================================

    // Load the first existing clip among several name candidates.
    static AnimationClip LoadClip(string[] candidates)
    {
        foreach (string name in candidates)
        {
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{AnimDir}/{name}.anim");
            if (clip != null) return clip;
        }
        Debug.LogError($"[AnimatorBuilder] Clip not found: " +
                       $"{string.Join(" / ", candidates.Select(c => c + ".anim"))} in {AnimDir}");
        return null;
    }

    // Load a Parts sprite robustly — handles both Single and Multiple (sliced) texture import modes.
    static Sprite LoadSprite(string fileNoExt)
    {
        string path = $"{PartsDir}/{fileNoExt}.png";
        Sprite s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (s == null) s = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
        if (s == null)
            Debug.LogError($"[AnimatorBuilder] No Sprite at {path}. Set its Texture Type to " +
                           "'Sprite (2D and UI)' so it imports as a Sprite.");
        return s;
    }

    static void EnsureParameter(AnimatorController controller, string name,
                                AnimatorControllerParameterType type)
    {
        foreach (AnimatorControllerParameter p in controller.parameters)
            if (p.name == name) return;
        controller.AddParameter(name, type);
    }

    static AnimatorState EnsureState(AnimatorStateMachine sm, string name)
    {
        AnimatorState s = FindState(sm, name);
        return s != null ? s : sm.AddState(name);
    }

    static AnimatorState FindState(AnimatorStateMachine sm, string name)
    {
        foreach (ChildAnimatorState c in sm.states)
            if (c.state.name == name) return c.state;
        return null;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/'));
        string leaf = path.Substring(path.LastIndexOf('/') + 1);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
