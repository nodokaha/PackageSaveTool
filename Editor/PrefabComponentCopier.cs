using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PackageSaveTool
{
    internal static class PrefabComponentCopier
    {
        private static readonly HashSet<string> SkippedObjectReferenceProperties = new HashSet<string>
        {
            "m_Materials",
            "bones",
            "m_SharedMaterials",
            "m_SharedMesh",
            "m_Mesh"
        };

        public static bool TryCopy(GameObject sourcePrefab, GameObject targetFbx, string saveAssetPath, out string error)
        {
            error = null;
            if (sourcePrefab == null || targetFbx == null)
            {
                error = "Failed to load the selected prefab or FBX asset.";
                return false;
            }

            if (string.IsNullOrEmpty(saveAssetPath))
            {
                error = "Save path must be inside the Assets folder.";
                return false;
            }

            GameObject instance = PrefabUtility.InstantiatePrefab(targetFbx) as GameObject;
            if (instance == null)
                instance = Object.Instantiate(targetFbx);

            try
            {
                CopyComponents(sourcePrefab, instance, sourcePrefab, instance);
                PrefabUtility.SaveAsPrefabAsset(instance, saveAssetPath);
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
            finally
            {
                if (instance != null)
                    Object.DestroyImmediate(instance);
            }

            AssetDatabase.Refresh();
            return true;
        }

        private static void CopyComponents(GameObject source, GameObject target, GameObject sourceRoot, GameObject targetRoot)
        {
            Component[] sourceComponents = source.GetComponents<Component>();
            foreach (Component sourceComponent in sourceComponents)
            {
                if (sourceComponent == null || sourceComponent is Transform)
                    continue;

                Type componentType = sourceComponent.GetType();
                Component existingComponent = target.GetComponent(componentType);
                if (existingComponent != null)
                {
                    CopyComponentProperties(sourceComponent, existingComponent, sourceRoot, targetRoot, preserveTargetRenderData: true);
                    continue;
                }

                Component newComponent;
                try
                {
                    newComponent = target.AddComponent(componentType);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[PackageSaveTool] Could not add {componentType.Name}: {e.Message}");
                    continue;
                }

                if (newComponent == null)
                {
                    Debug.LogWarning($"[PackageSaveTool] AddComponent returned null for {componentType.Name} on {target.name}.");
                    continue;
                }

                CopyComponentProperties(sourceComponent, newComponent, sourceRoot, targetRoot, preserveTargetRenderData: false);
            }

            for (int i = 0; i < source.transform.childCount; i++)
            {
                Transform sourceChild = source.transform.GetChild(i);
                Transform targetChild = target.transform.Find(sourceChild.name);
                if (targetChild == null)
                {
                    var newChild = new GameObject(sourceChild.name);
                    newChild.transform.SetParent(target.transform, false);
                    newChild.transform.localPosition = sourceChild.localPosition;
                    newChild.transform.localRotation = sourceChild.localRotation;
                    newChild.transform.localScale = sourceChild.localScale;
                    targetChild = newChild.transform;
                }

                CopyComponents(sourceChild.gameObject, targetChild.gameObject, sourceRoot, targetRoot);
            }
        }

        private static void CopyComponentProperties(
            Component sourceComponent,
            Component targetComponent,
            GameObject sourceRoot,
            GameObject targetRoot,
            bool preserveTargetRenderData)
        {
            if (sourceComponent == null || targetComponent == null)
                return;

            SerializedObject preservedTarget = preserveTargetRenderData ? new SerializedObject(targetComponent) : null;

            EditorUtility.CopySerialized(sourceComponent, targetComponent);

            SerializedObject targetSO = new SerializedObject(targetComponent);
            if (preservedTarget != null)
                RestoreSkippedProperties(preservedTarget, targetSO);

            RemapHierarchyReferences(sourceComponent, targetSO, sourceRoot, targetRoot);
            targetSO.ApplyModifiedProperties();
        }

        private static void RestoreSkippedProperties(SerializedObject originalTarget, SerializedObject copiedTarget)
        {
            SerializedProperty iterator = originalTarget.GetIterator();
            bool enterChildren = true;
            while (iterator.Next(enterChildren))
            {
                if (!SkippedObjectReferenceProperties.Contains(iterator.name))
                {
                    enterChildren = true;
                    continue;
                }

                SerializedProperty dest = copiedTarget.FindProperty(iterator.propertyPath);
                if (dest != null)
                    copiedTarget.CopyFromSerializedProperty(iterator);

                enterChildren = false;
            }
        }

        private static void RemapHierarchyReferences(
            Component sourceComponent,
            SerializedObject targetSO,
            GameObject sourceRoot,
            GameObject targetRoot)
        {
            SerializedObject sourceSO = new SerializedObject(sourceComponent);
            SerializedProperty iterator = sourceSO.GetIterator();
            bool enterChildren = true;
            while (iterator.Next(enterChildren))
            {
                enterChildren = true;
                if (iterator.propertyType != SerializedPropertyType.ObjectReference)
                    continue;
                if (SkippedObjectReferenceProperties.Contains(iterator.name))
                {
                    enterChildren = false;
                    continue;
                }

                Object sourceRef = iterator.objectReferenceValue;
                if (!IsHierarchyObject(sourceRef))
                    continue;

                SerializedProperty dest = targetSO.FindProperty(iterator.propertyPath);
                if (dest == null)
                    continue;

                dest.objectReferenceValue = ResolveObjectReference(sourceRef, sourceRoot, targetRoot);
            }
        }

        private static bool IsHierarchyObject(Object obj)
        {
            return obj is Transform || obj is GameObject || obj is Component;
        }

        private static Object ResolveObjectReference(Object sourceReference, GameObject sourceRoot, GameObject targetRoot)
        {
            if (sourceReference == null || sourceRoot == null || targetRoot == null)
                return sourceReference;

            if (sourceReference is Transform sourceTransform)
                return FindCorrespondingTransform(sourceTransform, sourceRoot, targetRoot);

            if (sourceReference is GameObject sourceGameObject)
            {
                var targetTransform = FindCorrespondingTransform(sourceGameObject.transform, sourceRoot, targetRoot);
                return targetTransform != null ? targetTransform.gameObject : null;
            }

            if (sourceReference is Component sourceComponent)
            {
                var targetTransform = FindCorrespondingTransform(sourceComponent.transform, sourceRoot, targetRoot);
                if (targetTransform == null)
                    return null;
                return targetTransform.GetComponent(sourceComponent.GetType());
            }

            return sourceReference;
        }

        private static Transform FindCorrespondingTransform(Transform sourceTransform, GameObject sourceRoot, GameObject targetRoot)
        {
            string relativePath = GetRelativeTransformPath(sourceTransform, sourceRoot.transform);
            if (relativePath == null)
                return null;
            if (relativePath.Length == 0)
                return targetRoot.transform;

            return targetRoot.transform.Find(relativePath);
        }

        private static string GetRelativeTransformPath(Transform transform, Transform root)
        {
            if (transform == root)
                return string.Empty;

            var segments = new List<string>();
            Transform current = transform;
            while (current != null && current != root)
            {
                segments.Insert(0, current.name);
                current = current.parent;
            }

            return current == root ? string.Join("/", segments) : null;
        }
    }
}
