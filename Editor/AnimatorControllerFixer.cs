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
            if (controller == null) return reports;

            string controllerPath = AssetDatabase.GetAssetPath(controller);

            foreach (var layer in controller.layers)
            {
                ScanStateMachine(layer.stateMachine, controller.name, controllerPath, reports);
            }

            return reports;
        }

        private static void ScanStateMachine(AnimatorStateMachine stateMachine, string controllerName, string controllerPath, List<AnimatorFixReport> reports)
        {
            foreach (var childState in stateMachine.states)
            {
                var state = childState.state;
                if (state == null) continue;

                if (state.motion is BlendTree blendTree)
                {
                    ScanBlendTree(blendTree, controllerName, controllerPath, reports);
                }
                else if (IsMotionMissing(state, out _))
                {
                    reports.Add(new AnimatorFixReport
                    {
                        ControllerName = controllerName,
                        ControllerPath = controllerPath,
                        StateName = state.name,
                        ReassignedClipName = "",
                        ClipPath = "",
                        IsFixed = false
                    });
                }
            }

            foreach (var subMachine in stateMachine.stateMachines)
            {
                ScanStateMachine(subMachine.stateMachine, controllerName, controllerPath, reports);
            }
        }

        private static void ScanBlendTree(BlendTree blendTree, string controllerName, string controllerPath, List<AnimatorFixReport> reports)
        {
            foreach (var child in blendTree.children)
            {
                if (child.motion is BlendTree subTree)
                {
                    ScanBlendTree(subTree, controllerName, controllerPath, reports);
                }
                else if (child.motion == null)
                {
                    reports.Add(new AnimatorFixReport
                    {
                        ControllerName = controllerName,
                        ControllerPath = controllerPath,
                        StateName = $"BlendTree ({blendTree.name})",
                        ReassignedClipName = "",
                        ClipPath = "",
                        IsFixed = false
                    });
                }
            }
        }

        public static List<AnimatorFixReport> DetectAndFixMissingClips(AnimatorController controller)
        {
            var reports = new List<AnimatorFixReport>();
            if (controller == null) return reports;

            string controllerPath = AssetDatabase.GetAssetPath(controller);
            var allClips = FindAllAnimationClipsInProject();

            foreach (var layer in controller.layers)
            {
                FixStateMachine(layer.stateMachine, controller.name, controllerPath, allClips, reports);
            }

            if (reports.Count > 0)
            {
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
                Debug.Log($"[AnimatorFixer] '{controller.name}' 内の欠落したアニメーション参照を {reports.Count} 件自動修復しました。");
            }

            return reports;
        }

        private static void FixStateMachine(
            AnimatorStateMachine stateMachine,
            string controllerName,
            string controllerPath,
            Dictionary<string, (AnimationClip clip, string path)> clipCache,
            List<AnimatorFixReport> reports)
        {
            foreach (var childState in stateMachine.states)
            {
                var state = childState.state;
                if (state == null) continue;

                if (state.motion is BlendTree blendTree)
                {
                    FixBlendTree(blendTree, controllerName, controllerPath, clipCache, reports);
                }
                else if (IsMotionMissing(state, out string missingClipName))
                {
                    if (!string.IsNullOrEmpty(missingClipName) && clipCache.TryGetValue(missingClipName, out var found))
                    {
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
                    }
                }
            }

            foreach (var subMachine in stateMachine.stateMachines)
            {
                FixStateMachine(subMachine.stateMachine, controllerName, controllerPath, clipCache, reports);
            }
        }

        private static void FixBlendTree(
            BlendTree blendTree,
            string controllerName,
            string controllerPath,
            Dictionary<string, (AnimationClip clip, string path)> clipCache,
            List<AnimatorFixReport> reports)
        {
            var children = blendTree.children;
            bool isModified = false;

            for (int i = 0; i < children.Length; i++)
            {
                var child = children[i];
                if (child.motion is BlendTree subTree)
                {
                    FixBlendTree(subTree, controllerName, controllerPath, clipCache, reports);
                }
                else if (child.motion == null)
                {
                    string searchKey = blendTree.name;
                    if (clipCache.TryGetValue(searchKey, out var found))
                    {
                        child.motion = found.clip;
                        children[i] = child;
                        isModified = true;

                        reports.Add(new AnimatorFixReport
                        {
                            ControllerName = controllerName,
                            ControllerPath = controllerPath,
                            StateName = $"BlendTree ({blendTree.name})",
                            ReassignedClipName = found.clip.name,
                            ClipPath = found.path,
                            IsFixed = true
                        });
                    }
                }
            }

            if (isModified)
            {
                blendTree.children = children;
            }
        }

        private static bool IsMotionMissing(AnimatorState state, out string originalName)
        {
            originalName = string.Empty;

            if (state.motion == null)
            {
                SerializedObject so = new SerializedObject(state);
                SerializedProperty motionProp = so.FindProperty("m_Motion");

                if (motionProp != null && motionProp.objectReferenceInstanceIDValue != 0 && motionProp.objectReferenceValue == null)
                {
                    originalName = state.name;
                    return true;
                }
            }

            return false;
        }

        private static Dictionary<string, (AnimationClip clip, string path)> FindAllAnimationClipsInProject()
        {
            var dict = new Dictionary<string, (AnimationClip, string)>(StringComparer.OrdinalIgnoreCase);
            string[] guids = AssetDatabase.FindAssets("t:AnimationClip");

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip != null && !dict.ContainsKey(clip.name))
                {
                    dict.Add(clip.name, (clip, path));
                }
            }

            return dict;
        }
    }
}