using System;
using System.Collections.Generic;
using UnityEngine;

public class ApartmentMeshBuilder
{
    private ProceduralApartment config;
    private MeshFilter meshFilter;
    private Transform transform;

    public ApartmentMeshBuilder(ProceduralApartment config, MeshFilter mf, Transform t)
    {
        this.config = config;
        this.meshFilter = mf;
        this.transform = t;
    }

    public void BuildMesh(List<RectXZ> rooms, float xMin, float xMax, float zMin, float zMax, float doorX, Mesh _mesh)
    {
        if (_mesh == null) _mesh = new Mesh { name = "ProceduralApartmentMesh" };
        else _mesh.Clear();

        float hx = config.width * 0.5f;
        float hz = config.length * 0.5f;

        // Inner corners (CCW)
        Vector3 c0 = new Vector3(-hx, 0f, -hz);
        Vector3 c1 = new Vector3(hx, 0f, -hz);
        Vector3 c2 = new Vector3(hx, 0f, hz);
        Vector3 c3 = new Vector3(-hx, 0f, hz);

        Vector3 center = (c0 + c1 + c2 + c3) * 0.25f;

        var verts = new List<Vector3>(4096);
        var tris = new List<int>(8192);
        var uvs = new List<Vector2>(4096);

        // --- Outer wall normals and corners ---
        Vector3 n0 = EdgeOutwardNormal(c0, c1, center);
        Vector3 n1 = EdgeOutwardNormal(c1, c2, center);
        Vector3 n2 = EdgeOutwardNormal(c2, c3, center);
        Vector3 n3 = EdgeOutwardNormal(c3, c0, center);

        Vector3 o0 = OffsetCorner(c0, n3, n0, config.outerWallThickness);
        Vector3 o1 = OffsetCorner(c1, n0, n1, config.outerWallThickness);
        Vector3 o2 = OffsetCorner(c2, n1, n2, config.outerWallThickness);
        Vector3 o3 = OffsetCorner(c3, n2, n3, config.outerWallThickness);

        // --- Outer walls ---
        AddThickWallSegment(verts, tris, uvs, c0, c1, o0, o1, config.height, center);
        AddThickWallSegment(verts, tris, uvs, c1, c2, o1, o2, config.height, center);

        // Forward wall (+Z) : c2 -> c3
        if (config.cutFrontDoor)
            AddThickWallSegmentWithDoor(verts, tris, uvs, c2, c3, o2, o3, config.height, center, config.doorWidth, config.doorHeight, config.doorOffset);
        else
            AddThickWallSegment(verts, tris, uvs, c2, c3, o2, o3, config.height, center);

        AddThickWallSegment(verts, tris, uvs, c3, c0, o3, o0, config.height, center);

        // --- Build floors + interior walls using grid ---
        if (config.generateRoomFloors || config.generateInteriorWalls)
            BuildInteriorFromGrid(verts, tris, uvs, rooms, xMin, xMax, zMin, zMax, config.height);

        // Apply main mesh
        _mesh.SetVertices(verts);
        _mesh.SetTriangles(tris, 0);
        _mesh.SetUVs(0, uvs);
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
        _mesh.RecalculateTangents();
        meshFilter.sharedMesh = _mesh;
    }

    void BuildInteriorFromGrid(List<Vector3> shellVerts, List<int> shellTris, List<Vector2> shellUvs,
                               List<RectXZ> rooms, float xMin, float xMax, float zMin, float zMax, float wallHeight)
    {
        var xs = new List<float> { xMin, xMax };
        var zs = new List<float> { zMin, zMax };

        foreach (var r in rooms)
        {
            xs.Add(r.x0); xs.Add(r.x1);
            zs.Add(r.z0); zs.Add(r.z1);
        }

        UniqueSort(xs);
        UniqueSort(zs);

        int nx = xs.Count - 1;
        int nz = zs.Count - 1;
        string[,] cell = new string[nx, nz];

        for (int i = 0; i < nx; i++)
        {
            float cx = (xs[i] + xs[i + 1]) * 0.5f;
            for (int j = 0; j < nz; j++)
            {
                float cz = (zs[j] + zs[j + 1]) * 0.5f;
                cell[i, j] = LabelAt(rooms, cx, cz);
            }
        }

        // Floors
        if (config.generateRoomFloors)
        {
            var floorBuilders = new Dictionary<string, FloorBuild>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < nx; i++)
            {
                float x0 = xs[i], x1 = xs[i + 1];
                for (int j = 0; j < nz; j++)
                {
                    float z0 = zs[j], z1 = zs[j + 1];
                    string label = cell[i, j];

                    if (!floorBuilders.TryGetValue(label, out var fb))
                    {
                        fb = new FloorBuild();
                        floorBuilders[label] = fb;
                    }

                    Vector3 v0 = new Vector3(x0, 0f, z0);
                    Vector3 v1 = new Vector3(x1, 0f, z0);
                    Vector3 v2 = new Vector3(x1, 0f, z1);
                    Vector3 v3 = new Vector3(x0, 0f, z1);

                    AddQuad(fb.verts, fb.tris, fb.uvs, v0, v1, v2, v3, Vector3.up);
                    if (config.doubleSidedFloors)
                        AddQuad(fb.verts, fb.tris, fb.uvs, v0, v1, v2, v3, Vector3.down);
                }
            }
            RebuildFloorObjects(floorBuilders);
        }
        else
        {
            ClearFloorObjects();
        }

        if (!config.generateInteriorWalls) return;

        float t = Mathf.Max(0.01f, config.interiorWallThickness);
        HashSet<string> roomsWithDoors = new HashSet<string>();

        // 1. Vertical boundaries
        for (int i = 1; i < nx; i++)
        {
            int j = 0;
            while (j < nz)
            {
                string a = cell[i - 1, j];
                string b = cell[i, j];

                bool need = ShouldWallBetween(a, b);
                if (!need) { j++; continue; }

                int start = j;
                int end = j;
                while (end + 1 < nz)
                {
                    string a2 = cell[i - 1, end + 1];
                    string b2 = cell[i, end + 1];
                    if (!ShouldWallBetween(a2, b2)) break;
                    if (!SamePair(a, b, a2, b2)) break;
                    end++;
                }

                float x = xs[i];
                float z0 = zs[start];
                float z1 = zs[end + 1];
                Vector3 pStart = new Vector3(x, 0f, z0);
                Vector3 pEnd = new Vector3(x, 0f, z1);
                float wallLen = Vector3.Distance(pStart, pEnd);

                string enclosedRoom = GetEnclosedRoom(a, b);
                bool canFitDoor = wallLen > config.doorWidth + 0.2f;

                if (enclosedRoom != null && !roomsWithDoors.Contains(enclosedRoom) && canFitDoor)
                {
                    AddInteriorWallWithDoor(shellVerts, shellTris, shellUvs, pStart, pEnd, wallHeight, t, config.doorWidth, config.doorHeight);
                    roomsWithDoors.Add(enclosedRoom);
                }
                else
                {
                    AddInteriorWall(shellVerts, shellTris, shellUvs, pStart, pEnd, 0f, wallHeight, t);
                }
                j = end + 1;
            }
        }

        // 2. Horizontal boundaries
        for (int j = 1; j < nz; j++)
        {
            int i = 0;
            while (i < nx)
            {
                string a = cell[i, j - 1];
                string b = cell[i, j];

                bool need = ShouldWallBetween(a, b);
                if (!need) { i++; continue; }

                int start = i;
                int end = i;
                while (end + 1 < nx)
                {
                    string a2 = cell[end + 1, j - 1];
                    string b2 = cell[end + 1, j];
                    if (!ShouldWallBetween(a2, b2)) break;
                    if (!SamePair(a, b, a2, b2)) break;
                    end++;
                }

                float z = zs[j];
                float x0 = xs[start];
                float x1 = xs[end + 1];
                Vector3 pStart = new Vector3(x0, 0f, z);
                Vector3 pEnd = new Vector3(x1, 0f, z);
                float wallLen = Vector3.Distance(pStart, pEnd);

                string enclosedRoom = GetEnclosedRoom(a, b);
                bool canFitDoor = wallLen > config.doorWidth + 0.2f;

                if (enclosedRoom != null && !roomsWithDoors.Contains(enclosedRoom) && canFitDoor)
                {
                    AddInteriorWallWithDoor(shellVerts, shellTris, shellUvs, pStart, pEnd, wallHeight, t, config.doorWidth, config.doorHeight);
                    roomsWithDoors.Add(enclosedRoom);
                }
                else
                {
                    AddInteriorWall(shellVerts, shellTris, shellUvs, pStart, pEnd, 0f, wallHeight, t);
                }
                i = end + 1;
            }
        }
    }

    // --- Helpers used by Grid & Mesh ---

    bool SamePair(string a, string b, string c, string d)
    {
        return (a == c && b == d) || (a == d && b == c);
    }

    bool ShouldWallBetween(string a, string b)
    {
        if (a == b) return false;
        if (config.entryOpenToLiving)
        {
            if ((a == "Entry" && b == "Living") || (a == "Living" && b == "Entry"))
                return false;
        }
        return true;
    }

    string LabelAt(List<RectXZ> rooms, float x, float z)
    {
        for (int i = 0; i < rooms.Count; i++)
        {
            if (rooms[i].Contains(x, z)) return rooms[i].label;
        }
        return "Living";
    }

    void UniqueSort(List<float> vals)
    {
        vals.Sort();
        const float eps = 1e-4f;
        int w = 0;
        for (int r = 0; r < vals.Count; r++)
        {
            if (w == 0 || Mathf.Abs(vals[r] - vals[w - 1]) > eps)
                vals[w++] = vals[r];
        }
        if (w < vals.Count) vals.RemoveRange(w, vals.Count - w);
    }

    string GetEnclosedRoom(string a, string b)
    {
        bool aIsOpen = (a == "Living" || a == "Entry");
        bool bIsOpen = (b == "Living" || b == "Entry");
        if (aIsOpen && !bIsOpen) return b;
        if (!aIsOpen && bIsOpen) return a;
        return null;
    }

    // --- Floor Object Management ---
    void RebuildFloorObjects(Dictionary<string, FloorBuild> floors)
    {
        Transform root = GetOrCreateFloorsRoot();
        var needed = new HashSet<string>(floors.Keys, StringComparer.OrdinalIgnoreCase);

        foreach (var kv in floors)
        {
            string name = kv.Key;
            var fb = kv.Value;

            Transform child = root.Find(name);
            if (child == null)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root, false);
                child = go.transform;
                go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>();
            }

            var mr = child.GetComponent<MeshRenderer>();
            var parentMr = config.GetComponent<MeshRenderer>();
            if (mr.sharedMaterial == null && parentMr != null)
                mr.sharedMaterial = parentMr.sharedMaterial;

            var mf = child.GetComponent<MeshFilter>();
            Mesh m = mf.sharedMesh;
            if (m == null)
            {
                m = new Mesh { name = $"Floor_{name}" };
                mf.sharedMesh = m;
            }
            else m.Clear();

            m.SetVertices(fb.verts);
            m.SetTriangles(fb.tris, 0);
            m.SetUVs(0, fb.uvs);
            m.RecalculateNormals();
            m.RecalculateBounds();
            m.RecalculateTangents();
        }

        var toDelete = new List<GameObject>();
        for (int i = 0; i < root.childCount; i++)
        {
            var ch = root.GetChild(i);
            if (!needed.Contains(ch.name)) toDelete.Add(ch.gameObject);
        }

        foreach (var go in toDelete)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(go);
            else UnityEngine.Object.DestroyImmediate(go);
        }
    }

    void ClearFloorObjects()
    {
        Transform root = transform.Find("Floors");
        if (root == null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(root.gameObject);
        else UnityEngine.Object.DestroyImmediate(root.gameObject);
    }

    Transform GetOrCreateFloorsRoot()
    {
        Transform root = transform.Find("Floors");
        if (root != null) return root;
        var go = new GameObject("Floors");
        go.transform.SetParent(transform, false);
        return go.transform;
    }

    // --- Wall Builders ---
    void AddInteriorWallWithDoor(List<Vector3> verts, List<int> tris, List<Vector2> uvs,
                                 Vector3 a, Vector3 b, float wallHeight, float thickness, float dWidth, float dHeight)
    {
        Vector3 dir = (b - a);
        float totalLen = dir.magnitude;
        dir.Normalize();

        if (totalLen < dWidth + 0.2f)
        {
            AddInteriorWall(verts, tris, uvs, a, b, 0f, wallHeight, thickness);
            return;
        }

        float mid = totalLen * 0.5f;
        float halfDoor = dWidth * 0.5f;
        Vector3 pLeftJamb = a + dir * (mid - halfDoor);
        Vector3 pRightJamb = a + dir * (mid + halfDoor);

        AddInteriorWall(verts, tris, uvs, a, pLeftJamb, 0f, wallHeight, thickness);
        AddInteriorWall(verts, tris, uvs, pRightJamb, b, 0f, wallHeight, thickness);

        float headerStart = Mathf.Min(dHeight, wallHeight);
        if (headerStart < wallHeight - 0.01f)
        {
            AddInteriorWall(verts, tris, uvs, pLeftJamb, pRightJamb, headerStart, wallHeight, thickness);
        }
    }

    void AddInteriorWall(List<Vector3> verts, List<int> tris, List<Vector2> uvs,
                         Vector3 a, Vector3 b, float bottomY, float topY, float thickness)
    {
        Vector3 dir = (b - a);
        dir.y = 0f;
        float len = dir.magnitude;
        if (len < 0.0001f) return;
        dir /= len;

        Vector3 perp = Vector3.Cross(Vector3.up, dir).normalized;
        Vector3 half = perp * (thickness * 0.5f);

        Vector3 aPos = a + half; Vector3 aNeg = a - half;
        Vector3 bPos = b + half; Vector3 bNeg = b - half;

        Vector3 upBottom = Vector3.up * bottomY;
        Vector3 upTop = Vector3.up * topY;

        AddQuad(verts, tris, uvs, aPos + upBottom, bPos + upBottom, bPos + upTop, aPos + upTop, perp);
        AddQuad(verts, tris, uvs, bNeg + upBottom, aNeg + upBottom, aNeg + upTop, bNeg + upTop, -perp);
        AddQuad(verts, tris, uvs, aNeg + upTop, bNeg + upTop, bPos + upTop, aPos + upTop, Vector3.up);
        AddQuad(verts, tris, uvs, aNeg + upBottom, aPos + upBottom, aPos + upTop, aNeg + upTop, -dir);
        AddQuad(verts, tris, uvs, bPos + upBottom, bNeg + upBottom, bNeg + upTop, bPos + upTop, dir);

        if (bottomY > 0.01f)
        {
            AddQuad(verts, tris, uvs, aPos + upBottom, aNeg + upBottom, bNeg + upBottom, bPos + upBottom, Vector3.down);
        }
    }

    // --- Thick Walls ---
    void AddThickWallSegment(List<Vector3> verts, List<int> tris, List<Vector2> uvs,
                             Vector3 aInner, Vector3 bInner, Vector3 aOuter, Vector3 bOuter, float wallHeight, Vector3 roomCenter)
    {
        AddThickWallSegmentRange(verts, tris, uvs, aInner, bInner, aOuter, bOuter, 0f, wallHeight, roomCenter, true);
    }

    void AddThickWallSegmentRange(List<Vector3> verts, List<int> tris, List<Vector2> uvs,
                                  Vector3 aInner, Vector3 bInner, Vector3 aOuter, Vector3 bOuter,
                                  float baseY, float topY, Vector3 roomCenter, bool addBottomFace)
    {
        Vector3 aInnerB = new Vector3(aInner.x, baseY, aInner.z);
        Vector3 bInnerB = new Vector3(bInner.x, baseY, bInner.z);
        Vector3 aOuterB = new Vector3(aOuter.x, baseY, aOuter.z);
        Vector3 bOuterB = new Vector3(bOuter.x, baseY, bOuter.z);

        Vector3 aInnerT = new Vector3(aInner.x, topY, aInner.z);
        Vector3 bInnerT = new Vector3(bInner.x, topY, bInner.z);
        Vector3 aOuterT = new Vector3(aOuter.x, topY, aOuter.z);
        Vector3 bOuterT = new Vector3(bOuter.x, topY, bOuter.z);

        Vector3 mid = (aInner + bInner) * 0.5f;
        Vector3 inward = (roomCenter - mid); inward.y = 0f;
        inward = inward.sqrMagnitude > 0f ? inward.normalized : Vector3.forward;
        Vector3 outward = -inward;

        AddQuad(verts, tris, uvs, aInnerB, bInnerB, bInnerT, aInnerT, inward);
        AddQuad(verts, tris, uvs, bOuterB, aOuterB, aOuterT, bOuterT, outward);
        AddQuad(verts, tris, uvs, aInnerT, bInnerT, bOuterT, aOuterT, Vector3.up);

        if (addBottomFace)
            AddQuad(verts, tris, uvs, aOuterB, bOuterB, bInnerB, aInnerB, Vector3.down);
    }

    void AddThickWallSegmentWithDoor(List<Vector3> verts, List<int> tris, List<Vector2> uvs,
                                     Vector3 aInner, Vector3 bInner, Vector3 aOuter, Vector3 bOuter,
                                     float wallHeight, Vector3 roomCenter, float dWidth, float dHeight, float dOffset)
    {
        float doorTopY = Mathf.Clamp(dHeight, 0.01f, wallHeight - 0.001f);
        Vector3 wallDir = (bInner - aInner); wallDir.y = 0f;
        float segLen = wallDir.magnitude;
        if (segLen < 0.0001f)
        {
            AddThickWallSegment(verts, tris, uvs, aInner, bInner, aOuter, bOuter, wallHeight, roomCenter);
            return;
        }
        wallDir /= segLen;

        float halfDoor = Mathf.Max(0.01f, dWidth * 0.5f);
        float centerT = (segLen * 0.5f) + dOffset;
        centerT = Mathf.Clamp(centerT, halfDoor, segLen - halfDoor);
        float leftT = centerT - halfDoor;
        float rightT = centerT + halfDoor;

        Vector3 lInner = aInner + wallDir * leftT;
        Vector3 rInner = aInner + wallDir * rightT;

        Vector3 outward = EdgeOutwardNormal(aInner, bInner, roomCenter);
        Vector3 lOuter = lInner + outward * config.outerWallThickness;
        Vector3 rOuter = rInner + outward * config.outerWallThickness;

        if (leftT > 0.0001f)
            AddThickWallSegment(verts, tris, uvs, aInner, lInner, aOuter, lOuter, wallHeight, roomCenter);
        if (rightT < segLen - 0.0001f)
            AddThickWallSegment(verts, tris, uvs, rInner, bInner, rOuter, bOuter, wallHeight, roomCenter);

        AddThickWallSegmentRange(verts, tris, uvs, lInner, rInner, lOuter, rOuter, doorTopY, wallHeight, roomCenter, true);

        Vector3 lInnerTop = new Vector3(lInner.x, doorTopY, lInner.z);
        Vector3 lOuterTop = new Vector3(lOuter.x, doorTopY, lOuter.z);
        Vector3 lInnerB = new Vector3(lInner.x, 0f, lInner.z);
        Vector3 lOuterB = new Vector3(lOuter.x, 0f, lOuter.z);

        Vector3 rInnerTop = new Vector3(rInner.x, doorTopY, rInner.z);
        Vector3 rOuterTop = new Vector3(rOuter.x, doorTopY, rOuter.z);
        Vector3 rInnerB = new Vector3(rInner.x, 0f, rInner.z);
        Vector3 rOuterB = new Vector3(rOuter.x, 0f, rOuter.z);

        AddQuad(verts, tris, uvs, lInnerB, lOuterB, lOuterTop, lInnerTop, wallDir);
        AddQuad(verts, tris, uvs, rOuterB, rInnerB, rInnerTop, rOuterTop, -wallDir);
    }

    // --- Math Geometry ---
    Vector3 EdgeOutwardNormal(Vector3 a, Vector3 b, Vector3 center)
    {
        Vector3 dir = b - a; dir.y = 0f;
        dir = dir.sqrMagnitude > 0f ? dir.normalized : Vector3.right;
        Vector3 outward = Vector3.Cross(Vector3.up, dir).normalized;
        Vector3 mid = (a + b) * 0.5f;
        Vector3 toOutside = mid - center; toOutside.y = 0f;
        if (Vector3.Dot(outward, toOutside) < 0f) outward = -outward;
        return outward;
    }

    Vector3 OffsetCorner(Vector3 corner, Vector3 outwardPrev, Vector3 outwardNext, float thickness)
    {
        float d = Mathf.Clamp(Vector3.Dot(outwardPrev, outwardNext), -0.999f, 0.999f);
        float scale = thickness / (1f + d);
        return corner + (outwardPrev + outwardNext) * scale;
    }

    void AddQuad(List<Vector3> verts, List<int> tris, List<Vector2> uvs,
                 Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 desiredNormal)
    {
        int start = verts.Count;
        verts.Add(v0); verts.Add(v1); verts.Add(v2); verts.Add(v3);

        float uLen = (v1 - v0).magnitude;
        float vLen = (v3 - v0).magnitude;
        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(uLen, 0f));
        uvs.Add(new Vector2(uLen, vLen));
        uvs.Add(new Vector2(0f, vLen));

        Vector3 n = Vector3.Cross(v1 - v0, v2 - v0);
        bool flip = Vector3.Dot(n, desiredNormal) < 0f;

        if (!flip)
        {
            tris.Add(start + 0); tris.Add(start + 1); tris.Add(start + 2);
            tris.Add(start + 0); tris.Add(start + 2); tris.Add(start + 3);
        }
        else
        {
            tris.Add(start + 0); tris.Add(start + 2); tris.Add(start + 1);
            tris.Add(start + 0); tris.Add(start + 3); tris.Add(start + 2);
        }
    }
}