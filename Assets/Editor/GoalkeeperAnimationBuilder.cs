using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Editor tool: builds the goalkeeper animation clips from the single sliced
// goalkeeper sprite sheet, assigns them to the existing states of
// GoalkeeperAnimation.controller, and wires the DiveState transitions that
// GoalkeeperAnimator.cs drives. Unlike AnimationClipBuilder, every clip is
// a single held frame (one sprite, sample rate 1, looping), and the
// controller + states must already exist.
//   Tools/Build Goalkeeper Animations
public static class GoalkeeperAnimationBuilder
{
    const string SheetPath = "Assets/Sprites/Players/goalkeeper_sheet.png";
    const string AnimDir = "Assets/Sprites/Players/Animations";
    const string ControllerPath = "Assets/Sprites/Players/GoalkeeperAnimation.controller";

    // One clip's recipe: clip asset name, controller state name, sheet frame index.
    // The frame index doubles as the runtime DiveState value (0 idle .. 7 save) —
    // GoalkeeperAnimator.cs sets that integer to pick the state.
    struct ClipSpec
    {
        public string clip;
        public string state;
        public int frame;
        public ClipSpec(string clip, string state, int frame)
        { this.clip = clip; this.state = state; this.frame = frame; }
    }

    static readonly ClipSpec[] Specs =
    {
        new ClipSpec("goalkeeper_idle",              "idle",              0),
        new ClipSpec("goalkeeper_dive_left",         "dive_left",         1),
        new ClipSpec("goalkeeper_dive_right",        "dive_right",        2),
        new ClipSpec("goalkeeper_dive_bottom_left",  "dive_bottom_left",  3),
        new ClipSpec("goalkeeper_dive_bottom_right", "dive_bottom_right", 4),
        new ClipSpec("goalkeeper_dive_top_left",     "dive_top_left",     5),
        new ClipSpec("goalkeeper_dive_top_right",    "dive_top_right",    6),
        new ClipSpec("goalkeeper_save",              "save",              7),
    };

    [MenuItem("Tools/Build Goalkeeper Animations")]
    public static void BuildAll()
    {
        EnsureFolder(AnimDir);

        // sliced sub-sprites, looked up by exact name (goalkeeper_sheet_0 .. _7)
        Sprite[] allSprites = AssetDatabase.LoadAllAssetsAtPath(SheetPath)
            .OfType<Sprite>()
            .ToArray();

        if (allSprites.Length == 0)
        {
            Debug.LogError($"[GoalkeeperAnimationBuilder] No sprites found at {SheetPath} — is the sheet sliced?");
            return;
        }

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError($"[GoalkeeperAnimationBuilder] Controller not found at {ControllerPath}");
            return;
        }

        // 1) Build + save every clip, keyed by state name for the assignment step.
        var clips = new Dictionary<string, AnimationClip>();
        foreach (ClipSpec spec in Specs)
        {
            Sprite sprite = allSprites.FirstOrDefault(s => s.name == $"goalkeeper_sheet_{spec.frame}");
            if (sprite == null)
            {
                Debug.LogError($"[GoalkeeperAnimationBuilder] Sprite goalkeeper_sheet_{spec.frame} not found in {SheetPath}");
                continue;
            }
            clips[spec.state] = BuildClip(spec, sprite);
        }

        // 2) Assign each clip to its already-existing state.
        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        foreach (ClipSpec spec in Specs)
        {
            if (!clips.TryGetValue(spec.state, out AnimationClip clip)) continue;
            AnimatorState state = FindState(sm, spec.state);
            if (state == null)
            {
                Debug.LogError($"[GoalkeeperAnimationBuilder] State '{spec.state}' missing in {ControllerPath}");
                continue;
            }
            state.motion = clip;
        }

        // 3) Parameter + Any State transitions for GoalkeeperAnimator's DiveState integer.
        WireTransitions(controller, sm);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[GoalkeeperAnimationBuilder] Done → {ControllerPath}");
    }

    // One sprite held forever: a single keyframe at t=0, sample rate 1, looping.
    static AnimationClip BuildClip(ClipSpec spec, Sprite sprite)
    {
        var clip = new AnimationClip { frameRate = 1f };

        var keys = new[]
        {
            new ObjectReferenceKeyframe { time = 0f, value = sprite }
        };

        var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

        // loop both ways: legacy wrapMode + the clip's loopTime setting the Animator reads
        clip.wrapMode = WrapMode.Loop;
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        string assetPath = $"{AnimDir}/{spec.clip}.anim";
        AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
        if (existing != null) { EditorUtility.CopySerialized(clip, existing); return existing; }

        AssetDatabase.CreateAsset(clip, assetPath);
        return clip;
    }

    // One uniform rule per state: Any State → state when DiveState equals its value
    // (idle = 0, dives = 1..6, save = 7), duration 0, no exit time. Re-running clears
    // the old any-state transitions to these states first, so it's idempotent.
    static void WireTransitions(AnimatorController controller, AnimatorStateMachine sm)
    {
        EnsureParameter(controller, "DiveState", AnimatorControllerParameterType.Int);

        AnimatorState idle = FindState(sm, "idle");
        if (idle != null) sm.defaultState = idle;

        var stateNames = new HashSet<string>(Specs.Select(s => s.state));
        foreach (AnimatorStateTransition t in sm.anyStateTransitions.ToArray())
            if (t.destinationState != null && stateNames.Contains(t.destinationState.name))
                sm.RemoveAnyStateTransition(t);

        foreach (ClipSpec spec in Specs)
        {
            AnimatorState state = FindState(sm, spec.state);
            if (state == null) continue; // already logged by the assignment step

            AnimatorStateTransition t = sm.AddAnyStateTransition(state);
            t.hasExitTime = false;
            t.exitTime = 0f;
            t.duration = 0f;
            t.canTransitionToSelf = false;
            t.AddCondition(AnimatorConditionMode.Equals, spec.frame, "DiveState");
        }
    }

    // Add a parameter if the controller doesn't have it yet (idempotent, never removes).
    static void EnsureParameter(AnimatorController controller, string name,
                                AnimatorControllerParameterType type)
    {
        foreach (AnimatorControllerParameter p in controller.parameters)
            if (p.name == name) return;
        controller.AddParameter(name, type);
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
