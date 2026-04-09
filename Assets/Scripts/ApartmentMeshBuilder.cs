using System;
using System.Collections.Generic;
using UnityEngine;

public class ApartmentMeshBuilder
{
    private ProceduralApartment config;
    private MeshFilter meshFilter;
    private Transform transform;

    // Submesh indices constants
    private const int ID_EXT = 0;
    private const int ID_LIV = 1;
    private const int ID_KIT = 2;
    private const int ID_BAT = 3;
    private const int ID_BED = 4;
    private const int ID_ENT = 5;

    // Tracks the bounding boxes of doors to prevent furniture blocking them
    public List<Bounds> DoorBlockers { get; private set; } = new List<Bounds>();

    public ApartmentMeshBuilder(ProceduralApartment config, MeshFilter mf, Transform t)
    {
        this.config = config;
        this.meshFilter = mf;
        this.transform = t;
    }

    public void BuildMesh(List<RectXZ> rooms, float xMin, float xMax, float zMin, float zMax, float doorX, Mesh _mesh)
    {
        DoorBlockers.Clear(); // Reset blockers

        float hx = config.width * 0.5f;
        float hz = config.length * 0.5f;

        // Corners
        Vector3 c0 = new Vector3(-hx, 0f, -hz);
        Vector3 c1 = new Vector3(hx, 0f, -hz);
        Vector3 c2 = new Vector3(hx, 0f, hz);
        Vector3 c3 = new Vector3(-hx, 0f, hz);
        Vector3 center = (c0 + c1 + c2 + c3) * 0.25f;

        var verts = new List<Vector3>(8192);
        var uvs = new List<Vector2>(8192);

        // We have 6 submeshes
        var subTris = new List<List<int>>(6);
        for (int i = 0; i < 6; i++) subTris.Add(new List<int>(4096));

        // --- Outer Wall Construction ---
        Vector3 n0 = EdgeOutwardNormal(c0, c1, center);
        Vector3 n1 = EdgeOutwardNormal(c1, c2, center);
        Vector3 n2 = EdgeOutwardNormal(c2, c3, center);
        Vector3 n3 = EdgeOutwardNormal(c3, c0, center);

        Vector3 o0 = OffsetCorner(c0, n3, n0, config.outerWallThickness);
        Vector3 o1 = OffsetCorner(c1, n0, n1, config.outerWallThickness);
        Vector3 o2 = OffsetCorner(c2, n1, n2, config.outerWallThickness);
        Vector3 o3 = OffsetCorner(c3, n2, n3, config.outerWallThickness);

        // 1. Draw Exterior Skin
        AddOuterSkin(verts, uvs, subTris, c0, c1, o0, o1, config.height, center);
        AddOuterSkin(verts, uvs, subTris, c1, c2, o1, o2, config.height, center);

        if (config.cutFrontDoor)
        {
            // Add Front Door Blocker
            Vector3 dir = (c3 - c2).normalized;
            float len = Vector3.Distance(c2, c3);
            float ct = (len * 0.5f) + config.doorOffset;
            Vector3 doorCenter = c2 + dir * ct;
            // 2.0f depth creates a 1-meter walking clearance zone on both sides of the door
            Vector3 size = new Vector3(config.doorWidth + 0.4f, config.height, 2.0f);
            DoorBlockers.Add(new Bounds(doorCenter, size));
            // -----------------------------------

            AddOuterSkinWithDoor(verts, uvs, subTris, c2, c3, o2, o3, config.height, center, config.doorWidth, config.doorHeight, config.doorOffset);
        }
        else
        {
            AddOuterSkin(verts, uvs, subTris, c2, c3, o2, o3, config.height, center);
        }

        AddOuterSkin(verts, uvs, subTris, c3, c0, o3, o0, config.height, center);

        // 2. Draw Interior Faces of Outer Walls
        AddSegmentedInnerFace(verts, uvs, subTris, rooms, c0, c1, config.height, center);
        AddSegmentedInnerFace(verts, uvs, subTris, rooms, c1, c2, config.height, center);

        if (config.cutFrontDoor)
            AddSegmentedInnerFaceWithDoor(verts, uvs, subTris, rooms, c2, c3, config.height, center, config.doorWidth, config.doorHeight, config.doorOffset);
        else
            AddSegmentedInnerFace(verts, uvs, subTris, rooms, c2, c3, config.height, center);

        AddSegmentedInnerFace(verts, uvs, subTris, rooms, c3, c0, config.height, center);


        // --- Interior Grid (Floors + Partitions) ---
        if (config.generateRoomFloors || config.generateInteriorWalls)
            BuildInteriorFromGrid(verts, subTris, uvs, rooms, xMin, xMax, zMin, zMax, config.height);

        // Final Mesh Assembly
        _mesh.Clear();
        _mesh.SetVertices(verts);
        _mesh.subMeshCount = 6;
        for (int i = 0; i < 6; i++) _mesh.SetTriangles(subTris[i], i);
        _mesh.SetUVs(0, uvs);
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
        _mesh.RecalculateTangents();
        meshFilter.sharedMesh = _mesh;
    }

    void BuildInteriorFromGrid(List<Vector3> verts, List<List<int>> subTris, List<Vector2> uvs,
                               List<RectXZ> rooms, float xMin, float xMax, float zMin, float zMax, float wallHeight)
    {
        var xs = new List<float> { xMin, xMax };
        var zs = new List<float> { zMin, zMax };
        foreach (var r in rooms) { xs.Add(r.x0); xs.Add(r.x1); zs.Add(r.z0); zs.Add(r.z1); }
        UniqueSort(xs); UniqueSort(zs);

        int nx = xs.Count - 1; int nz = zs.Count - 1;
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

        if (config.generateRoomFloors) GenerateFloorObjects(cell, xs, zs);
        else ClearFloorObjects();

        if (!config.generateInteriorWalls) return;

        float t = Mathf.Max(0.01f, config.interiorWallThickness);
        HashSet<string> roomsWithDoors = new HashSet<string>();

        // 1. Vertical Boundaries 
        for (int i = 1; i < nx; i++)
        {
            int j = 0;
            while (j < nz)
            {
                string a = cell[i - 1, j]; string b = cell[i, j];
                if (!ShouldWallBetween(a, b)) { j++; continue; }

                int end = j;
                while (end + 1 < nz)
                {
                    if (!ShouldWallBetween(cell[i - 1, end + 1], cell[i, end + 1])) break;
                    if (!SamePair(a, b, cell[i - 1, end + 1], cell[i, end + 1])) break;
                    end++;
                }

                Vector3 pStart = new Vector3(xs[i], 0f, zs[j]);
                Vector3 pEnd = new Vector3(xs[i], 0f, zs[end + 1]);

                int matA = GetRoomMatIndex(a); int matB = GetRoomMatIndex(b);
                ProcessWallSegment(verts, subTris, uvs, pStart, pEnd, wallHeight, t, matB, matA, a, b, roomsWithDoors);
                j = end + 1;
            }
        }

        // 2. Horizontal Boundaries 
        for (int j = 1; j < nz; j++)
        {
            int i = 0;
            while (i < nx)
            {
                string a = cell[i, j - 1]; string b = cell[i, j];
                if (!ShouldWallBetween(a, b)) { i++; continue; }

                int end = i;
                while (end + 1 < nx)
                {
                    if (!ShouldWallBetween(cell[end + 1, j - 1], cell[end + 1, j])) break;
                    if (!SamePair(a, b, cell[end + 1, j - 1], cell[end + 1, j])) break;
                    end++;
                }

                Vector3 pStart = new Vector3(xs[i], 0f, zs[j]);
                Vector3 pEnd = new Vector3(xs[end + 1], 0f, zs[j]);

                int matA = GetRoomMatIndex(a); int matB = GetRoomMatIndex(b);
                ProcessWallSegment(verts, subTris, uvs, pStart, pEnd, wallHeight, t, matA, matB, a, b, roomsWithDoors);
                i = end + 1;
            }
        }
    }

    void ProcessWallSegment(List<Vector3> verts, List<List<int>> subTris, List<Vector2> uvs,
        Vector3 pStart, Vector3 pEnd, float h, float t, int matSideA, int matSideB, string roomA, string roomB, HashSet<string> roomsWithDoors)
    {
        float len = Vector3.Distance(pStart, pEnd);
        string enclosed = GetEnclosedRoom(roomA, roomB);
        bool canFit = len > config.doorWidth + 0.2f;

        if (enclosed != null && !roomsWithDoors.Contains(enclosed) && canFit)
        {
            AddInteriorWallWithDoor(verts, subTris, uvs, pStart, pEnd, h, t, config.doorWidth, config.doorHeight, matSideA, matSideB);
            roomsWithDoors.Add(enclosed);

            // Add Interior Door Blocker
            Vector3 dir = (pEnd - pStart).normalized;
            Vector3 doorCenter = pStart + dir * (len * 0.5f);
            float clearanceDepth = 2.0f; // Walking clearance through the door
            float doorClearanceWidth = config.doorWidth + 0.4f;

            // Construct bounds aligned to the wall
            Vector3 size = new Vector3(
                Mathf.Abs(dir.x) * doorClearanceWidth + Mathf.Abs(dir.z) * clearanceDepth,
                h,
                Mathf.Abs(dir.z) * doorClearanceWidth + Mathf.Abs(dir.x) * clearanceDepth
            );
            DoorBlockers.Add(new Bounds(doorCenter, size));
            // --------------------------------------
        }
        else
        {
            AddInteriorWall(verts, subTris, uvs, pStart, pEnd, 0f, h, t, matSideA, matSideB);
        }
    }

    // --- Outer Skins ---
    void AddOuterSkin(List<Vector3> verts, List<Vector2> uvs, List<List<int>> subTris,
                      Vector3 cInner1, Vector3 cInner2, Vector3 cOuter1, Vector3 cOuter2, float h, Vector3 center)
    {
        Vector3 mid = (cInner1 + cInner2) * 0.5f;
        Vector3 inward = (center - mid); inward.y = 0f; inward.Normalize();
        Vector3 outward = -inward;

        Vector3 o1b = cOuter1; Vector3 o2b = cOuter2;
        Vector3 o1t = cOuter1 + Vector3.up * h; Vector3 o2t = cOuter2 + Vector3.up * h;
        Vector3 i1t = cInner1 + Vector3.up * h; Vector3 i2t = cInner2 + Vector3.up * h;

        AddQuad(verts, subTris[ID_EXT], uvs, o2b, o1b, o1t, o2t, outward);
        AddQuad(verts, subTris[ID_EXT], uvs, i1t, i2t, o2t, o1t, Vector3.up);
    }

    void AddOuterSkinWithDoor(List<Vector3> verts, List<Vector2> uvs, List<List<int>> subTris,
        Vector3 cInner1, Vector3 cInner2, Vector3 cOuter1, Vector3 cOuter2, float h, Vector3 center, float dw, float dh, float offset)
    {
        float dTop = Mathf.Clamp(dh, 0.01f, h - 0.001f);
        Vector3 dir = (cInner2 - cInner1); float len = dir.magnitude; dir.Normalize();

        float half = Mathf.Max(0.01f, dw * 0.5f);
        float ct = (len * 0.5f) + offset;
        float t1 = Mathf.Clamp(ct - half, 0f, len);
        float t2 = Mathf.Clamp(ct + half, 0f, len);

        Vector3 iL = cInner1 + dir * t1; Vector3 iR = cInner1 + dir * t2;
        Vector3 outward = EdgeOutwardNormal(cInner1, cInner2, center);
        Vector3 oL = iL + outward * config.outerWallThickness;
        Vector3 oR = iR + outward * config.outerWallThickness;

        if (t1 > 0.01f) AddOuterSkin(verts, uvs, subTris, cInner1, iL, cOuter1, oL, h, center);
        if (t2 < len - 0.01f) AddOuterSkin(verts, uvs, subTris, iR, cInner2, oR, cOuter2, h, center);

        Vector3 oLb = oL + Vector3.up * dTop; Vector3 oRb = oR + Vector3.up * dTop;
        Vector3 oLt = oL + Vector3.up * h; Vector3 oRt = oR + Vector3.up * h;
        Vector3 iLt = iL + Vector3.up * h; Vector3 iRt = iR + Vector3.up * h;
        Vector3 iLb_h = iL + Vector3.up * dTop; Vector3 iRb_h = iR + Vector3.up * dTop;

        AddQuad(verts, subTris[ID_EXT], uvs, oRb, oLb, oLt, oRt, outward);
        AddQuad(verts, subTris[ID_EXT], uvs, iLt, iRt, oRt, oLt, Vector3.up);
        AddQuad(verts, subTris[ID_EXT], uvs, oLb, oRb, iRb_h, iLb_h, Vector3.down);

        Vector3 jambL_o_b = oL; Vector3 jambL_o_t = oLb;
        Vector3 jambL_i_b = iL; Vector3 jambL_i_t = iLb_h;
        AddQuad(verts, subTris[ID_EXT], uvs, jambL_i_b, jambL_o_b, jambL_o_t, jambL_i_t, dir);

        Vector3 jambR_o_b = oR; Vector3 jambR_o_t = oRb;
        Vector3 jambR_i_b = iR; Vector3 jambR_i_t = iRb_h;
        AddQuad(verts, subTris[ID_EXT], uvs, jambR_o_b, jambR_i_b, jambR_i_t, jambR_o_t, -dir);
    }

    void AddSegmentedInnerFace(List<Vector3> verts, List<Vector2> uvs, List<List<int>> subTris,
        List<RectXZ> rooms, Vector3 p1, Vector3 p2, float h, Vector3 center)
    {
        Vector3 dir = (p2 - p1); float len = dir.magnitude; dir.Normalize();
        Vector3 inward = (center - (p1 + p2) * 0.5f); inward.y = 0; inward.Normalize();

        float step = 0.5f;
        for (float d = 0; d < len; d += step)
        {
            float dNext = Mathf.Min(d + step, len);
            Vector3 s1 = p1 + dir * d;
            Vector3 s2 = p1 + dir * dNext;

            Vector3 checkPos = (s1 + s2) * 0.5f + inward * 0.1f;
            string room = LabelAt(rooms, checkPos.x, checkPos.z);
            int matId = GetRoomMatIndex(room);

            AddQuad(verts, subTris[matId], uvs, s1, s2, s2 + Vector3.up * h, s1 + Vector3.up * h, inward);
        }
    }

    void AddSegmentedInnerFaceWithDoor(List<Vector3> verts, List<Vector2> uvs, List<List<int>> subTris,
       List<RectXZ> rooms, Vector3 p1, Vector3 p2, float h, Vector3 center, float dw, float dh, float offset)
    {
        Vector3 dir = (p2 - p1); float len = dir.magnitude; dir.Normalize();
        Vector3 inward = (center - (p1 + p2) * 0.5f); inward.y = 0; inward.Normalize();

        float mid = len * 0.5f + offset;
        float holeStart = mid - dw * 0.5f;
        float holeEnd = mid + dw * 0.5f;
        float topH = Mathf.Clamp(dh, 0f, h);

        float step = 0.25f;
        for (float d = 0; d < len; d += step)
        {
            float dNext = Mathf.Min(d + step, len);
            float segMid = (d + dNext) * 0.5f;
            bool inHole = (segMid > holeStart && segMid < holeEnd);

            Vector3 s1 = p1 + dir * d;
            Vector3 s2 = p1 + dir * dNext;
            Vector3 checkPos = (s1 + s2) * 0.5f + inward * 0.1f;
            string room = LabelAt(rooms, checkPos.x, checkPos.z);
            int matId = GetRoomMatIndex(room);

            if (inHole)
            {
                if (topH < h) AddQuad(verts, subTris[matId], uvs, s1 + Vector3.up * topH, s2 + Vector3.up * topH, s2 + Vector3.up * h, s1 + Vector3.up * h, inward);
            }
            else
            {
                AddQuad(verts, subTris[matId], uvs, s1, s2, s2 + Vector3.up * h, s1 + Vector3.up * h, inward);
            }
        }
    }

    void AddInteriorWall(List<Vector3> verts, List<List<int>> subTris, List<Vector2> uvs,
                         Vector3 a, Vector3 b, float bottomY, float topY, float thickness, int matA, int matB)
    {
        Vector3 dir = (b - a); dir.y = 0f; dir.Normalize();
        Vector3 perp = Vector3.Cross(Vector3.up, dir).normalized;
        Vector3 half = perp * (thickness * 0.5f);
        Vector3 upB = Vector3.up * bottomY; Vector3 upT = Vector3.up * topY;

        AddQuad(verts, subTris[matA], uvs, a + half + upB, b + half + upB, b + half + upT, a + half + upT, perp);
        AddQuad(verts, subTris[matB], uvs, b - half + upB, a - half + upB, a - half + upT, b - half + upT, -perp);

        AddQuad(verts, subTris[ID_LIV], uvs, a - half + upT, b - half + upT, b + half + upT, a + half + upT, Vector3.up);
        AddQuad(verts, subTris[ID_LIV], uvs, a - half + upB, a + half + upB, a + half + upT, a - half + upT, -dir);
        AddQuad(verts, subTris[ID_LIV], uvs, b + half + upB, b - half + upB, b - half + upT, b + half + upT, dir);
        if (bottomY > 0.01f) AddQuad(verts, subTris[ID_LIV], uvs, a + half + upB, a - half + upB, b - half + upB, b + half + upB, Vector3.down);
    }

    void AddInteriorWallWithDoor(List<Vector3> verts, List<List<int>> subTris, List<Vector2> uvs,
                                 Vector3 a, Vector3 b, float h, float t, float dw, float dh, int matA, int matB)
    {
        Vector3 dir = (b - a); float len = dir.magnitude; dir.Normalize();
        float mid = len * 0.5f; float half = dw * 0.5f;
        Vector3 pL = a + dir * (mid - half); Vector3 pR = a + dir * (mid + half);

        AddInteriorWall(verts, subTris, uvs, a, pL, 0f, h, t, matA, matB);
        AddInteriorWall(verts, subTris, uvs, pR, b, 0f, h, t, matA, matB);
        if (dh < h) AddInteriorWall(verts, subTris, uvs, pL, pR, dh, h, t, matA, matB);
    }

    int GetRoomMatIndex(string room)
    {
        if (string.Equals(room, "Kitchen", StringComparison.OrdinalIgnoreCase)) return ID_KIT;
        if (string.Equals(room, "Bathroom", StringComparison.OrdinalIgnoreCase)) return ID_BAT;
        if (string.Equals(room, "Bedroom", StringComparison.OrdinalIgnoreCase)) return ID_BED;
        if (string.Equals(room, "Entry", StringComparison.OrdinalIgnoreCase)) return ID_ENT;
        return ID_LIV;
    }

    void AddQuad(List<Vector3> verts, List<int> tris, List<Vector2> uvs, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 n)
    {
        int start = verts.Count;
        verts.Add(v0); verts.Add(v1); verts.Add(v2); verts.Add(v3);
        float u = (v1 - v0).magnitude; float v = (v3 - v0).magnitude;
        uvs.Add(Vector2.zero); uvs.Add(new Vector2(u, 0)); uvs.Add(new Vector2(u, v)); uvs.Add(new Vector2(0, v));

        bool flip = Vector3.Dot(Vector3.Cross(v1 - v0, v2 - v0), n) < 0f;
        if (!flip) { tris.Add(start); tris.Add(start + 1); tris.Add(start + 2); tris.Add(start); tris.Add(start + 2); tris.Add(start + 3); }
        else { tris.Add(start); tris.Add(start + 2); tris.Add(start + 1); tris.Add(start); tris.Add(start + 3); tris.Add(start + 2); }
    }

    Vector3 EdgeOutwardNormal(Vector3 a, Vector3 b, Vector3 center)
    {
        Vector3 d = b - a; d.y = 0; d.Normalize();
        Vector3 outw = Vector3.Cross(Vector3.up, d);
        if (Vector3.Dot(outw, (a + b) * 0.5f - center) < 0) outw = -outw;
        return outw;
    }
    Vector3 OffsetCorner(Vector3 c, Vector3 prev, Vector3 next, float t)
    {
        float d = Mathf.Clamp(Vector3.Dot(prev, next), -0.99f, 0.99f);
        return c + (prev + next) * (t / (1f + d));
    }

    bool SamePair(string a, string b, string c, string d) { return (a == c && b == d) || (a == d && b == c); }
    bool ShouldWallBetween(string a, string b)
    {
        if (a == b) return false;
        if (config.entryOpenToLiving && ((a == "Entry" && b == "Living") || (a == "Living" && b == "Entry"))) return false;
        return true;
    }
    string LabelAt(List<RectXZ> rooms, float x, float z)
    {
        foreach (var r in rooms) if (r.Contains(x, z)) return r.label;
        return "Living";
    }
    string GetEnclosedRoom(string a, string b)
    {
        if ((a == "Living" || a == "Entry") && (b != "Living" && b != "Entry")) return b;
        if ((b == "Living" || b == "Entry") && (a != "Living" && a != "Entry")) return a;
        return null;
    }
    void UniqueSort(List<float> v) { v.Sort(); int w = 0; for (int i = 0; i < v.Count; i++) if (w == 0 || Mathf.Abs(v[i] - v[w - 1]) > 1e-4f) v[w++] = v[i]; v.RemoveRange(w, v.Count - w); }

    void GenerateFloorObjects(string[,] cell, List<float> xs, List<float> zs)
    {
        var builders = new Dictionary<string, FloorBuild>();
        for (int i = 0; i < cell.GetLength(0); i++)
        {
            for (int j = 0; j < cell.GetLength(1); j++)
            {
                string label = cell[i, j];
                if (!builders.ContainsKey(label)) builders[label] = new FloorBuild();
                Vector3 v0 = new Vector3(xs[i], 0, zs[j]); Vector3 v1 = new Vector3(xs[i + 1], 0, zs[j]);
                Vector3 v2 = new Vector3(xs[i + 1], 0, zs[j + 1]); Vector3 v3 = new Vector3(xs[i], 0, zs[j + 1]);
                AddQuad(builders[label].verts, builders[label].tris, builders[label].uvs, v0, v1, v2, v3, Vector3.up);
                if (config.doubleSidedFloors) AddQuad(builders[label].verts, builders[label].tris, builders[label].uvs, v0, v1, v2, v3, Vector3.down);
            }
        }

        Transform root = transform.Find("Floors");
        if (root == null) { var go = new GameObject("Floors"); go.transform.SetParent(transform, false); root = go.transform; }

        var needed = new HashSet<string>(builders.Keys);
        List<GameObject> kill = new List<GameObject>();
        for (int i = 0; i < root.childCount; i++) if (!needed.Contains(root.GetChild(i).name)) kill.Add(root.GetChild(i).gameObject);
        foreach (var k in kill) if (Application.isPlaying) UnityEngine.Object.Destroy(k); else UnityEngine.Object.DestroyImmediate(k);

        foreach (var kv in builders)
        {
            Transform t = root.Find(kv.Key);
            if (t == null) { var go = new GameObject(kv.Key); go.transform.SetParent(root, false); t = go.transform; go.AddComponent<MeshFilter>(); go.AddComponent<MeshRenderer>(); }

            Material m = config.livingFloorMat;
            if (kv.Key == "Kitchen") m = config.kitchenFloorMat;
            else if (kv.Key == "Bathroom") m = config.bathroomFloorMat;
            else if (kv.Key == "Bedroom") m = config.bedroomFloorMat; else if (kv.Key == "Entry") m = config.entryFloorMat;
            t.GetComponent<MeshRenderer>().sharedMaterial = m;

            Mesh mesh = t.GetComponent<MeshFilter>().sharedMesh;
            if (mesh == null) mesh = new Mesh { name = "Floor_" + kv.Key };
            else mesh.Clear();
            mesh.SetVertices(kv.Value.verts); mesh.SetTriangles(kv.Value.tris, 0); mesh.SetUVs(0, kv.Value.uvs);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); t.GetComponent<MeshFilter>().sharedMesh = mesh;
        }
    }
    void ClearFloorObjects() { var t = transform.Find("Floors"); if (t) if (Application.isPlaying) UnityEngine.Object.Destroy(t.gameObject); else UnityEngine.Object.DestroyImmediate(t.gameObject); }
}