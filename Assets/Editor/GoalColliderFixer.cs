using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// One-shot editor helper (Tools ▸ Fix Goal Colliders) that resizes the GoalRight / GoalLeft
// trigger boxes so they match the VISUAL goal mouth instead of the tiny 1×1 default.
//
// Both goals sit at scale 0.2, so a Box Collider 2D Size of (4, 15) gives a world trigger of
// 0.8 × 3.0 units — a realistic water-polo goal mouth. Run it once; it edits the open scene and
// marks it dirty so you just need to save (Ctrl+S). It does NOT enter play mode or touch
// anything else.
public static class GoalColliderFixer
{
    // Goal object name -> the Box Collider 2D size to apply (local size; world = size * scale).
    static readonly Vector2 GoalColliderSize = new Vector2(4f, 15f);
    static readonly string[] GoalNames = { "GoalRight", "GoalLeft" };

    [MenuItem("Tools/Fix Goal Colliders")]
    public static void FixGoalColliders()
    {
        int fixedCount = 0;

        foreach (string goalName in GoalNames)
        {
            // Find the goal in the currently open scene (inactive objects included).
            GameObject go = FindInScene(goalName);
            if (go == null)
            {
                Debug.LogWarning($"[GoalColliderFixer] Could not find a GameObject named '{goalName}' in the open scene — skipped.");
                continue;
            }

            BoxCollider2D box = go.GetComponent<BoxCollider2D>();
            if (box == null)
            {
                Debug.LogWarning($"[GoalColliderFixer] '{goalName}' has no Box Collider 2D — skipped.");
                continue;
            }

            Undo.RecordObject(box, "Fix Goal Collider");
            box.size = GoalColliderSize;
            EditorUtility.SetDirty(box);
            fixedCount++;

            Vector3 ls = go.transform.lossyScale;
            Debug.Log($"[GoalColliderFixer] '{goalName}' Box Collider 2D Size set to {GoalColliderSize} " +
                      $"(world ≈ {GoalColliderSize.x * ls.x:0.##} × {GoalColliderSize.y * ls.y:0.##} units).");
        }

        if (fixedCount > 0)
        {
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[GoalColliderFixer] Done — {fixedCount} goal collider(s) updated. Save the scene (Ctrl+S) to keep the change.");
        }
        else
        {
            Debug.LogWarning("[GoalColliderFixer] Nothing was changed. Make sure the match scene (with GoalRight/GoalLeft) is the OPEN scene before running this.");
        }
    }

    // Resolve a top-level/nested object by name in the active scene, including inactive ones.
    static GameObject FindInScene(string name)
    {
        foreach (GameObject root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name == name) return root;
            Transform t = FindChild(root.transform, name);
            if (t != null) return t.gameObject;
        }
        return null;
    }

    static Transform FindChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            Transform found = FindChild(child, name);
            if (found != null) return found;
        }
        return null;
    }
}
