using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace PackageSaveTool
{
    /// <summary>
    /// AnimatorController 内のアニメーション欠落（Missing）の自動検知および修復クラス
    /// </summary>
    public static class AnimatorControllerFixer
    {
        public static List<AnimatorFixReport> ScanMissingClips(AnimatorController controller)
        {
            var reports = new List<AnimatorFixReport>();
            if (controller == null)
                return reports;

            WalkController(controller, null, reports, fix: false);
            return reports;
        }

        public static List<AnimatorFixReport> FixMissingClips(AnimatorController controller)
        {
            var reports = new List<AnimatorFixReport>();
            if (controller == null)
                return reports;

            var clipCache = FindAllAnimationClipsInProject();
            bool modified = WalkController(controller, clipCache, reports, fix: true);
            if (modified)
                EditorUtility.SetDirty(controller);

            return reports;
        }

        private static bool WalkController(
            AnimatorController controller,
            Dictionary<string, List<(AnimationClip clip, string path)>> clipCache,
            List<AnimatorFixReport> reports,
            bool fix)
        {
            bool modified = false;
            string controllerPath = AssetDatabase.GetAssetPath(controller);

            foreach (var layer in controller.layers)
            {
                if (layer.syncedLayerIndex >= 0 || layer.stateMachine == null)
                    continue;

                modified |= WalkStateMachine(layer.stateMachine, controller.name, controllerPath, clipCache, reports, fix);
            }

            return modified;
        }

        private static bool WalkStateMachine(
            AnimatorStateMachine stateMachine,
            string controllerName,
            string controllerPath,
            Dictionary<string, List<(AnimationClip clip, string path)>> clipCache,
            List<AnimatorFixReport> reports,
            bool fix)
        {
            if (stateMachine == null)
                return false;

            bool modified = false;

            foreach (var childState in stateMachine.states)
            {
                var state = childState.state;
                if (state == null)
                    continue;

                if (state.motion is BlendTree blendTree)
                {
                    WalkBlendTree(blendTree, controllerName, controllerPath, reports);
                    continue;
                }

                if (!IsMotionMissing(state, out string missingClipName))
                    continue;

                if (fix)
                    modified |= TryAssignClip(state, missingClipName, controllerName, controllerPath, clipCache, reports);
                else
                    reports.Add(CreateReport(controllerName, controllerPath, state.name, false));
            }

            foreach (var subMachine in stateMachine.stateMachines)
            {
                modified |= WalkStateMachine(subMachine.stateMachine, controllerName, controllerPath, clipCache, reports, fix);
            }

            return modified;
        }

        private static void WalkBlendTree(
            BlendTree blendTree,
            string controllerName,
            string controllerPath,
            List<AnimatorFixReport> reports)
        {
            if (blendTree == null)
                return;

            SerializedObject so = new SerializedObject(blendTree);
            SerializedProperty childrenProp = so.FindProperty("m_Childs") ?? so.FindProperty("m_Children");
            if (childrenProp == null || !childrenProp.isArray)
                return;

            for (int i = 0; i < childrenProp.arraySize; i++)
            {
                SerializedProperty childProp = childrenProp.GetArrayElementAtIndex(i);
                SerializedProperty motionProp = childProp.FindPropertyRelative("m_Motion");
                if (motionProp == null)
                    continue;

                var motion = motionProp.objectReferenceValue;
                if (motion is BlendTree subTree)
                {
                    WalkBlendTree(subTree, controllerName, controllerPath, reports);
                    continue;
                }

                if (!IsSerializedReferenceMissing(motionProp))
                    continue;

                reports.Add(new AnimatorFixReport
                {
                    ControllerName = controllerName,
                    ControllerPath = controllerPath,
                    StateName = $"BlendTree ({blendTree.name}) [{i}]",
                    ReassignedClipName = "",
                    ClipPath = "",
                    IsFixed = false,
                    Note = "BlendTree の子モーションが Missing です。空スロットは無視し、名前推定による自動修復は行いません。"
                });
            }
        }

        private static bool TryAssignClip(
            AnimatorState state,
            string missingClipName,
            string controllerName,
            string controllerPath,
            Dictionary<string, List<(AnimationClip clip, string path)>> clipCache,
            List<AnimatorFixReport> reports)
        {
            if (string.IsNullOrEmpty(missingClipName) ||
                clipCache == null ||
                !clipCache.TryGetValue(missingClipName, out var matches) ||
                matches.Count == 0)
            {
                reports.Add(CreateReport(controllerName, controllerPath, state.name, false, note: "同名の AnimationClip が見つかりませんでした。"));
                return false;
            }

            if (matches.Count > 1)
            {
                reports.Add(CreateReport(
                    controllerName,
                    controllerPath,
                    state.name,
                    false,
                    note: $"同名クリップが {matches.Count} 件あり、自動修復できませんでした。"));
                return false;
            }

            var found = matches[0];
            state.motion = found.clip;
            reports.Add(new AnimatorFixReport
            {
                ControllerName = controllerName,
                ControllerPath = controllerPath,
                StateName = state.name,
                ReassignedClipName = found.clip.name,
                ClipPath = found.path,
                IsFixed = true
            });
            return true;
        }

        private static AnimatorFixReport CreateReport(
            string controllerName,
            string controllerPath,
            string stateName,
            bool isFixed,
            string clipName = "",
            string clipPath = "",
            string note = "")
        {
            return new AnimatorFixReport
            {
                ControllerName = controllerName,
                ControllerPath = controllerPath,
                StateName = stateName,
                ReassignedClipName = clipName,
                ClipPath = clipPath,
                IsFixed = isFixed,
                Note = note
            };
        }

        private static bool IsMotionMissing(AnimatorState state, out string originalName)
        {
            originalName = string.Empty;
            if (state.motion != null)
                return false;

            SerializedObject so = new SerializedObject(state);
            SerializedProperty motionProp = so.FindProperty("m_Motion");
            if (!IsSerializedReferenceMissing(motionProp))
                return false;

            originalName = state.name;
            return true;
        }

        private static bool IsSerializedReferenceMissing(SerializedProperty motionProp)
        {
            return motionProp != null &&
                   motionProp.objectReferenceInstanceIDValue != 0 &&
                   motionProp.objectReferenceValue == null;
        }

        private static Dictionary<string, List<(AnimationClip clip, string path)>> FindAllAnimationClipsInProject()
        {
            var dict = new Dictionary<string, List<(AnimationClip, string)>>(StringComparer.OrdinalIgnoreCase);
            string[] guids = AssetDatabase.FindAssets("t:AnimationClip");

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null)
                    continue;

                if (!dict.TryGetValue(clip.name, out var list))
                {
                    list = new List<(AnimationClip, string)>();
                    dict.Add(clip.name, list);
                }

                list.Add((clip, path));
            }

            return dict;
        }
    }
}
