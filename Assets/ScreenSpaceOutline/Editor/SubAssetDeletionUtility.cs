// Assets/Editor/ContextDeleteSubAsset.cs
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class ContextDeleteSubAsset
{
    private const string MenuPath = "Assets/Delete Sub-Assets";

    // 우클릭 메뉴 본체
    [MenuItem(MenuPath, priority = 19)]
    private static void DeleteSelectedSubAssets()
    {
        var targets = Selection.objects;
        if (targets == null || targets.Length == 0) return;

        var deletable = new List<Object>();
        var skipped = new List<Object>();

        foreach (var obj in targets)
        {
            if (IsDeletableSubAsset(obj)) deletable.Add(obj);
            else skipped.Add(obj);
        }

        if (deletable.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Delete Sub-Assets",
                "삭제 가능한 자식 에셋이 선택되지 않았습니다.\n" +
                "FBX/스프라이트 시트처럼 임포터가 생성한 서브 에셋은 여기서 삭제할 수 없습니다.\n" +
                "→ 임포트 설정에서 비활성화/제거 후 Reimport 해주세요.",
                "OK");
            return;
        }

        // 경고/확인
        var summary = string.Join("\n", deletable.Take(10).Select(o => $"• {o.name} ({o.GetType().Name})"));
        if (deletable.Count > 10) summary += $"\n... (+{deletable.Count - 10} more)";

        if (!EditorUtility.DisplayDialog(
                "Delete Sub-Assets",
                $"아래 서브 에셋 {deletable.Count}개를 삭제합니다:\n\n{summary}\n\n되돌리기 어려울 수 있습니다. 진행할까요?",
                "Delete", "Cancel"))
            return;

        Undo.IncrementCurrentGroup();
        var group = Undo.GetCurrentGroup();

        // 경로별로 저장/리임포트 묶음 처리
        var pathsToRefresh = new HashSet<string>();

        foreach (var sub in deletable)
        {
            var path = AssetDatabase.GetAssetPath(sub);
            if (string.IsNullOrEmpty(path)) continue;

            // 숨김 플래그 제거(보존 중인 HideFlags로 인해 제거 실패하는 경우 방지)
            if (sub.hideFlags != HideFlags.None)
            {
                Undo.RecordObject(sub, "Clear hideFlags");
                sub.hideFlags = HideFlags.None;
                EditorUtility.SetDirty(sub);
            }

            Undo.RegisterCompleteObjectUndo(sub, "Delete Sub-Asset");
            AssetDatabase.RemoveObjectFromAsset(sub);
            Object.DestroyImmediate(sub, true);

            pathsToRefresh.Add(path);
        }

        foreach (var p in pathsToRefresh)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(p);
        }

        Undo.CollapseUndoOperations(group);

        if (skipped.Count > 0)
        {
            var skippedMsg = string.Join("\n",
                skipped.Take(8).Select(o => $"• {o.name} ({o.GetType().Name})"));
            if (skipped.Count > 8) skippedMsg += $"\n... (+{skipped.Count - 8} more)";

            EditorUtility.DisplayDialog(
                "일부 항목은 건너뜀",
                "임포터가 생성한 서브 에셋은 삭제 대상에서 제외했습니다.\n" +
                "해당 항목은 임포트 설정에서 제거 후 Reimport 하세요.\n\n" + skippedMsg,
                "OK");
        }
    }

    // 메뉴 노출/활성화 규칙(선택 내에 '삭제 가능한' 서브 에셋이 하나라도 있으면 활성화)
    [MenuItem(MenuPath, validate = true)]
    private static bool ValidateDeleteSelectedSubAssets()
    {
        var targets = Selection.objects;
        if (targets == null || targets.Length == 0) return false;

        // 프로젝트의 에셋만 대상으로(씬 오브젝트 제외)
        return targets.Any(IsDeletableSubAsset);
    }

    // ----- 내부 로직 -----

    private static bool IsDeletableSubAsset(Object obj)
    {
        if (obj == null) return false;

        // 프로젝트 자산이 아니면 제외
        var path = AssetDatabase.GetAssetPath(obj);
        if (string.IsNullOrEmpty(path)) return false;

        // 메인 에셋은 제외(메뉴는 'Sub-Asset' 전용)
        if (!AssetDatabase.IsSubAsset(obj)) return false;

        // 임포터가 생성한 서브 에셋은 여기서 삭제하지 않음(임포트 설정에서 제거)
        if (IsImporterGeneratedSubAsset(path, obj)) return false;

        // 그 외(보통 .asset 안에 AddObjectToAsset로 붙인 ScriptableObject 등)만 허용
        return true;
    }

    private static bool IsImporterGeneratedSubAsset(string path, Object sub)
    {
        var importer = AssetImporter.GetAtPath(path);
        if (importer == null) return false; // .asset 등은 importer 없음 → 직접 생성물일 가능성 높음

        // FBX/모델: ModelImporter가 관리하는 AnimationClip/Mesh/Avatar/Material 등
        if (importer is ModelImporter) return true;

        // 텍스처/PSD: TextureImporter 또는 PSDImporter가 만드는 Sprite 등
        var importerType = importer.GetType().Name; // 패키지 의존 없이 이름으로 판별
        if (importerType == "TextureImporter" || importerType.Contains("PSD"))
        {
            if (sub is Sprite) return true;
        }

        // 그 외에도 임포터가 만들 수 있지만, 보수적으로 '알려진 케이스'만 막습니다.
        return false;
    }
}
