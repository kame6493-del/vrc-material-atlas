using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace HatoTools.VRCMaterialAtlas.Tests
{
    /// <summary>
    /// Unity Test Framework テスト（実際のUnity Editor上で実行）
    /// GameCI GitHub Actions で自動実行される
    /// </summary>
    [TestFixture]
    public class AtlasGeneratorTests
    {
        // === ヘルパー ===

        private Texture2D CreateTex(int w, int h, Color color)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = color;
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        private Texture2D CreateGradientTex(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = new Color((float)x / w, (float)y / h, 0.5f, 1f);
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        private Material CreateMat(string name, Color color, Texture2D mainTex = null)
        {
            // Unity実機ではStandard Shaderを使用（lilToonは別途インストールが必要なため）
            var mat = new Material(Shader.Find("Standard"));
            mat.name = name;
            mat.SetColor("_Color", color);
            if (mainTex != null)
            {
                mat.SetTexture("_MainTex", mainTex);
                mat.mainTexture = mainTex;
            }
            return mat;
        }

        private Mesh CreateMesh(int subMeshCount)
        {
            var mesh = new Mesh();
            mesh.name = "TestMesh";

            int totalVerts = subMeshCount * 4;
            var verts = new Vector3[totalVerts];
            var normals = new Vector3[totalVerts];
            var uvs = new Vector2[totalVerts];
            var tangents = new Vector4[totalVerts];
            var boneWeights = new BoneWeight[totalVerts];

            for (int s = 0; s < subMeshCount; s++)
            {
                int b = s * 4;
                float off = s * 2f;
                verts[b + 0] = new Vector3(off, 0, 0);
                verts[b + 1] = new Vector3(off + 1, 0, 0);
                verts[b + 2] = new Vector3(off, 1, 0);
                verts[b + 3] = new Vector3(off + 1, 1, 0);

                for (int i = 0; i < 4; i++)
                {
                    normals[b + i] = Vector3.forward;
                    tangents[b + i] = new Vector4(1, 0, 0, 1);
                    boneWeights[b + i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
                }

                uvs[b + 0] = new Vector2(0, 0);
                uvs[b + 1] = new Vector2(1, 0);
                uvs[b + 2] = new Vector2(0, 1);
                uvs[b + 3] = new Vector2(1, 1);
            }

            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.tangents = tangents;
            mesh.uv = uvs;
            mesh.boneWeights = boneWeights;
            mesh.bindposes = new Matrix4x4[] { Matrix4x4.identity };

            mesh.subMeshCount = subMeshCount;
            for (int s = 0; s < subMeshCount; s++)
            {
                int b = s * 4;
                mesh.SetTriangles(new int[] { b, b + 1, b + 2, b + 1, b + 3, b + 2 }, s);
            }

            return mesh;
        }

        private SkinnedMeshRenderer CreateSMR(int matCount, bool withTex = false, int texSize = 256)
        {
            var go = new GameObject("TestAvatar_" + matCount);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = CreateMesh(matCount);

            Color[] colors = {
                Color.red, Color.green, Color.blue, Color.yellow,
                Color.magenta, Color.cyan, new Color(0.5f,0.5f,0,1), new Color(0,0.5f,0.5f,1),
                new Color(0.8f,0.2f,0.2f,1), new Color(0.2f,0.8f,0.2f,1),
                new Color(0.2f,0.2f,0.8f,1), new Color(0.9f,0.9f,0.1f,1),
                new Color(0.3f,0.3f,0.3f,1), new Color(0.6f,0.6f,0.6f,1),
                new Color(0.4f,0.2f,0.8f,1), new Color(0.8f,0.4f,0.2f,1),
            };

            var mats = new Material[matCount];
            for (int i = 0; i < matCount; i++)
            {
                var c = colors[i % colors.Length];
                Texture2D tex = withTex ? CreateTex(texSize, texSize, c) : null;
                mats[i] = CreateMat($"Mat_{i}", c, tex);
            }
            smr.sharedMaterials = mats;
            return smr;
        }

        private AtlasGenerator.AtlasSettings Settings(int maxSize = 4096, int padding = 4)
        {
            return new AtlasGenerator.AtlasSettings
            {
                MaxAtlasSize = maxSize,
                Padding = padding,
                IncludeNormalMap = true,
                IncludeEmissionMap = true,
                IncludeOcclusionMap = false,
                PreserveTexelDensity = true,
            };
        }

        // === Teardown: テストオブジェクト破棄 ===
        private List<GameObject> _createdObjects = new List<GameObject>();

        [TearDown]
        public void Cleanup()
        {
            foreach (var obj in _createdObjects)
            {
                if (obj != null) Object.DestroyImmediate(obj);
            }
            _createdObjects.Clear();
        }

        private SkinnedMeshRenderer TrackSMR(SkinnedMeshRenderer smr)
        {
            _createdObjects.Add(smr.gameObject);
            return smr;
        }

        // ==========================================
        // バリデーションテスト
        // ==========================================

        [Test]
        public void Validate_NullSMR_ReturnsError()
        {
            var result = AtlasGenerator.Generate(null, Settings());
            Assert.IsFalse(result.Success);
            Assert.That(result.ErrorMessage, Does.Contain("null"));
        }

        [Test]
        public void Validate_NullMesh_ReturnsError()
        {
            var go = new GameObject("NullMesh");
            _createdObjects.Add(go);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = null;
            smr.sharedMaterials = new Material[] { CreateMat("a", Color.white), CreateMat("b", Color.black) };

            var result = AtlasGenerator.Generate(smr, Settings());
            Assert.IsFalse(result.Success);
        }

        [Test]
        public void Validate_EmptyMaterials_ReturnsError()
        {
            var go = new GameObject("EmptyMat");
            _createdObjects.Add(go);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = CreateMesh(1);
            smr.sharedMaterials = new Material[0];

            var result = AtlasGenerator.Generate(smr, Settings());
            Assert.IsFalse(result.Success);
        }

        [Test]
        public void Validate_SingleMaterial_SkipsAtlas()
        {
            var smr = TrackSMR(CreateSMR(1));
            var result = AtlasGenerator.Generate(smr, Settings());
            Assert.IsFalse(result.Success);
            Assert.That(result.ErrorMessage, Does.Contain("1つ以下"));
        }

        // ==========================================
        // 基本アトラス生成テスト
        // ==========================================

        [Test]
        public void Atlas_TwoMaterials_Succeeds()
        {
            var smr = TrackSMR(CreateSMR(2, true));
            var result = AtlasGenerator.Generate(smr, Settings());
            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.IsNotNull(result.AtlasMainTex);
            Assert.IsNotNull(result.RemappedMesh);
            Assert.IsNotNull(result.AtlasMaterial);
            Assert.AreEqual(2, result.OriginalMaterialCount);
        }

        [Test]
        public void Atlas_ThreeMaterials_Succeeds()
        {
            var smr = TrackSMR(CreateSMR(3, true));
            var result = AtlasGenerator.Generate(smr, Settings());
            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.AreEqual(3, result.OriginalMaterialCount);
        }

        [Test]
        public void Atlas_FourMaterials_Succeeds()
        {
            var smr = TrackSMR(CreateSMR(4, true));
            var result = AtlasGenerator.Generate(smr, Settings());
            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.AreEqual(4, result.OriginalMaterialCount);
        }

        [Test]
        public void Atlas_EightMaterials_Succeeds()
        {
            var smr = TrackSMR(CreateSMR(8, true));
            var result = AtlasGenerator.Generate(smr, Settings());
            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.AreEqual(8, result.OriginalMaterialCount);
        }

        [Test]
        public void Atlas_SixteenMaterials_Succeeds()
        {
            var smr = TrackSMR(CreateSMR(16, true, 128));
            var result = AtlasGenerator.Generate(smr, Settings());
            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.AreEqual(16, result.OriginalMaterialCount);
        }

        // ==========================================
        // テクスチャ処理テスト
        // ==========================================

        [Test]
        public void Texture_WithTextures_CreatesAtlas()
        {
            var smr = TrackSMR(CreateSMR(3, true));
            var result = AtlasGenerator.Generate(smr, Settings());
            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.Greater(result.AtlasMainTex.width, 0);
            Assert.Greater(result.AtlasMainTex.height, 0);
        }

        [Test]
        public void Texture_SolidColorOnly_CreatesAtlas()
        {
            var smr = TrackSMR(CreateSMR(3, false));
            var result = AtlasGenerator.Generate(smr, Settings());
            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.IsNotNull(result.AtlasMainTex);
        }

        [Test]
        public void Texture_MixedTextureAndSolid_Works()
        {
            var go = new GameObject("Mixed");
            _createdObjects.Add(go);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = CreateMesh(3);
            smr.sharedMaterials = new Material[] {
                CreateMat("WithTex", Color.white, CreateTex(256, 256, Color.white)),
                CreateMat("NoTex", Color.red, null),
                CreateMat("WithTex2", Color.black, CreateTex(128, 128, Color.black)),
            };

            var result = AtlasGenerator.Generate(smr, Settings());
            Assert.IsTrue(result.Success, result.ErrorMessage);
        }

        [Test]
        public void Texture_DifferentSizes_Works()
        {
            var go = new GameObject("DiffSize");
            _createdObjects.Add(go);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = CreateMesh(3);
            smr.sharedMaterials = new Material[] {
                CreateMat("Big", Color.white, CreateTex(1024, 1024, Color.white)),
                CreateMat("Med", Color.white, CreateTex(512, 512, Color.gray)),
                CreateMat("Small", Color.white, CreateTex(128, 128, Color.black)),
            };

            var result = AtlasGenerator.Generate(smr, Settings());
            Assert.IsTrue(result.Success, result.ErrorMessage);
        }

        [Test]
        public void Texture_NormalMap_IncludedWhenPresent()
        {
            var go = new GameObject("WithNormal");
            _createdObjects.Add(go);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = CreateMesh(2);
            var mat1 = CreateMat("M1", Color.white, CreateTex(256, 256, Color.white));
            mat1.SetTexture("_BumpMap", CreateTex(256, 256, new Color(0.5f, 0.5f, 1f)));
            smr.sharedMaterials = new Material[] { mat1, CreateMat("M2", Color.black) };

            var result = AtlasGenerator.Generate(smr, Settings());
            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.IsNotNull(result.AtlasBumpMap, "Normal map atlas should exist");
        }

        [Test]
        public void Texture_EmissionMap_IncludedWhenPresent()
        {
            var go = new GameObject("WithEmission");
            _createdObjects.Add(go);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = CreateMesh(2);
            var mat1 = CreateMat("M1", Color.white, CreateTex(256, 256, Color.white));
            mat1.SetTexture("_EmissionMap", CreateTex(256, 256, Color.red));
            mat1.EnableKeyword("_EMISSION");
            smr.sharedMaterials = new Material[] { mat1, CreateMat("M2", Color.black) };

            var s = Settings();
            s.IncludeEmissionMap = true;
            var result = AtlasGenerator.Generate(smr, s);
            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.IsNotNull(result.AtlasEmissionMap, "Emission atlas should exist");
        }

        // ==========================================
        // UVリマッピングテスト
        // ==========================================

        [Test]
        public void UV_RemappedUVs_AreInRange()
        {
            var smr = TrackSMR(CreateSMR(4, true));
            var result = AtlasGenerator.Generate(smr, Settings());
            Assert.IsTrue(result.Success, result.ErrorMessage);

            foreach (var uv in result.RemappedMesh.uv)
            {
                Assert.GreaterOrEqual(uv.x, 0f, "UV.x >= 0");
                Assert.LessOrEqual(uv.x, 1f, "UV.x <= 1");
                Assert.GreaterOrEqual(uv.y, 0f, "UV.y >= 0");
                Assert.LessOrEqual(uv.y, 1f, "UV.y <= 1");
            }
        }

        [Test]
        public void UV_AtlasRects_DontOverlap()
        {
            var smr = TrackSMR(CreateSMR(4, true));
            var result = AtlasGenerator.Generate(smr, Settings());
            Assert.IsTrue(result.Success, result.ErrorMessage);

            var entries = result.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                for (int j = i + 1; j < entries.Count; j++)
                {
                    var a = entries[i].AtlasRect;
                    var b = entries[j].AtlasRect;
                    bool overlap = !(a.x + a.width <= b.x || b.x + b.width <= a.x ||
                                     a.y + a.height <= b.y || b.y + b.height <= a.y);
                    Assert.IsFalse(overlap, $"Rects [{i}] and [{j}] should not overlap");
                }
            }
        }

        [Test]
        public void UV_WrappedUVs_RemappedCorrectly()
        {
            var go = new GameObject("UVWrap");
            _createdObjects.Add(go);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            var mesh = CreateMesh(2);
            var uvs = mesh.uv;
            for (int i = 0; i < uvs.Length; i++)
                uvs[i] = new Vector2(uvs[i].x + 1.5f, uvs[i].y + 2.3f);
            mesh.uv = uvs;
            smr.sharedMesh = mesh;
            smr.sharedMaterials = new Material[] {
                CreateMat("A", Color.white, CreateTex(256, 256, Color.white)),
                CreateMat("B", Color.black, CreateTex(256, 256, Color.black)),
            };

            var result = AtlasGenerator.Generate(smr, Settings());
            Assert.IsTrue(result.Success, result.ErrorMessage);

            foreach (var uv in result.RemappedMesh.uv)
            {
                Assert.GreaterOrEqual(uv.x, 0f, "Wrapped UV.x >= 0");
                Assert.LessOrEqual(uv.x, 1f, "Wrapped UV.x <= 1");
                Assert.GreaterOrEqual(uv.y, 0f, "Wrapped UV.y >= 0");
                Assert.LessOrEqual(uv.y, 1f, "Wrapped UV.y <= 1");
            }
        }

        // ==========================================
        // メッシュ処理テスト
        // ==========================================

        [Test]
        public void Mesh_VertexCount_Preserved()
        {
            var smr = TrackSMR(CreateSMR(3, true));
            int orig = smr.sharedMesh.vertexCount;
            var result = AtlasGenerator.Generate(smr, Settings());
            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.AreEqual(orig, result.RemappedMesh.vertexCount);
        }

        [Test]
        public void Mesh_Normals_Preserved()
        {
            var smr = TrackSMR(CreateSMR(2, true));
            var origN = smr.sharedMesh.normals;
            var result = AtlasGenerator.Generate(smr, Settings());
            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.AreEqual(origN.Length, result.RemappedMesh.normals.Length);
            for (int i = 0; i < origN.Length; i++)
                Assert.AreEqual(origN[i], result.RemappedMesh.normals[i], $"Normal[{i}]");
        }

        [Test]
        public void Mesh_BoneWeights_Preserved()
        {
            var smr = TrackSMR(CreateSMR(2, true));
            int origCount = smr.sharedMesh.boneWeights.Length;
            var result = AtlasGenerator.Generate(smr, Settings());
            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.AreEqual(origCount, result.RemappedMesh.boneWeights.Length);
        }

        [Test]
        public void Mesh_TriangleCount_Preserved()
        {
            var smr = TrackSMR(CreateSMR(3, true));
            int origTris = 0;
            for (int s = 0; s < smr.sharedMesh.subMeshCount; s++)
                origTris += smr.sharedMesh.GetTriangles(s).Length;

            var result = AtlasGenerator.Generate(smr, Settings());
            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.AreEqual(origTris, result.RemappedMesh.GetTriangles(0).Length);
        }

        [Test]
        public void Mesh_SubMeshes_MergedToOne()
        {
            var smr = TrackSMR(CreateSMR(4, true));
            Assert.AreEqual(4, smr.sharedMesh.subMeshCount);

            var result = AtlasGenerator.Generate(smr, Settings());
            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.AreEqual(1, result.RemappedMesh.subMeshCount);
        }

        // ==========================================
        // アトラスレイアウトテスト
        // ==========================================

        [Test]
        public void Layout_AtlasSize_IsPowerOfTwo()
        {
            var smr = TrackSMR(CreateSMR(3, true));
            var result = AtlasGenerator.Generate(smr, Settings());
            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.IsTrue(Mathf.IsPowerOfTwo(result.AtlasSize), $"Size {result.AtlasSize} not power of 2");
        }

        [Test]
        public void Layout_MaxSize_Respected()
        {
            var smr = TrackSMR(CreateSMR(4, true, 2048));
            var result = AtlasGenerator.Generate(smr, Settings(maxSize: 2048));
            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.LessOrEqual(result.AtlasSize, 2048);
        }

        [Test]
        public void Layout_AllTiles_InBounds()
        {
            var smr = TrackSMR(CreateSMR(8, true, 128));
            var result = AtlasGenerator.Generate(smr, Settings());
            Assert.IsTrue(result.Success, result.ErrorMessage);

            foreach (var entry in result.Entries)
            {
                var r = entry.AtlasRect;
                Assert.GreaterOrEqual(r.x, 0f);
                Assert.GreaterOrEqual(r.y, 0f);
                Assert.LessOrEqual(r.x + r.width, 1.001f, "Right edge > 1.0");
                Assert.LessOrEqual(r.y + r.height, 1.001f, "Bottom edge > 1.0");
            }
        }

        // ==========================================
        // マテリアル出力テスト
        // ==========================================

        [Test]
        public void Output_SingleMaterial_Created()
        {
            var smr = TrackSMR(CreateSMR(4, true));
            var result = AtlasGenerator.Generate(smr, Settings());
            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.IsNotNull(result.AtlasMaterial);
        }

        [Test]
        public void Output_ShaderPreserved()
        {
            var smr = TrackSMR(CreateSMR(2, true));
            string origShader = smr.sharedMaterials[0].shader.name;
            var result = AtlasGenerator.Generate(smr, Settings());
            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.AreEqual(origShader, result.AtlasMaterial.shader.name);
        }

        [Test]
        public void Output_MainTexture_Set()
        {
            var smr = TrackSMR(CreateSMR(2, true));
            var result = AtlasGenerator.Generate(smr, Settings());
            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.IsNotNull(result.AtlasMaterial.GetTexture("_MainTex"), "MainTex should be set");
        }

        // ==========================================
        // エッジケーステスト
        // ==========================================

        [Test]
        public void Edge_NullMaterialInArray_Handled()
        {
            var go = new GameObject("NullInArray");
            _createdObjects.Add(go);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = CreateMesh(3);
            smr.sharedMaterials = new Material[] {
                CreateMat("A", Color.white, CreateTex(128,128,Color.white)),
                null,
                CreateMat("C", Color.black, CreateTex(128,128,Color.black)),
            };

            var result = AtlasGenerator.Generate(smr, Settings());
            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.AreEqual(2, result.Entries.Count, "Null material should be skipped");
        }

        [Test]
        public void Edge_DuplicateMaterials_Handled()
        {
            var go = new GameObject("Dup");
            _createdObjects.Add(go);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = CreateMesh(3);
            var mat = CreateMat("Shared", Color.white, CreateTex(128,128,Color.white));
            smr.sharedMaterials = new Material[] { mat, mat, mat };

            var result = AtlasGenerator.Generate(smr, Settings());
            Assert.IsTrue(result.Success, result.ErrorMessage);
        }

        [Test]
        public void Edge_LargeMaterialCount_32()
        {
            var smr = TrackSMR(CreateSMR(32, true, 32));
            var result = AtlasGenerator.Generate(smr, Settings());
            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.AreEqual(32, result.OriginalMaterialCount);
        }

        [Test]
        public void Edge_TinyTextures_1x1()
        {
            var smr = TrackSMR(CreateSMR(4, true, 1));
            var result = AtlasGenerator.Generate(smr, Settings());
            Assert.IsTrue(result.Success, result.ErrorMessage);
        }

        [Test]
        public void Edge_NonSquareTextures()
        {
            var go = new GameObject("NonSquare");
            _createdObjects.Add(go);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = CreateMesh(2);
            smr.sharedMaterials = new Material[] {
                CreateMat("Wide", Color.white, CreateTex(512, 128, Color.white)),
                CreateMat("Tall", Color.white, CreateTex(128, 512, Color.black)),
            };

            var result = AtlasGenerator.Generate(smr, Settings());
            Assert.IsTrue(result.Success, result.ErrorMessage);
        }

        [Test]
        public void Edge_ZeroPadding()
        {
            var smr = TrackSMR(CreateSMR(4, true));
            var result = AtlasGenerator.Generate(smr, Settings(padding: 0));
            Assert.IsTrue(result.Success, result.ErrorMessage);
        }

        // ==========================================
        // 設定バリエーションテスト
        // ==========================================

        [Test]
        public void Settings_Size1024()
        {
            var smr = TrackSMR(CreateSMR(4, true, 128));
            var result = AtlasGenerator.Generate(smr, Settings(maxSize: 1024));
            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.LessOrEqual(result.AtlasSize, 1024);
        }

        [Test]
        public void Settings_NormalMapOff_ExcludesNormals()
        {
            var go = new GameObject("NormOff");
            _createdObjects.Add(go);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = CreateMesh(2);
            var mat1 = CreateMat("M1", Color.white, CreateTex(256, 256, Color.white));
            mat1.SetTexture("_BumpMap", CreateTex(256, 256, new Color(0.5f,0.5f,1f)));
            smr.sharedMaterials = new Material[] { mat1, CreateMat("M2", Color.black) };

            var s = Settings();
            s.IncludeNormalMap = false;
            var result = AtlasGenerator.Generate(smr, s);
            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.IsNull(result.AtlasBumpMap, "Normal should be null when disabled");
        }

        [Test]
        public void Settings_EmissionOff_ExcludesEmission()
        {
            var go = new GameObject("EmOff");
            _createdObjects.Add(go);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = CreateMesh(2);
            var mat1 = CreateMat("M1", Color.white, CreateTex(256, 256, Color.white));
            mat1.SetTexture("_EmissionMap", CreateTex(256, 256, Color.red));
            smr.sharedMaterials = new Material[] { mat1, CreateMat("M2", Color.black) };

            var s = Settings();
            s.IncludeEmissionMap = false;
            var result = AtlasGenerator.Generate(smr, s);
            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.IsNull(result.AtlasEmissionMap, "Emission should be null when disabled");
        }

        // ==========================================
        // ピクセル検証テスト（Unity実機でしかできないテスト）
        // ==========================================

        [Test]
        public void Pixel_SolidColor_CorrectlyPlaced()
        {
            var go = new GameObject("PixelCheck");
            _createdObjects.Add(go);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = CreateMesh(2);
            smr.sharedMaterials = new Material[] {
                CreateMat("Red", Color.red, CreateTex(64, 64, Color.red)),
                CreateMat("Blue", Color.blue, CreateTex(64, 64, Color.blue)),
            };

            var result = AtlasGenerator.Generate(smr, Settings(padding: 0));
            Assert.IsTrue(result.Success, result.ErrorMessage);

            // エントリ0の中心ピクセルが赤系であることを確認
            var r0 = result.Entries[0].AtlasRect;
            int cx0 = Mathf.RoundToInt((r0.x + r0.width * 0.5f) * result.AtlasSize);
            int cy0 = Mathf.RoundToInt((r0.y + r0.height * 0.5f) * result.AtlasSize);
            var p0 = result.AtlasMainTex.GetPixel(cx0, cy0);
            Assert.Greater(p0.r, 0.5f, "Entry 0 center should be reddish");

            // エントリ1の中心ピクセルが青系であることを確認
            var r1 = result.Entries[1].AtlasRect;
            int cx1 = Mathf.RoundToInt((r1.x + r1.width * 0.5f) * result.AtlasSize);
            int cy1 = Mathf.RoundToInt((r1.y + r1.height * 0.5f) * result.AtlasSize);
            var p1 = result.AtlasMainTex.GetPixel(cx1, cy1);
            Assert.Greater(p1.b, 0.5f, "Entry 1 center should be bluish");
        }

        [Test]
        public void Pixel_GradientTexture_InterpolationAccurate()
        {
            var go = new GameObject("GradCheck");
            _createdObjects.Add(go);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = CreateMesh(2);
            smr.sharedMaterials = new Material[] {
                CreateMat("Grad", Color.white, CreateGradientTex(128, 128)),
                CreateMat("Black", Color.black, CreateTex(128, 128, Color.black)),
            };

            var result = AtlasGenerator.Generate(smr, Settings());
            Assert.IsTrue(result.Success, result.ErrorMessage);

            // グラデーションテクスチャの中心付近を確認
            var r0 = result.Entries[0].AtlasRect;
            int cx = Mathf.RoundToInt((r0.x + r0.width * 0.5f) * result.AtlasSize);
            int cy = Mathf.RoundToInt((r0.y + r0.height * 0.5f) * result.AtlasSize);
            var p = result.AtlasMainTex.GetPixel(cx, cy);
            // 中心なのでR,Gが0.4~0.6付近のはず
            Assert.Greater(p.r, 0.2f, "Gradient center R should be ~0.5");
            Assert.Less(p.r, 0.8f, "Gradient center R should be ~0.5");
        }

        // ==========================================
        // Apply適用テスト（Unity実機でしかできないテスト）
        // ==========================================

        [Test]
        public void Apply_SMR_HasSingleMaterial()
        {
            var smr = TrackSMR(CreateSMR(4, true));
            Assert.AreEqual(4, smr.sharedMaterials.Length);

            var result = AtlasGenerator.Generate(smr, Settings());
            Assert.IsTrue(result.Success, result.ErrorMessage);

            // 適用
            smr.sharedMesh = result.RemappedMesh;
            smr.sharedMaterials = new Material[] { result.AtlasMaterial };

            Assert.AreEqual(1, smr.sharedMaterials.Length, "After apply, should have 1 material");
            Assert.AreEqual(1, smr.sharedMesh.subMeshCount, "After apply, should have 1 submesh");
        }
    }
}
