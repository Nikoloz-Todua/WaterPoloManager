using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Editor tool: builds the water-polo player animation clips from sliced sprite
// sheets, assigns them to an animator controller's states, and wires the
// transitions. Two menu items share ONE pipeline:
//   Tools/Build Water Polo Animations → RedTeam sheets  → PlayerAnimation.controller
//   Tools/Build Blue Team Animations  → BlueTeam sheets → BlueAnimation.controller
// A missing controller is CREATED from scratch (parameters + states included), so
// BlueAnimation.controller is born as a real, separate asset on first run.
public static class AnimationClipBuilder
{
    // ---- red (default) team ----
    const string SpriteSheetDir = "Assets/Sprites/Players/RedTeam";
    const string AnimDir = "Assets/Sprites/Players/Animations";
    const string ControllerPath = "Assets/Sprites/PlayerAnimation.controller";

    // ---- blue team: identical sheets with a _blue filename suffix ----
    const string BlueTeamSpriteSheetDir = "Assets/Sprites/Players/BlueTeam";
    const string BlueAnimDir = "Assets/Sprites/Players/Animations/Blue";
    const string BlueControllerPath = "Assets/Sprites/BlueAnimation.controller";
    const string BlueSheetSuffix = "_blue";

    // One clip's recipe: state name, sprite-sheet file (no extension/suffix), FPS, looping.
    struct ClipSpec
    {
        public string state;
        public string sheet;
        public float fps;
        public bool loop;
        public ClipSpec(string state, string sheet, float fps, bool loop)
        { this.state = state; this.sheet = sheet; this.fps = fps; this.loop = loop; }
    }

    static readonly ClipSpec[] Specs =
    {
        new ClipSpec("idle",   "idle_floating_in_water__gentle_arm_movement", 8f,  true),
        new ClipSpec("swim",   "swimming_forward__arms_mid-stroke",           10f, true),
        new ClipSpec("sprint", "sprinting__arms_in_fast_crawl_stroke",        12f, true),
        new ClipSpec("hold",   "holding_ball_raised_in_right_hand",           8f,  true),
        new ClipSpec("throw",  "throwing_ball_overhead__arm_extended",        12f, false),
        new ClipSpec("defend", "defensive_stance__arms_out_wide",             8f,  true),
        new ClipSpec("steal",  "steal_snatch_attempt",                        14f, false),
    };

    // Every parameter the runtime animators drive — created on a fresh controller,
    // verified (never removed) on an existing one.
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

    [MenuItem("Tools/Build Water Polo Animations")]
    public static void BuildAll() => Build(SpriteSheetDir, AnimDir, ControllerPath, "");

    [MenuItem("Tools/Build Blue Team Animations")]
    public static void BuildAllBlue() => Build(BlueTeamSpriteSheetDir, BlueAnimDir, BlueControllerPath, BlueSheetSuffix);

    // ---- the shared pipeline ----

    static void Build(string sheetDir, string animDir, string controllerPath, string sheetSuffix)
    {
        EnsureFolder(sheetDir); // so the blue sheet folder exists to drop PNGs into
        EnsureFolder(animDir);

        // 1) Build + save every clip, keyed by state name for the wiring step.
        var clips = new Dictionary<string, AnimationClip>();
        foreach (ClipSpec spec in Specs)
        {
            AnimationClip clip = BuildClip(spec, sheetDir, animDir, sheetSuffix);
            if (clip != null) clips[spec.state] = clip;
        }

        // 2) Load — or CREATE — the controller, then make sure every parameter and
        //    state exists before motions/conditions reference them.
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            Debug.Log($"[AnimationClipBuilder] Created new controller at {controllerPath}");
        }

        foreach (var (name, type) in Parameters) EnsureParameter(controller, name, type);
        foreach (ClipSpec spec in Specs) EnsureState(controller.layers[0].stateMachine, spec.state);

        AssignMotions(controller, clips);
        WireTransitions(controller);
        EditorUtility.SetDirty(controller);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[AnimationClipBuilder] Done → {controllerPath}");
    }

    // ---- clip building ----

    static AnimationClip BuildClip(ClipSpec spec, string sheetDir, string animDir, string sheetSuffix)
    {
        string sheetPath = $"{sheetDir}/{spec.sheet}{sheetSuffix}.png";

        // sliced sub-sprites only, in slice order (name-sorted)
        Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(sheetPath)
            .OfType<Sprite>()
            .OrderBy(s => s.name)
            .ToArray();

        if (sprites.Length == 0)
        {
            Debug.LogError($"[AnimationClipBuilder] No sprites found at {sheetPath}");
            return null;
        }

        var clip = new AnimationClip { frameRate = spec.fps };

        // keyframe the SpriteRenderer's sprite, one frame per FPS step
        var keys = new ObjectReferenceKeyframe[sprites.Length];
        for (int i = 0; i < sprites.Length; i++)
            keys[i] = new ObjectReferenceKeyframe { time = i / spec.fps, value = sprites[i] };

        var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

        // loop both ways: legacy wrapMode + the clip's loopTime setting the Animator reads
        clip.wrapMode = spec.loop ? WrapMode.Loop : WrapMode.Once;
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = spec.loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        string assetPath = $"{animDir}/{spec.state}.anim";
        AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
        if (existing != null) { EditorUtility.CopySerialized(clip, existing); return existing; }

        AssetDatabase.CreateAsset(clip, assetPath);
        return clip;
    }

    // ---- controller wiring ----

    static void AssignMotions(AnimatorController controller, Dictionary<string, AnimationClip> clips)
    {
        foreach (ChildAnimatorState child in controller.layers[0].stateMachine.states)
        {
            if (clips.TryGetValue(child.state.name, out AnimationClip clip))
                child.state.motion = clip;
        }
    }

    static void WireTransitions(AnimatorController controller)
    {
        AnimatorStateMachine sm = controller.layers[0].stateMachine;

        AnimatorState idle = FindState(sm, "idle");
        AnimatorState swim = FindState(sm, "swim");
        AnimatorState sprint = FindState(sm, "sprint");
        AnimatorState hold = FindState(sm, "hold");
        AnimatorState shoot = FindState(sm, "throw");
        AnimatorState defend = FindState(sm, "defend");
        AnimatorState steal = FindState(sm, "steal");

        if (idle == null || swim == null || sprint == null || hold == null || shoot == null ||
            defend == null || steal == null)
        {
            Debug.LogError("[AnimationClipBuilder] One or more states (idle/swim/sprint/hold/throw/defend/steal) missing.");
            return;
        }

        sm.defaultState = idle; // a freshly created controller must start in idle

        ClearTransitions(sm, new[] { idle, swim, sprint, hold, shoot, defend, steal });

        // idle <-> swim on Speed
        AddStateTransition(idle, swim).AddCondition(AnimatorConditionMode.Greater, 0.3f, "Speed");
        AddStateTransition(swim, idle).AddCondition(AnimatorConditionMode.Less, 0.3f, "Speed");

        // swim <-> sprint on IsSprinting
        AddStateTransition(swim, sprint).AddCondition(AnimatorConditionMode.If, 0f, "IsSprinting");
        AddStateTransition(sprint, swim).AddCondition(AnimatorConditionMode.IfNot, 0f, "IsSprinting");

        // hold: enter from anywhere, leave to idle
        AddAnyTransition(sm, hold).AddCondition(AnimatorConditionMode.If, 0f, "IsHolding");
        AddStateTransition(hold, idle).AddCondition(AnimatorConditionMode.IfNot, 0f, "IsHolding");

        // throw: trigger in from anywhere, time out back to idle
        AddAnyTransition(sm, shoot).AddCondition(AnimatorConditionMode.If, 0f, "IsShooting");
        AnimatorStateTransition throwToIdle = AddStateTransition(shoot, idle);
        throwToIdle.hasExitTime = true;
        throwToIdle.exitTime = 1.0f;

        // defend: enter from anywhere, leave to swim
        AddAnyTransition(sm, defend).AddCondition(AnimatorConditionMode.If, 0f, "IsDefending");
        AddStateTransition(defend, swim).AddCondition(AnimatorConditionMode.IfNot, 0f, "IsDefending");

        // steal: trigger in from anywhere, time out back to idle (same pattern as throw)
        AddAnyTransition(sm, steal).AddCondition(AnimatorConditionMode.If, 0f, "IsStealing");
        AnimatorStateTransition stealToIdle = AddStateTransition(steal, idle);
        stealToIdle.hasExitTime = true;
        stealToIdle.exitTime = 1.0f;
    }

    // Add a parameter if the controller doesn't have it yet (idempotent, never removes).
    static void EnsureParameter(AnimatorController controller, string name,
                                AnimatorControllerParameterType type)
    {
        foreach (AnimatorControllerParameter p in controller.parameters)
            if (p.name == name) return;
        controller.AddParameter(name, type);
    }

    // Find a state by name, creating it if missing (idempotent).
    static AnimatorState EnsureState(AnimatorStateMachine sm, string name)
    {
        AnimatorState s = FindState(sm, name);
        return s != null ? s : sm.AddState(name);
    }

    // Remove any existing outgoing / any-state transitions for these states so re-running is idempotent.
    static void ClearTransitions(AnimatorStateMachine sm, AnimatorState[] states)
    {
        var set = new HashSet<AnimatorState>(states);
        foreach (AnimatorState s in states)
            foreach (AnimatorStateTransition t in s.transitions.ToArray())
                s.RemoveTransition(t);

        foreach (AnimatorStateTransition t in sm.anyStateTransitions.ToArray())
            if (t.destinationState != null && set.Contains(t.destinationState))
                sm.RemoveAnyStateTransition(t);
    }

    static AnimatorStateTransition AddStateTransition(AnimatorState from, AnimatorState to)
    {
        AnimatorStateTransition t = from.AddTransition(to);
        t.hasExitTime = false;
        t.exitTime = 0f;
        t.duration = 0f;
        return t;
    }

    static AnimatorStateTransition AddAnyTransition(AnimatorStateMachine sm, AnimatorState to)
    {
        AnimatorStateTransition t = sm.AddAnyStateTransition(to);
        t.hasExitTime = false;
        t.exitTime = 0f;
        t.duration = 0f;
        t.canTransitionToSelf = false;
        return t;
    }

    static AnimatorState FindState(AnimatorStateMachine sm, string name)
    {
        foreach (ChildAnimatorState c in sm.states)
            if (c.state.name == name) return c.state;
        return null;
    }

    // ---- util ----

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/'));
        string leaf = path.Substring(path.LastIndexOf('/') + 1);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
