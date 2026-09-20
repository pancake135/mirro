using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mirro.Maze
{
    /// <summary>
    /// MazeData를 실제 월드 지오메트리로 변환한다.
    /// 200x200(4만 셀) 규모에서도 성능을 유지하기 위해 벽을 셀 단위 GameObject로
    /// 만들지 않고, 청크(chunkCellSpan x chunkCellSpan) 단위로 메쉬를 합쳐(CombineMeshes)
    /// 청크당 1개의 드로우콜만 발생시킨다.
    /// </summary>
    public class MazeBuilder : MonoBehaviour
    {
        [Header("Layout")]
        public float cellSize = 4f;
        public float wallHeight = 3f;
        public float wallThickness = 0.3f;
        public int chunkCellSpan = 20;

        [Header("Materials (비워두면 테마 적용 전 기본 초록/회색 머티리얼 사용)")]
        public Material wallMaterial;
        public Material floorMaterial;

        private Mesh _unitWallMesh;

        public void Build(MazeData maze)
        {
            _unitWallMesh = CreateUnitBoxMesh();
            EnsureFallbackMaterials();

            BuildFloor(maze);
            BuildWallChunks(maze);
        }

        private void EnsureFallbackMaterials()
        {
            if (wallMaterial == null)
                wallMaterial = CreateColoredMaterial(new Color(0.20f, 0.45f, 0.18f));
            if (floorMaterial == null)
                floorMaterial = CreateColoredMaterial(new Color(0.42f, 0.36f, 0.27f));
        }

        private static Material _materialTemplate;

        /// <summary>
        /// Resources/Materials/MazeLit(URP Lit 머티리얼 에셋)을 복제해 색만 바꾼다. 셰이더를 코드에서
        /// Shader.Find로 찾으면 빌드에서 셰이더가 제거되어 null이 되므로, 에셋 참조로 포함시킨다.
        /// </summary>
        public static Material CreateColoredMaterial(Color color)
        {
            if (_materialTemplate == null)
                _materialTemplate = Resources.Load<Material>("Materials/MazeLit");

            Material mat;
            if (_materialTemplate != null)
            {
                mat = new Material(_materialTemplate);
            }
            else
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                    throw new System.InvalidOperationException("Resources/Materials/MazeLit.mat is missing - run Mirro > Setup Project.");
                mat = new Material(shader);
            }

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            else mat.color = color;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.1f);
            return mat;
        }

        /// <summary>
        /// 면마다 정점을 공유하지 않는 24정점 큐브(각 면이 독립 노멀을 갖도록) —
        /// 셰이딩이 뭉개지지 않고 벽 모서리가 또렷하게 보이도록 한다.
        /// </summary>
        private Mesh CreateUnitBoxMesh()
        {
            var mesh = new Mesh { name = "UnitWallBox" };

            Vector3 p0 = new Vector3(-0.5f, -0.5f, 0.5f);
            Vector3 p1 = new Vector3(0.5f, -0.5f, 0.5f);
            Vector3 p2 = new Vector3(0.5f, -0.5f, -0.5f);
            Vector3 p3 = new Vector3(-0.5f, -0.5f, -0.5f);
            Vector3 p4 = new Vector3(-0.5f, 0.5f, 0.5f);
            Vector3 p5 = new Vector3(0.5f, 0.5f, 0.5f);
            Vector3 p6 = new Vector3(0.5f, 0.5f, -0.5f);
            Vector3 p7 = new Vector3(-0.5f, 0.5f, -0.5f);

            Vector3[] vertices =
            {
                p0, p1, p2, p3, // bottom
                p7, p4, p0, p3, // left
                p4, p5, p1, p0, // front
                p6, p7, p3, p2, // back
                p5, p6, p2, p1, // right
                p7, p6, p5, p4  // top
            };

            var triangles = new int[36];
            for (int face = 0; face < 6; face++)
            {
                int vOffset = face * 4;
                int tOffset = face * 6;
                triangles[tOffset + 0] = vOffset + 3;
                triangles[tOffset + 1] = vOffset + 1;
                triangles[tOffset + 2] = vOffset + 0;
                triangles[tOffset + 3] = vOffset + 3;
                triangles[tOffset + 4] = vOffset + 2;
                triangles[tOffset + 5] = vOffset + 1;
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void BuildFloor(MazeData maze)
        {
            float w = maze.Width * cellSize;
            float h = maze.Height * cellSize;

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.SetParent(transform, false);
            floor.transform.position = new Vector3(w * 0.5f, -0.1f, h * 0.5f);
            floor.transform.localScale = new Vector3(w, 0.2f, h);
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMaterial;
        }

        private void BuildWallChunks(MazeData maze)
        {
            int chunkSpan = Mathf.Max(1, chunkCellSpan);
            int chunksX = Mathf.CeilToInt(maze.Width / (float)chunkSpan);
            int chunksY = Mathf.CeilToInt(maze.Height / (float)chunkSpan);

            var wallsRoot = new GameObject("Walls").transform;
            wallsRoot.SetParent(transform, false);

            for (int cy = 0; cy < chunksY; cy++)
            {
                for (int cx = 0; cx < chunksX; cx++)
                {
                    BuildSingleChunk(maze, wallsRoot, cx, cy, chunkSpan);
                }
            }
        }

        private void BuildSingleChunk(MazeData maze, Transform parent, int chunkX, int chunkY, int chunkSpan)
        {
            var combineList = new List<CombineInstance>();

            int xStart = chunkX * chunkSpan;
            int yStart = chunkY * chunkSpan;
            int xEnd = Mathf.Min(xStart + chunkSpan, maze.Width);
            int yEnd = Mathf.Min(yStart + chunkSpan, maze.Height);

            for (int y = yStart; y < yEnd; y++)
            {
                for (int x = xStart; x < xEnd; x++)
                {
                    if (maze.HasWall(x, y, WallSide.East))
                        AddWallSegment(combineList, x, y, isNorthSouth: false, edgeIndex: x + 1);
                    if (maze.HasWall(x, y, WallSide.North))
                        AddWallSegment(combineList, x, y, isNorthSouth: true, edgeIndex: y + 1);
                    if (x == 0 && maze.HasWall(x, y, WallSide.West))
                        AddWallSegment(combineList, x, y, isNorthSouth: false, edgeIndex: 0);
                    if (y == 0 && maze.HasWall(x, y, WallSide.South))
                        AddWallSegment(combineList, x, y, isNorthSouth: true, edgeIndex: 0);
                }
            }

            if (combineList.Count == 0) return;

            var chunkMesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            chunkMesh.CombineMeshes(combineList.ToArray(), true, true);
            chunkMesh.RecalculateBounds();

            var chunkGo = new GameObject($"WallChunk_{chunkX}_{chunkY}");
            chunkGo.transform.SetParent(parent, false);
            chunkGo.AddComponent<MeshFilter>().sharedMesh = chunkMesh;
            chunkGo.AddComponent<MeshRenderer>().sharedMaterial = wallMaterial;
            chunkGo.AddComponent<MeshCollider>().sharedMesh = chunkMesh;
        }

        /// <summary>
        /// isNorthSouth=true : X축을 따라 뻗는 벽 (두 행 사이 경계, 예: North/South 벽)
        /// isNorthSouth=false: Z축을 따라 뻗는 벽 (두 열 사이 경계, 예: East/West 벽)
        /// edgeIndex는 셀 격자 기준 경계선의 인덱스 (예: East는 x+1, North는 y+1).
        /// </summary>
        private void AddWallSegment(List<CombineInstance> list, int x, int y, bool isNorthSouth, int edgeIndex)
        {
            Vector3 position;
            Quaternion rotation;

            if (isNorthSouth)
            {
                position = new Vector3((x + 0.5f) * cellSize, wallHeight * 0.5f, edgeIndex * cellSize);
                rotation = Quaternion.identity;
            }
            else
            {
                position = new Vector3(edgeIndex * cellSize, wallHeight * 0.5f, (y + 0.5f) * cellSize);
                rotation = Quaternion.Euler(0f, 90f, 0f);
            }

            var scale = new Vector3(cellSize, wallHeight, wallThickness);
            var matrix = Matrix4x4.TRS(position, rotation, scale);

            list.Add(new CombineInstance { mesh = _unitWallMesh, transform = matrix });
        }
    }
}
