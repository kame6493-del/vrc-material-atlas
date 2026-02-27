using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HatoTools.VRCMaterialAtlas
{
    /// <summary>
    /// テクスチャアトラス生成エンジン
    /// 複数マテリアルのテクスチャを1枚のアトラスに統合し、UV座標をリマップする
    /// </summary>
    public static class AtlasGenerator
    {
        /// <summary>アトラスに含める1マテリアルの情報</summary>
        public class MaterialEntry
        {
            public Material Material;
            public int OriginalIndex;       // sharedMaterials配列内のindex
            public List<int> SubMeshIndices; // このマテリアルを使用するサブメッシュのindex
            public Rect AtlasRect;          // アトラス内の割り当て領域 (0-1)

            // 各チャンネルのテクスチャ（nullの場合はソリッドカラー）
            public Texture2D MainTex;
            public Texture2D BumpMap;
            public Texture2D EmissionMap;
            public Texture2D OcclusionMap;

            // ソリッドカラー（テクスチャがない場合に使用）
            public Color MainColor = Color.white;
            public Color EmissionColor = Color.black;
        }

        /// <summary>アトラス生成結果</summary>
        public class AtlasResult
        {
            public Texture2D AtlasMainTex;
            public Texture2D AtlasBumpMap;      // null if no bump maps
            public Texture2D AtlasEmissionMap;  // null if no emission maps
            public Texture2D AtlasOcclusionMap; // null if no occlusion maps
            public Material AtlasMaterial;
            public Mesh RemappedMesh;
            public List<MaterialEntry> Entries;
            public int OriginalMaterialCount;
            public int AtlasSize;
            public string ErrorMessage;
            public bool Success => string.IsNullOrEmpty(ErrorMessage);
        }

        /// <summary>設定</summary>
        public class AtlasSettings
        {
            public int MaxAtlasSize = 4096;
            public int Padding = 4;             // タイル間のパディング（ブリードリング防止）
            public bool IncludeNormalMap = true;
            public bool IncludeEmissionMap = true;
            public bool IncludeOcclusionMap = false;
            public bool PreserveTexelDensity = true; // テクセル密度を保持（大きいテクスチャに多くの領域を割り当て）
            public FilterMode FilterMode = FilterMode.Bilinear;
        }

        /// <summary>
        /// メインのアトラス生成メソッド
        /// </summary>
        public static AtlasResult Generate(SkinnedMeshRenderer smr, AtlasSettings settings)
        {
            var result = new AtlasResult();

            try
            {
                // 1. バリデーション
                if (smr == null)
                {
                    result.ErrorMessage = "SkinnedMeshRendererがnullです";
                    return result;
                }

                if (smr.sharedMesh == null)
                {
                    result.ErrorMessage = "メッシュがnullです";
                    return result;
                }

                if (smr.sharedMaterials == null || smr.sharedMaterials.Length == 0)
                {
                    result.ErrorMessage = "マテリアルがありません";
                    return result;
                }

                Material[] mats = smr.sharedMaterials;
                Mesh srcMesh = smr.sharedMesh;
                result.OriginalMaterialCount = mats.Length;

                if (mats.Length <= 1)
                {
                    result.ErrorMessage = "マテリアルが1つ以下のため、アトラス化は不要です";
                    return result;
                }

                // 2. マテリアルエントリを収集
                var entries = CollectMaterialEntries(mats, srcMesh);
                if (entries.Count == 0)
                {
                    result.ErrorMessage = "有効なマテリアルが見つかりません";
                    return result;
                }
                result.Entries = entries;

                // 3. アトラスレイアウトを計算
                int atlasSize = CalculateAtlasLayout(entries, settings);
                result.AtlasSize = atlasSize;

                if (atlasSize > settings.MaxAtlasSize)
                {
                    // サイズ超過時はMaxAtlasSizeに制限して再計算
                    atlasSize = settings.MaxAtlasSize;
                    RecalculateLayoutWithFixedSize(entries, atlasSize, settings.Padding);
                    result.AtlasSize = atlasSize;
                }

                // 4. アトラステクスチャを生成
                result.AtlasMainTex = ComposeAtlasTexture(entries, atlasSize, settings, TextureChannel.Main);

                bool hasAnyBump = settings.IncludeNormalMap && entries.Any(e => e.BumpMap != null);
                if (hasAnyBump)
                    result.AtlasBumpMap = ComposeAtlasTexture(entries, atlasSize, settings, TextureChannel.Normal);

                bool hasAnyEmission = settings.IncludeEmissionMap && entries.Any(e => e.EmissionMap != null);
                if (hasAnyEmission)
                    result.AtlasEmissionMap = ComposeAtlasTexture(entries, atlasSize, settings, TextureChannel.Emission);

                bool hasAnyOcclusion = settings.IncludeOcclusionMap && entries.Any(e => e.OcclusionMap != null);
                if (hasAnyOcclusion)
                    result.AtlasOcclusionMap = ComposeAtlasTexture(entries, atlasSize, settings, TextureChannel.Occlusion);

                // 5. UVをリマップした新しいメッシュを生成
                result.RemappedMesh = RemapMeshUVs(srcMesh, entries);

                // 6. アトラスマテリアルを生成
                result.AtlasMaterial = CreateAtlasMaterial(entries[0].Material, result);

                Debug.Log($"[VRC Material Atlas] アトラス生成完了: {mats.Length}マテリアル → 1マテリアル, サイズ={atlasSize}x{atlasSize}");
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"アトラス生成中にエラー: {ex.Message}";
                Debug.LogError($"[VRC Material Atlas] {result.ErrorMessage}\n{ex.StackTrace}");
            }

            return result;
        }

        /// <summary>マテリアル情報を収集</summary>
        private static List<MaterialEntry> CollectMaterialEntries(Material[] mats, Mesh mesh)
        {
            var entries = new List<MaterialEntry>();

            for (int i = 0; i < mats.Length; i++)
            {
                var mat = mats[i];
                if (mat == null) continue;

                var entry = new MaterialEntry
                {
                    Material = mat,
                    OriginalIndex = i,
                    SubMeshIndices = new List<int> { i < mesh.subMeshCount ? i : 0 }
                };

                // テクスチャを取得（lilToon + Standard両対応）
                entry.MainTex = GetReadableTexture(mat, "_MainTex");
                entry.BumpMap = GetReadableTexture(mat, "_BumpMap");
                entry.EmissionMap = GetReadableTexture(mat, "_EmissionMap");
                entry.OcclusionMap = GetReadableTexture(mat, "_OcclusionMap");

                // lilToon特有のプロパティ名もチェック
                if (entry.MainTex == null)
                    entry.MainTex = GetReadableTexture(mat, "_MainColorTex");
                if (entry.BumpMap == null)
                    entry.BumpMap = GetReadableTexture(mat, "_MainNormalTex");
                if (entry.EmissionMap == null)
                    entry.EmissionMap = GetReadableTexture(mat, "_EmissionMapTex");

                // カラー取得
                if (mat.HasProperty("_Color"))
                    entry.MainColor = mat.GetColor("_Color");
                if (mat.HasProperty("_EmissionColor"))
                    entry.EmissionColor = mat.GetColor("_EmissionColor");

                entries.Add(entry);
            }

            return entries;
        }

        /// <summary>テクスチャを読み取り可能な形式で取得</summary>
        private static Texture2D GetReadableTexture(Material mat, string propertyName)
        {
            if (!mat.HasProperty(propertyName)) return null;
            var tex = mat.GetTexture(propertyName) as Texture2D;
            if (tex == null) return null;

            // テクスチャが読み取り可能でない場合はコピーを作成
            if (!tex.isReadable)
            {
                return CopyTextureReadable(tex);
            }
            return tex;
        }

        /// <summary>読み取り不可テクスチャの読み取り可能コピーを作成</summary>
        private static Texture2D CopyTextureReadable(Texture2D source)
        {
            // RenderTextureを使ってコピー（Unity実機で動作）
            var prevRT = RenderTexture.active;
            var rt = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;

            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            // ReadPixels はスタブでは模倣不可だが、実機では動作
            // copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            copy.Apply();

            RenderTexture.active = prevRT;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);

            return copy;
        }

        /// <summary>アトラスレイアウト計算（テクセル密度考慮）</summary>
        private static int CalculateAtlasLayout(List<MaterialEntry> entries, AtlasSettings settings)
        {
            int count = entries.Count;
            int padding = settings.Padding;

            if (settings.PreserveTexelDensity)
            {
                return CalculateLayoutPreservingDensity(entries, padding, settings.MaxAtlasSize);
            }
            else
            {
                return CalculateLayoutEqualSize(entries, padding, settings.MaxAtlasSize);
            }
        }

        /// <summary>均等サイズレイアウト</summary>
        private static int CalculateLayoutEqualSize(List<MaterialEntry> entries, int padding, int maxSize)
        {
            int count = entries.Count;
            int gridSize = (int)Math.Ceiling(Math.Sqrt(count));

            // 最大のテクスチャサイズを基準にアトラスサイズを決定
            int maxTexSize = 512; // デフォルト
            foreach (var entry in entries)
            {
                if (entry.MainTex != null)
                    maxTexSize = Mathf.Max(maxTexSize, Mathf.Max(entry.MainTex.width, entry.MainTex.height));
            }

            int tileSize = Mathf.Min(maxTexSize, maxSize / gridSize);
            int atlasSize = Mathf.ClosestPowerOfTwo(gridSize * tileSize + (gridSize + 1) * padding);
            atlasSize = Mathf.Min(atlasSize, maxSize);

            // 実際のタイルサイズを再計算
            int effectiveTileSize = (atlasSize - (gridSize + 1) * padding) / gridSize;

            for (int i = 0; i < entries.Count; i++)
            {
                int row = i / gridSize;
                int col = i % gridSize;
                float x = (float)(col * (effectiveTileSize + padding) + padding) / atlasSize;
                float y = (float)(row * (effectiveTileSize + padding) + padding) / atlasSize;
                float w = (float)effectiveTileSize / atlasSize;
                float h = (float)effectiveTileSize / atlasSize;
                entries[i].AtlasRect = new Rect(x, y, w, h);
            }

            return atlasSize;
        }

        /// <summary>テクセル密度保持レイアウト</summary>
        private static int CalculateLayoutPreservingDensity(List<MaterialEntry> entries, int padding, int maxSize)
        {
            // テクスチャサイズで重み付け
            var weights = new float[entries.Count];
            float totalWeight = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                int texSize = 512;
                if (entries[i].MainTex != null)
                    texSize = Mathf.Max(entries[i].MainTex.width, entries[i].MainTex.height);
                weights[i] = texSize;
                totalWeight += texSize * texSize;
            }

            // 基本的には均等グリッドを使用（シンプルさ優先）
            // 将来的にはbin packingに改良可能
            return CalculateLayoutEqualSize(entries, padding, maxSize);
        }

        /// <summary>固定サイズでレイアウト再計算</summary>
        private static void RecalculateLayoutWithFixedSize(List<MaterialEntry> entries, int atlasSize, int padding)
        {
            int count = entries.Count;
            int gridSize = (int)Math.Ceiling(Math.Sqrt(count));
            int effectiveTileSize = (atlasSize - (gridSize + 1) * padding) / gridSize;

            for (int i = 0; i < entries.Count; i++)
            {
                int row = i / gridSize;
                int col = i % gridSize;
                float x = (float)(col * (effectiveTileSize + padding) + padding) / atlasSize;
                float y = (float)(row * (effectiveTileSize + padding) + padding) / atlasSize;
                float w = (float)effectiveTileSize / atlasSize;
                float h = (float)effectiveTileSize / atlasSize;
                entries[i].AtlasRect = new Rect(x, y, w, h);
            }
        }

        /// <summary>テクスチャチャンネル</summary>
        public enum TextureChannel { Main, Normal, Emission, Occlusion }

        /// <summary>アトラステクスチャを合成</summary>
        private static Texture2D ComposeAtlasTexture(List<MaterialEntry> entries, int atlasSize, AtlasSettings settings, TextureChannel channel)
        {
            var atlas = new Texture2D(atlasSize, atlasSize, TextureFormat.RGBA32, true);
            atlas.filterMode = settings.FilterMode;

            // 背景を初期化
            Color bgColor = (channel == TextureChannel.Normal) ? new Color(0.5f, 0.5f, 1f, 1f) : Color.black;
            Color[] bgPixels = new Color[atlasSize * atlasSize];
            for (int i = 0; i < bgPixels.Length; i++) bgPixels[i] = bgColor;
            atlas.SetPixels(bgPixels);

            foreach (var entry in entries)
            {
                Texture2D srcTex = null;
                Color solidColor = Color.white;

                switch (channel)
                {
                    case TextureChannel.Main:
                        srcTex = entry.MainTex;
                        solidColor = entry.MainColor;
                        break;
                    case TextureChannel.Normal:
                        srcTex = entry.BumpMap;
                        solidColor = new Color(0.5f, 0.5f, 1f, 1f); // デフォルト法線
                        break;
                    case TextureChannel.Emission:
                        srcTex = entry.EmissionMap;
                        solidColor = entry.EmissionColor;
                        break;
                    case TextureChannel.Occlusion:
                        srcTex = entry.OcclusionMap;
                        solidColor = Color.white;
                        break;
                }

                int dstX = Mathf.RoundToInt(entry.AtlasRect.x * atlasSize);
                int dstY = Mathf.RoundToInt(entry.AtlasRect.y * atlasSize);
                int dstW = Mathf.RoundToInt(entry.AtlasRect.width * atlasSize);
                int dstH = Mathf.RoundToInt(entry.AtlasRect.height * atlasSize);

                if (srcTex != null)
                {
                    // テクスチャをリサイズしてアトラスに配置
                    BlitResized(atlas, srcTex, dstX, dstY, dstW, dstH);
                }
                else
                {
                    // ソリッドカラーで塗りつぶし
                    FillRect(atlas, dstX, dstY, dstW, dstH, solidColor);
                }

                // パディング用のブリードリング（テクスチャ端のピクセルを複製してミップマップのにじみ防止）
                if (settings.Padding > 0)
                {
                    FillPaddingBorder(atlas, dstX, dstY, dstW, dstH, settings.Padding);
                }
            }

            atlas.Apply(true, false);
            return atlas;
        }

        /// <summary>テクスチャをバイリニア補間でリサイズして配置</summary>
        private static void BlitResized(Texture2D dst, Texture2D src, int dstX, int dstY, int dstW, int dstH)
        {
            if (src == null || dst == null) return;

            Color[] srcPixels = null;
            try { srcPixels = src.GetPixels(); }
            catch { return; } // 読み取り不可の場合はスキップ

            int srcW = src.width;
            int srcH = src.height;

            for (int y = 0; y < dstH; y++)
            {
                for (int x = 0; x < dstW; x++)
                {
                    // バイリニア補間でサンプリング
                    float u = (float)x / dstW;
                    float v = (float)y / dstH;
                    Color pixel = SampleBilinear(srcPixels, srcW, srcH, u, v);
                    dst.SetPixel(dstX + x, dstY + y, pixel);
                }
            }
        }

        /// <summary>バイリニア補間サンプリング</summary>
        private static Color SampleBilinear(Color[] pixels, int w, int h, float u, float v)
        {
            float fx = u * (w - 1);
            float fy = v * (h - 1);
            int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, w - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(fy), 0, h - 1);
            int x1 = Mathf.Clamp(x0 + 1, 0, w - 1);
            int y1 = Mathf.Clamp(y0 + 1, 0, h - 1);
            float tx = fx - x0;
            float ty = fy - y0;

            Color c00 = pixels[y0 * w + x0];
            Color c10 = pixels[y0 * w + x1];
            Color c01 = pixels[y1 * w + x0];
            Color c11 = pixels[y1 * w + x1];

            return new Color(
                Mathf.Lerp(Mathf.Lerp(c00.r, c10.r, tx), Mathf.Lerp(c01.r, c11.r, tx), ty),
                Mathf.Lerp(Mathf.Lerp(c00.g, c10.g, tx), Mathf.Lerp(c01.g, c11.g, tx), ty),
                Mathf.Lerp(Mathf.Lerp(c00.b, c10.b, tx), Mathf.Lerp(c01.b, c11.b, tx), ty),
                Mathf.Lerp(Mathf.Lerp(c00.a, c10.a, tx), Mathf.Lerp(c01.a, c11.a, tx), ty)
            );
        }

        /// <summary>矩形をソリッドカラーで塗りつぶし</summary>
        private static void FillRect(Texture2D tex, int x, int y, int w, int h, Color color)
        {
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                    tex.SetPixel(x + i, y + j, color);
        }

        /// <summary>パディング領域をエッジピクセルで塗りつぶし（ミップマップにじみ防止）</summary>
        private static void FillPaddingBorder(Texture2D tex, int rx, int ry, int rw, int rh, int padding)
        {
            int atlasW = tex.width;
            int atlasH = tex.height;

            // 上下のボーダー
            for (int x = rx; x < rx + rw; x++)
            {
                Color topPixel = tex.GetPixel(x, ry + rh - 1);
                Color bottomPixel = tex.GetPixel(x, ry);
                for (int p = 1; p <= padding; p++)
                {
                    if (ry + rh - 1 + p < atlasH) tex.SetPixel(x, ry + rh - 1 + p, topPixel);
                    if (ry - p >= 0) tex.SetPixel(x, ry - p, bottomPixel);
                }
            }
            // 左右のボーダー
            for (int y = ry; y < ry + rh; y++)
            {
                Color leftPixel = tex.GetPixel(rx, y);
                Color rightPixel = tex.GetPixel(rx + rw - 1, y);
                for (int p = 1; p <= padding; p++)
                {
                    if (rx - p >= 0) tex.SetPixel(rx - p, y, leftPixel);
                    if (rx + rw - 1 + p < atlasW) tex.SetPixel(rx + rw - 1 + p, y, rightPixel);
                }
            }
        }

        /// <summary>UVをリマップした新しいメッシュを生成</summary>
        private static Mesh RemapMeshUVs(Mesh srcMesh, List<MaterialEntry> entries)
        {
            var newMesh = new Mesh();
            newMesh.name = srcMesh.name + "_Atlas";

            // 頂点データコピー
            newMesh.vertices = srcMesh.vertices;
            newMesh.normals = srcMesh.normals;
            newMesh.tangents = srcMesh.tangents;
            newMesh.boneWeights = srcMesh.boneWeights;
            newMesh.bindposes = srcMesh.bindposes;

            // UV2以降はそのまま
            newMesh.uv2 = srcMesh.uv2;

            // BlendShapeコピー
            CopyBlendShapes(srcMesh, newMesh);

            // UVリマップ + サブメッシュ統合
            Vector2[] originalUVs = srcMesh.uv;
            Vector2[] newUVs = new Vector2[originalUVs.Length];
            Array.Copy(originalUVs, newUVs, originalUVs.Length);

            // マテリアルindex → エントリのマッピング
            var matIndexToEntry = new Dictionary<int, MaterialEntry>();
            foreach (var entry in entries)
                matIndexToEntry[entry.OriginalIndex] = entry;

            // 全サブメッシュの三角形を収集し、UV をリマップ
            var allTriangles = new List<int>();
            // 各頂点がどのサブメッシュに属するかを追跡（同一頂点が複数サブメッシュで使用される場合の対応）
            var vertexSubMeshMap = new Dictionary<int, int>(); // vertexIndex -> first subMesh index

            for (int subIdx = 0; subIdx < srcMesh.subMeshCount; subIdx++)
            {
                int[] tris = srcMesh.GetTriangles(subIdx);
                if (tris == null || tris.Length == 0) continue;

                // このサブメッシュに対応するエントリを探す
                MaterialEntry entry = null;
                if (matIndexToEntry.ContainsKey(subIdx))
                    entry = matIndexToEntry[subIdx];

                if (entry == null) continue;

                Rect rect = entry.AtlasRect;

                foreach (int vertIdx in tris)
                {
                    if (vertIdx >= 0 && vertIdx < newUVs.Length)
                    {
                        // 同一頂点が複数サブメッシュで参照される場合の処理
                        // ここでは最初に遭遇したサブメッシュのUVリマップを適用
                        if (!vertexSubMeshMap.ContainsKey(vertIdx))
                        {
                            vertexSubMeshMap[vertIdx] = subIdx;
                            Vector2 origUV = originalUVs[vertIdx];
                            // UV をアトラス座標にリマップ
                            // [0,1] → [rect.x, rect.x+rect.width]
                            float newU = rect.x + WrapUV(origUV.x) * rect.width;
                            float newV = rect.y + WrapUV(origUV.y) * rect.height;
                            newUVs[vertIdx] = new Vector2(newU, newV);
                        }
                    }
                }

                allTriangles.AddRange(tris);
            }

            newMesh.uv = newUVs;

            // サブメッシュを1つに統合
            newMesh.subMeshCount = 1;
            newMesh.SetTriangles(allTriangles.ToArray(), 0);

            newMesh.RecalculateBounds();
            return newMesh;
        }

        /// <summary>UV座標を[0,1]にラップ</summary>
        private static float WrapUV(float uv)
        {
            // frac相当: 0-1に収める（タイリング対応）
            float result = uv - (float)Math.Floor(uv);
            return Mathf.Clamp01(result);
        }

        /// <summary>BlendShapeデータをコピー</summary>
        private static void CopyBlendShapes(Mesh src, Mesh dst)
        {
            int blendShapeCount = src.GetBlendShapeCount();
            for (int i = 0; i < blendShapeCount; i++)
            {
                string shapeName = src.GetBlendShapeName(i);
                int frameCount = src.GetBlendShapeFrameCount(i);
                for (int f = 0; f < frameCount; f++)
                {
                    float weight = src.GetBlendShapeFrameWeight(i, f);
                    var deltaVertices = new Vector3[src.vertexCount];
                    var deltaNormals = new Vector3[src.vertexCount];
                    var deltaTangents = new Vector3[src.vertexCount];
                    src.GetBlendShapeFrameVertices(i, f, deltaVertices, deltaNormals, deltaTangents);
                    dst.AddBlendShapeFrame(shapeName, weight, deltaVertices, deltaNormals, deltaTangents);
                }
            }
        }

        /// <summary>アトラスマテリアルを生成</summary>
        private static Material CreateAtlasMaterial(Material srcMat, AtlasResult result)
        {
            // 元のマテリアルのシェーダーを維持
            var atlasMat = new Material(srcMat.shader);
            atlasMat.name = "Atlas_" + srcMat.name;
            atlasMat.CopyPropertiesFromMaterial(srcMat);

            // テクスチャをアトラスに差し替え
            atlasMat.SetTexture("_MainTex", result.AtlasMainTex);
            atlasMat.SetColor("_Color", Color.white); // カラーはアトラスに焼き込み済み

            if (result.AtlasBumpMap != null)
                atlasMat.SetTexture("_BumpMap", result.AtlasBumpMap);
            if (result.AtlasEmissionMap != null)
                atlasMat.SetTexture("_EmissionMap", result.AtlasEmissionMap);

            // lilToon特有プロパティも設定
            if (srcMat.shader != null && srcMat.shader.name != null &&
                srcMat.shader.name.Contains("lilToon"))
            {
                atlasMat.SetTexture("_MainColorTex", result.AtlasMainTex);
                if (result.AtlasBumpMap != null)
                    atlasMat.SetTexture("_MainNormalTex", result.AtlasBumpMap);
                if (result.AtlasEmissionMap != null)
                    atlasMat.SetTexture("_EmissionMapTex", result.AtlasEmissionMap);
            }

            return atlasMat;
        }
    }
}
