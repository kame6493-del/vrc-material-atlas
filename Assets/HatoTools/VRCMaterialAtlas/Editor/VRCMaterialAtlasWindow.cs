using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HatoTools.VRCMaterialAtlas
{
    /// <summary>
    /// VRC Material Atlas メインエディタウィンドウ
    /// </summary>
    public class VRCMaterialAtlasWindow : EditorWindow
    {
        // === 定数 ===
        private const string TOOL_NAME = "VRC Material Atlas";
        private const string VERSION = "1.0.0";
        private const string OUTPUT_FOLDER = "Assets/HatoTools/VRCMaterialAtlas/Generated";

        // === UI State ===
        private SkinnedMeshRenderer _targetRenderer;
        private Vector2 _scrollPos;
        private bool _showAdvancedSettings = false;
        private bool _showMaterialList = true;

        // === Settings ===
        private int _maxAtlasSize = 4096;
        private int _padding = 4;
        private bool _includeNormalMap = true;
        private bool _includeEmissionMap = true;
        private bool _includeOcclusionMap = false;
        private bool _preserveTexelDensity = true;

        // === Atlas Size Options ===
        private static readonly string[] _atlasSizeOptions = { "1024", "2048", "4096", "8192" };
        private static readonly int[] _atlasSizeValues = { 1024, 2048, 4096, 8192 };
        private int _atlasSizeIndex = 2; // default 4096

        // === Result ===
        private AtlasGenerator.AtlasResult _lastResult;
        private string _statusMessage = "";
        private bool _isProcessing = false;

        [MenuItem("Tools/HatoTools/VRC Material Atlas", false, 100)]
        public static void ShowWindow()
        {
            var window = GetWindow<VRCMaterialAtlasWindow>(TOOL_NAME);
            window.position = new Rect(100, 100, 450, 650);
        }

        public override void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            DrawHeader();
            EditorGUILayout.Space();
            DrawTargetSection();
            EditorGUILayout.Space();
            DrawMaterialListSection();
            EditorGUILayout.Space();
            DrawSettingsSection();
            EditorGUILayout.Space();
            DrawExecuteSection();
            EditorGUILayout.Space();
            DrawStatusSection();

            EditorGUILayout.EndScrollView();
        }

        /// <summary>ヘッダー描画</summary>
        private void DrawHeader()
        {
            EditorGUILayout.LabelField($"🎨 {TOOL_NAME} v{VERSION}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("複数マテリアルを1つのアトラスに統合してMaterial Slotsを削減します", EditorStyles.wordWrappedMiniLabel);
        }

        /// <summary>ターゲット選択セクション</summary>
        private void DrawTargetSection()
        {
            EditorGUILayout.LabelField("■ ターゲット", EditorStyles.boldLabel);

            var newTarget = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(
                "Skinned Mesh Renderer",
                _targetRenderer,
                typeof(SkinnedMeshRenderer),
                true
            );

            if (newTarget != _targetRenderer)
            {
                _targetRenderer = newTarget;
                _lastResult = null;
                _statusMessage = "";
            }

            // アクティブな選択からの自動取得ボタン
            if (GUILayout.Button("Hierarchyの選択から取得"))
            {
                if (Selection.activeGameObject != null)
                {
                    var smr = Selection.activeGameObject.GetComponent<SkinnedMeshRenderer>();
                    if (smr != null)
                    {
                        _targetRenderer = smr;
                        _lastResult = null;
                        _statusMessage = "";
                    }
                    else
                    {
                        _statusMessage = "⚠ 選択オブジェクトにSkinnedMeshRendererがありません";
                    }
                }
                else
                {
                    _statusMessage = "⚠ Hierarchyでオブジェクトを選択してください";
                }
            }

            // 情報表示
            if (_targetRenderer != null)
            {
                var mats = _targetRenderer.sharedMaterials;
                var mesh = _targetRenderer.sharedMesh;
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.LabelField("  オブジェクト", _targetRenderer.gameObject.name);
                EditorGUILayout.LabelField("  マテリアル数", mats != null ? mats.Length.ToString() : "0");
                EditorGUILayout.LabelField("  サブメッシュ数", mesh != null ? mesh.subMeshCount.ToString() : "0");
                EditorGUILayout.LabelField("  頂点数", mesh != null ? mesh.vertexCount.ToString("N0") : "0");
                EditorGUI.EndDisabledGroup();

                if (mats != null && mats.Length <= 1)
                {
                    EditorGUILayout.HelpBox("マテリアルが1つ以下のため、アトラス化は不要です。", MessageType.Info);
                }
            }
        }

        /// <summary>マテリアル一覧セクション</summary>
        private void DrawMaterialListSection()
        {
            if (_targetRenderer == null) return;

            _showMaterialList = EditorGUILayout.BeginFoldoutHeaderGroup(_showMaterialList, "■ マテリアル一覧");
            if (_showMaterialList)
            {
                var mats = _targetRenderer.sharedMaterials;
                if (mats != null)
                {
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var mat = mats[i];
                        if (mat == null)
                        {
                            EditorGUILayout.LabelField($"  [{i}] (Missing)");
                            continue;
                        }

                        GUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField($"  [{i}]", GUILayout.Width(30));
                        EditorGUILayout.LabelField(mat.name, GUILayout.Width(180));

                        string shaderName = mat.shader != null ? mat.shader.name : "None";
                        // 長いシェーダー名を省略
                        if (shaderName.Length > 20)
                            shaderName = "..." + shaderName.Substring(shaderName.Length - 20);
                        EditorGUILayout.LabelField(shaderName, EditorStyles.miniLabel);
                        GUILayout.EndHorizontal();
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        /// <summary>設定セクション</summary>
        private void DrawSettingsSection()
        {
            _showAdvancedSettings = EditorGUILayout.BeginFoldoutHeaderGroup(_showAdvancedSettings, "■ 詳細設定");
            if (_showAdvancedSettings)
            {
                _atlasSizeIndex = EditorGUILayout.Popup("最大アトラスサイズ", _atlasSizeIndex, _atlasSizeOptions);
                _maxAtlasSize = _atlasSizeValues[_atlasSizeIndex];

                _padding = EditorGUILayout.IntSlider("パディング (px)", _padding, 0, 16);
                _includeNormalMap = EditorGUILayout.Toggle("法線マップを含める", _includeNormalMap);
                _includeEmissionMap = EditorGUILayout.Toggle("エミッションマップを含める", _includeEmissionMap);
                _includeOcclusionMap = EditorGUILayout.Toggle("オクルージョンマップを含める", _includeOcclusionMap);
                _preserveTexelDensity = EditorGUILayout.Toggle("テクセル密度を保持", _preserveTexelDensity);

                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(
                    "パディング: ミップマップのにじみ防止。通常4で十分です。\n" +
                    "テクセル密度保持: 大きいテクスチャにより多くの領域を割り当てます。",
                    MessageType.Info
                );
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        /// <summary>実行セクション</summary>
        private void DrawExecuteSection()
        {
            EditorGUILayout.LabelField("■ 実行", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(_targetRenderer == null || _isProcessing ||
                (_targetRenderer != null && _targetRenderer.sharedMaterials != null && _targetRenderer.sharedMaterials.Length <= 1));

            // メインの実行ボタン
            GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f, 1f);
            if (GUILayout.Button("🚀 アトラス化を実行", GUILayout.Height(40)))
            {
                ExecuteAtlasGeneration();
            }
            GUI.backgroundColor = Color.white;

            EditorGUI.EndDisabledGroup();

            // 結果の適用ボタン
            if (_lastResult != null && _lastResult.Success)
            {
                EditorGUILayout.Space();
                GUI.backgroundColor = new Color(0.3f, 0.6f, 1f, 1f);
                if (GUILayout.Button("✅ 結果をアバターに適用（Undo対応）", GUILayout.Height(30)))
                {
                    ApplyResult();
                }
                GUI.backgroundColor = Color.white;

                if (GUILayout.Button("💾 アセットとして保存"))
                {
                    SaveResultAsAssets();
                }
            }
        }

        /// <summary>ステータス表示セクション</summary>
        private void DrawStatusSection()
        {
            if (!string.IsNullOrEmpty(_statusMessage))
            {
                MessageType msgType = MessageType.Info;
                if (_statusMessage.StartsWith("⚠")) msgType = MessageType.Warning;
                if (_statusMessage.StartsWith("❌")) msgType = MessageType.Error;
                if (_statusMessage.StartsWith("✅")) msgType = MessageType.Info;
                EditorGUILayout.HelpBox(_statusMessage, msgType);
            }

            if (_lastResult != null && _lastResult.Success)
            {
                EditorGUILayout.LabelField("■ 結果", EditorStyles.boldLabel);
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.LabelField("  マテリアル数", $"{_lastResult.OriginalMaterialCount} → 1");
                EditorGUILayout.LabelField("  アトラスサイズ", $"{_lastResult.AtlasSize} x {_lastResult.AtlasSize}");
                EditorGUILayout.LabelField("  メインテクスチャ", _lastResult.AtlasMainTex != null ? "✓" : "✗");
                EditorGUILayout.LabelField("  法線マップ", _lastResult.AtlasBumpMap != null ? "✓" : "✗");
                EditorGUILayout.LabelField("  エミッション", _lastResult.AtlasEmissionMap != null ? "✓" : "✗");
                EditorGUI.EndDisabledGroup();
            }
        }

        /// <summary>アトラス生成を実行</summary>
        private void ExecuteAtlasGeneration()
        {
            _isProcessing = true;
            _statusMessage = "処理中...";

            try
            {
                EditorUtility.DisplayProgressBar(TOOL_NAME, "マテリアルを解析中...", 0.1f);

                var settings = new AtlasGenerator.AtlasSettings
                {
                    MaxAtlasSize = _maxAtlasSize,
                    Padding = _padding,
                    IncludeNormalMap = _includeNormalMap,
                    IncludeEmissionMap = _includeEmissionMap,
                    IncludeOcclusionMap = _includeOcclusionMap,
                    PreserveTexelDensity = _preserveTexelDensity,
                };

                EditorUtility.DisplayProgressBar(TOOL_NAME, "アトラスを生成中...", 0.5f);
                _lastResult = AtlasGenerator.Generate(_targetRenderer, settings);

                if (_lastResult.Success)
                {
                    _statusMessage = $"✅ 成功: {_lastResult.OriginalMaterialCount}マテリアル → 1マテリアル (アトラスサイズ: {_lastResult.AtlasSize}x{_lastResult.AtlasSize})";
                }
                else
                {
                    _statusMessage = $"❌ {_lastResult.ErrorMessage}";
                }
            }
            catch (Exception ex)
            {
                _statusMessage = $"❌ エラー: {ex.Message}";
                Debug.LogError($"[{TOOL_NAME}] {ex}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                _isProcessing = false;
                Repaint();
            }
        }

        /// <summary>結果をアバターに適用</summary>
        private void ApplyResult()
        {
            if (_lastResult == null || !_lastResult.Success || _targetRenderer == null) return;

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("VRC Material Atlas Apply");

            try
            {
                Undo.RecordObject(_targetRenderer, "Apply Material Atlas");

                // メッシュを差し替え
                _targetRenderer.sharedMesh = _lastResult.RemappedMesh;

                // マテリアルを1つに統合
                _targetRenderer.sharedMaterials = new Material[] { _lastResult.AtlasMaterial };

                EditorUtility.SetDirty(_targetRenderer);
                _statusMessage = "✅ アバターに適用完了（Ctrl+Zで元に戻せます）";
            }
            catch (Exception ex)
            {
                _statusMessage = $"❌ 適用エラー: {ex.Message}";
                Debug.LogError($"[{TOOL_NAME}] Apply error: {ex}");
            }

            Undo.CollapseUndoOperations(undoGroup);
            Repaint();
        }

        /// <summary>結果をアセットとして保存</summary>
        private void SaveResultAsAssets()
        {
            if (_lastResult == null || !_lastResult.Success) return;

            try
            {
                // 出力フォルダ確認・作成
                EnsureOutputFolder();

                string baseName = _targetRenderer.gameObject.name;
                string folderPath = OUTPUT_FOLDER;

                EditorUtility.DisplayProgressBar(TOOL_NAME, "アセットを保存中...", 0.3f);

                // テクスチャ保存（PNGとして）
                SaveTexturePNG(_lastResult.AtlasMainTex, $"{folderPath}/{baseName}_Atlas_Main.png");
                if (_lastResult.AtlasBumpMap != null)
                    SaveTexturePNG(_lastResult.AtlasBumpMap, $"{folderPath}/{baseName}_Atlas_Normal.png");
                if (_lastResult.AtlasEmissionMap != null)
                    SaveTexturePNG(_lastResult.AtlasEmissionMap, $"{folderPath}/{baseName}_Atlas_Emission.png");

                // メッシュ保存
                AssetDatabase.CreateAsset(UnityEngine.Object.Instantiate(_lastResult.RemappedMesh),
                    $"{folderPath}/{baseName}_Atlas_Mesh.asset");

                // マテリアル保存
                AssetDatabase.CreateAsset(new Material(_lastResult.AtlasMaterial),
                    $"{folderPath}/{baseName}_Atlas_Material.mat");

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                _statusMessage = $"✅ アセットを保存しました: {folderPath}";
            }
            catch (Exception ex)
            {
                _statusMessage = $"❌ 保存エラー: {ex.Message}";
                Debug.LogError($"[{TOOL_NAME}] Save error: {ex}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                Repaint();
            }
        }

        /// <summary>出力フォルダの作成</summary>
        private void EnsureOutputFolder()
        {
            string[] parts = OUTPUT_FOLDER.Split('/');
            string current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        /// <summary>テクスチャをPNGとして保存</summary>
        private void SaveTexturePNG(Texture2D tex, string path)
        {
            if (tex == null) return;
            byte[] pngData = tex.EncodeToPNG();
            string fullPath = Path.Combine(Application.dataPath, "..", path);
            File.WriteAllBytes(fullPath, pngData);
        }
    }
}
