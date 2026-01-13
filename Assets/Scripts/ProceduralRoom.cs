using System;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ProceduralApartment : MonoBehaviour
{
    [Header("Apartment Shell")]
    [Min(0.01f)] public float width = 6f;     // interior clear width
    [Min(0.01f)] public float length = 10f;   // interior clear length
    [Min(0.01f)] public float height = 3f;

    [Header("Outer Walls")]
    [Min(0.001f)] public float outerWallThickness = 0.2f;

    [Header("Front Door (Forward wall)")]
    public bool cutFrontDoor = true;
    [Min(0.1f)] public float doorWidth = 1.0f;
    [Min(0.1f)] public float doorHeight = 2.1f;

    [Tooltip("Door offset along the forward wall (c2 -> c3). Positive moves toward -X. (0 = centered)")]
    public float doorOffset = 0f;

    [Header("Interior Rooms")]
    public bool includeKitchen = true;
    public bool includeBathroom = true;
    public bool includeBedroom = true;

    [Header("Randomness")]
    public int seed = 12345;
    public bool randomizeSeedOnPlay = false;

    [Header("Entry/Clearance)")]
    [Min(0.5f)] public float entryMinWidth = 1.6f;
    [Min(0.5f)] public float entryMaxWidth = 2.8f;
    [Min(0.5f)] public float entryMinDepth = 1.4f;
    [Min(0.5f)] public float entryMaxDepth = 3.0f;

    [Header("Kitchen Size Constraints")]
    [Min(0.5f)] public float kitchenMinWidth = 1.8f;
    [Min(0.5f)] public float kitchenMaxWidth = 3.5f;
    [Min(0.5f)] public float kitchenMinDepth = 1.8f;
    [Min(0.5f)] public float kitchenMaxDepth = 3.2f;

    [Header("Bathroom Size Constraints")]
    [Min(0.5f)] public float bathMinWidth = 1.5f;
    [Min(0.5f)] public float bathMaxWidth = 2.6f;
    [Min(0.5f)] public float bathMinDepth = 1.6f;
    [Min(0.5f)] public float bathMaxDepth = 2.8f;

    [Header("Bedroom Size Constraints")]
    [Min(0.5f)] public float bedMinWidth = 2.6f;
    [Min(0.5f)] public float bedMaxWidth = 4.5f;
    [Min(0.5f)] public float bedMinDepth = 2.6f;
    [Min(0.5f)] public float bedMaxDepth = 5.0f;

    [Header("Living Safeguards")]
    [Tooltip("Keep at least this much 'living' depth between front strip and bedroom.")]
    [Min(0.5f)] public float minLivingDepth = 1.5f;

    [Tooltip("Keep at least this much free width for living (prevents a bedroom that consumes everything).")]
    [Min(0.5f)] public float minLivingWidth = 1.5f;

    [Header("Interior Partitions")]
    public bool generateInteriorWalls = true;
    [Min(0.01f)] public float interiorWallThickness = 0.12f;

    [Header("Floors (Separate per Room)")]
    public bool generateRoomFloors = true;
    public bool doubleSidedFloors = true;

    [Tooltip("If true, Entry is OPEN to Living (no partition wall between them).")]
    public bool entryOpenToLiving = true;

    [Tooltip("Generate automatically when values change in the editor.")]
    public bool regenerateInEditor = true;

    Mesh _mesh;

    // ------------------------- Types -------------------------
    struct RectXZ
    {
        public float x0, x1, z0, z1;
        public string label;

        public RectXZ(string label, float x0, float x1, float z0, float z1)
        {
            this.label = label;
            this.x0 = Mathf.Min(x0, x1);
            this.x1 = Mathf.Max(x0, x1);
            this.z0 = Mathf.Min(z0, z1);
            this.z1 = Mathf.Max(z0, z1);
        }

        public bool Contains(float x, float z)
        {
            return x >= x0 && x < x1 && z >= z0 && z < z1;
        }
    }

    class FloorBuild
    {
        public readonly List<Vector3> verts = new();
        public readonly List<int> tris = new();
        public readonly List<Vector2> uvs = new();
    }

    // ------------------------- Unity -------------------------
    void OnEnable() => Generate();

    void OnValidate()
    {
        if (regenerateInEditor) Generate();
    }

    // ------------------------- Main -------------------------
    public void Generate()
    {
        if (Application.isPlaying && randomizeSeedOnPlay)
            seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);

        var rng = new System.Random(seed);

        var mf = GetComponent<MeshFilter>();
        if (_mesh == null) _mesh = new Mesh { name = "ProceduralApartmentMesh" };
        else _mesh.Clear();

        float hx = width * 0.5f;
        float hz = length * 0.5f;

        // Inner corners (CCW), floor plane at y=0
        Vector3 c0 = new Vector3(-hx, 0f, -hz);
        Vector3 c1 = new Vector3(hx, 0f, -hz);
        Vector3 c2 = new Vector3(hx, 0f, hz);
        Vector3 c3 = new Vector3(-hx, 0f, hz);

        Vector3 center = (c0 + c1 + c2 + c3) * 0.25f;

        var verts = new List<Vector3>(4096);
        var tris = new List<int>(8192);
        var uvs = new List<Vector2>(4096);

        // --- Outer wall outward normals and shared miter corners ---
        Vector3 n0 = EdgeOutwardNormal(c0, c1, center);
        Vector3 n1 = EdgeOutwardNormal(c1, c2, center);
        Vector3 n2 = EdgeOutwardNormal(c2, c3, center);
        Vector3 n3 = EdgeOutwardNormal(c3, c0, center);

        Vector3 o0 = OffsetCorner(c0, n3, n0, outerWallThickness);
        Vector3 o1 = OffsetCorner(c1, n0, n1, outerWallThickness);
        Vector3 o2 = OffsetCorner(c2, n1, n2, outerWallThickness);
        Vector3 o3 = OffsetCorner(c3, n2, n3, outerWallThickness);

        // --- Outer walls (no roof) ---
        AddThickWallSegment(verts, tris, uvs, c0, c1, o0, o1, height, center);
        AddThickWallSegment(verts, tris, uvs, c1, c2, o1, o2, height, center);

        // Forward wall (+Z) : c2 -> c3 (door optional)
        if (cutFrontDoor)
            AddThickWallSegmentWithDoor(verts, tris, uvs, c2, c3, o2, o3, height, center, doorWidth, doorHeight, doorOffset);
        else
            AddThickWallSegment(verts, tris, uvs, c2, c3, o2, o3, height, center);

        AddThickWallSegment(verts, tris, uvs, c3, c0, o3, o0, height, center);

        // --- Interior layout (rectangles) ---
        float xMin = -hx, xMax = hx, zMin = -hz, zMax = hz;

        float doorX = Mathf.Clamp(-doorOffset, xMin + doorWidth * 0.5f, xMax - doorWidth * 0.5f);

        var rooms = GenerateRoomRectangles(rng, xMin, xMax, zMin, zMax, doorX);

        // --- Build floors + interior walls using a boundary grid ---
        if (generateRoomFloors || generateInteriorWalls)
            BuildInteriorFromGrid(verts, tris, uvs, rooms, xMin, xMax, zMin, zMax, height);

        // Apply main mesh (walls + partitions)
        _mesh.SetVertices(verts);
        _mesh.SetTriangles(tris, 0);
        _mesh.SetUVs(0, uvs);
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
        _mesh.RecalculateTangents();
        mf.sharedMesh = _mesh;
    }

    // ------------------------- Layout Generation -------------------------
    List<RectXZ> GenerateRoomRectangles(System.Random rng,
                                       float xMin, float xMax, float zMin, float zMax,
                                       float doorX)
    {
        float W = xMax - xMin;
        float L = zMax - zMin;

        // Max front-depth we can spend while still guaranteeing bedroomMinDepth + minLivingDepth (if bedroom enabled)
        float maxFrontDepth = L - (includeBedroom ? bedMinDepth : 0f) - minLivingDepth;
        maxFrontDepth = Mathf.Max(0.5f, maxFrontDepth);

        // Sample depths (clamped so front strip can't consume everything)
        float entryDepth = PickInRange(rng, entryMinDepth, entryMaxDepth, 0.5f, maxFrontDepth);
        float kitchenDepth = includeKitchen ? PickInRange(rng, kitchenMinDepth, kitchenMaxDepth, 0.5f, maxFrontDepth) : 0f;
        float bathDepth = includeBathroom ? PickInRange(rng, bathMinDepth, bathMaxDepth, 0.5f, maxFrontDepth) : 0f;

        float frontStripDepth = Mathf.Max(entryDepth, kitchenDepth, bathDepth);
        frontStripDepth = Mathf.Clamp(frontStripDepth, 0.5f, maxFrontDepth);

        // Ensure entry depth fits in front strip (entry is always at the front)
        entryDepth = Mathf.Min(entryDepth, frontStripDepth);

        // Width budgeting: keep at least entryMinWidth in the center if kitchen+bath exist
        float minCenter = Mathf.Clamp(entryMinWidth, 0.5f, W - 0.5f);

        float kW = 0f, bW = 0f;
        if (includeKitchen)
            kW = PickInRange(rng, kitchenMinWidth, kitchenMaxWidth, 0f, W);

        if (includeBathroom)
            bW = PickInRange(rng, bathMinWidth, bathMaxWidth, 0f, W);

        // If both exist, enforce kW + bW <= W - minCenter by shrinking (but not below mins if possible)
        if (includeKitchen && includeBathroom)
        {
            float maxSum = Mathf.Max(0.1f, W - minCenter);

            // clamp each to feasible max given the other's minimum
            float kMaxFeasible = Mathf.Min(kitchenMaxWidth, maxSum - bathMinWidth);
            float bMaxFeasible = Mathf.Min(bathMaxWidth, maxSum - kitchenMinWidth);

            kW = Mathf.Clamp(kW, kitchenMinWidth, Mathf.Max(kitchenMinWidth, kMaxFeasible));
            bW = Mathf.Clamp(bW, bathMinWidth, Mathf.Max(bathMinWidth, bMaxFeasible));

            float sum = kW + bW;
            if (sum > maxSum)
            {
                float over = sum - maxSum;
                // reduce larger one first
                if (kW >= bW)
                {
                    float newKW = Mathf.Max(kitchenMinWidth, kW - over);
                    over -= (kW - newKW);
                    kW = newKW;
                    if (over > 0f) bW = Mathf.Max(bathMinWidth, bW - over);
                }
                else
                {
                    float newBW = Mathf.Max(bathMinWidth, bW - over);
                    over -= (bW - newBW);
                    bW = newBW;
                    if (over > 0f) kW = Mathf.Max(kitchenMinWidth, kW - over);
                }
            }
        }

        // Decide which side gets kitchen/bath (try to keep door in the center/entry zone)
        // Two candidates if both exist: (K left, B right) or (B left, K right)
        bool kitchenLeft = true;
        bool bathLeft = false;

        if (includeKitchen && includeBathroom)
        {
            bool optionA_OK = DoorInCenterInterval(doorX, xMin + kW, xMax - bW, entryMinWidth);
            bool optionB_OK = DoorInCenterInterval(doorX, xMin + bW, xMax - kW, entryMinWidth);

            if (optionA_OK && optionB_OK)
            {
                if (rng.NextDouble() < 0.5) { kitchenLeft = true; bathLeft = false; }
                else { kitchenLeft = false; bathLeft = true; }
            }
            else if (optionA_OK)
            {
                kitchenLeft = true; bathLeft = false;
            }
            else if (optionB_OK)
            {
                kitchenLeft = false; bathLeft = true;
            }
            else
            {
                // Fallback: put the larger one opposite the door side to free the door
                // (still guarantees Entry contains door even if center is tight)
                if (doorX < 0f) { kitchenLeft = false; bathLeft = true; }
                else { kitchenLeft = true; bathLeft = false; }
            }
        }
        else if (includeKitchen)
        {
            // If door is far left, put kitchen right, otherwise random
            kitchenLeft = (Mathf.Abs(doorX - xMin) > Mathf.Abs(doorX - xMax)) ? true : false;
            if (rng.NextDouble() < 0.5) kitchenLeft = !kitchenLeft;
        }
        else if (includeBathroom)
        {
            bathLeft = (Mathf.Abs(doorX - xMin) > Mathf.Abs(doorX - xMax)) ? true : false;
            if (rng.NextDouble() < 0.5) bathLeft = !bathLeft;
        }

        // Compute center interval available for Entry
        float leftOccupied = 0f;
        float rightOccupied = 0f;

        if (includeKitchen)
        {
            if (kitchenLeft) leftOccupied += kW;
            else rightOccupied += kW;
        }
        if (includeBathroom)
        {
            if (bathLeft) leftOccupied += bW;
            else rightOccupied += bW;
        }

        float entryAvailX0 = xMin + leftOccupied;
        float entryAvailX1 = xMax - rightOccupied;

        float entryAvailW = Mathf.Max(0.01f, entryAvailX1 - entryAvailX0);

        float eW = PickInRange(rng, entryMinWidth, entryMaxWidth, 0.5f, entryAvailW);
        eW = Mathf.Min(eW, entryAvailW);

        // Place entry so it CONTAINS the doorX, clamped to free interval
        float entryX0 = Mathf.Clamp(doorX - eW * 0.5f, entryAvailX0, entryAvailX1 - eW);
        float entryX1 = entryX0 + eW;

        float entryZ1 = zMax;
        float entryZ0 = zMax - entryDepth;

        var rooms = new List<RectXZ>(8);

        // Entry / Clearance area
        rooms.Add(new RectXZ("Entry", entryX0, entryX1, entryZ0, entryZ1));

        // Kitchen (front strip, corner)
        if (includeKitchen)
        {
            float x0 = kitchenLeft ? xMin : (xMax - kW);
            float x1 = kitchenLeft ? (xMin + kW) : xMax;
            float z0 = zMax - Mathf.Min(kitchenDepth, frontStripDepth);
            float z1 = zMax;
            rooms.Add(new RectXZ("Kitchen", x0, x1, z0, z1));
        }

        // Bathroom (front strip, corner)
        if (includeBathroom)
        {
            float x0 = bathLeft ? xMin : (xMax - bW);
            float x1 = bathLeft ? (xMin + bW) : xMax;
            float z0 = zMax - Mathf.Min(bathDepth, frontStripDepth);
            float z1 = zMax;
            rooms.Add(new RectXZ("Bathroom", x0, x1, z0, z1));
        }

        // Bedroom (back strip)
        if (includeBedroom)
        {
            float maxBedroomDepth = L - frontStripDepth - minLivingDepth;
            maxBedroomDepth = Mathf.Max(0.5f, maxBedroomDepth);

            float bedDepth = PickInRange(rng, bedMinDepth, bedMaxDepth, 0.5f, maxBedroomDepth);
            bedDepth = Mathf.Min(bedDepth, maxBedroomDepth);

            float maxBedroomWidth = Mathf.Max(0.5f, W - minLivingWidth);
            float bedWidth = PickInRange(rng, bedMinWidth, bedMaxWidth, 0.5f, maxBedroomWidth);
            bedWidth = Mathf.Min(bedWidth, maxBedroomWidth);

            // Prefer bedroom on same side as bathroom if bathroom exists, else random
            bool bedLeft;
            if (includeBathroom) bedLeft = bathLeft;
            else bedLeft = rng.NextDouble() < 0.5;

            float bx0 = bedLeft ? xMin : (xMax - bedWidth);
            float bx1 = bedLeft ? (xMin + bedWidth) : xMax;

            float bz0 = zMin;
            float bz1 = zMin + bedDepth;

            rooms.Add(new RectXZ("Bedroom", bx0, bx1, bz0, bz1));
        }

        // Living is implicit (everything else not covered by above)
        return rooms;
    }

    bool DoorInCenterInterval(float doorX, float intervalX0, float intervalX1, float requiredMinWidth)
    {
        float w = intervalX1 - intervalX0;
        if (w < requiredMinWidth) return false;
        return doorX >= intervalX0 && doorX <= intervalX1;
    }

    float PickInRange(System.Random rng, float min, float max, float hardMin, float hardMax)
    {
        float a = Mathf.Max(min, hardMin);
        float b = Mathf.Max(a, Mathf.Min(max, hardMax));
        double t = rng.NextDouble();
        return Mathf.Lerp(a, b, (float)t);
    }

    // ------------------------- Grid Build (Floors + Walls) -------------------------
    void BuildInteriorFromGrid(List<Vector3> shellVerts, List<int> shellTris, List<Vector2> shellUvs,
                               List<RectXZ> rooms,
                               float xMin, float xMax, float zMin, float zMax,
                               float wallHeight)
    {
        // Gather boundary lines from room rectangles
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

        // Label each cell by checking its center point against explicit room rects
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

        // Floors: build one mesh per label
        if (generateRoomFloors)
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

                    // one quad per cell
                    Vector3 v0 = new Vector3(x0, 0f, z0);
                    Vector3 v1 = new Vector3(x1, 0f, z0);
                    Vector3 v2 = new Vector3(x1, 0f, z1);
                    Vector3 v3 = new Vector3(x0, 0f, z1);

                    AddQuad(fb.verts, fb.tris, fb.uvs, v0, v1, v2, v3, Vector3.up);
                    if (doubleSidedFloors)
                        AddQuad(fb.verts, fb.tris, fb.uvs, v0, v1, v2, v3, Vector3.down);
                }
            }

            RebuildFloorObjects(floorBuilders);
        }
        else
        {
            ClearFloorObjects();
        }

        // Interior walls: add walls along boundaries where labels differ (with exceptions)
        if (!generateInteriorWalls) return;

        float t = Mathf.Max(0.01f, interiorWallThickness);

        // Vertical boundaries (between i-1 and i) at x = xs[i]
        for (int i = 1; i < nx; i++)
        {
            int j = 0;
            while (j < nz)
            {
                string a = cell[i - 1, j];
                string b = cell[i, j];

                bool need = ShouldWallBetween(a, b);
                if (!need)
                {
                    j++;
                    continue;
                }

                int start = j;
                int end = j;

                // merge contiguous segments with same pair of labels
                while (end + 1 < nz)
                {
                    string a2 = cell[i - 1, end + 1];
                    string b2 = cell[i, end + 1];
                    if (!ShouldWallBetween(a2, b2)) break;

                    // keep merging even if swapped order (A/B vs B/A)
                    if (!SamePair(a, b, a2, b2)) break;

                    end++;
                }

                float x = xs[i];
                float z0 = zs[start];
                float z1 = zs[end + 1];

                AddInteriorWall(shellVerts, shellTris, shellUvs,
                    new Vector3(x, 0f, z0),
                    new Vector3(x, 0f, z1),
                    wallHeight, t);

                j = end + 1;
            }
        }

        // Horizontal boundaries (between j-1 and j) at z = zs[j]
        for (int j = 1; j < nz; j++)
        {
            int i = 0;
            while (i < nx)
            {
                string a = cell[i, j - 1];
                string b = cell[i, j];

                bool need = ShouldWallBetween(a, b);
                if (!need)
                {
                    i++;
                    continue;
                }

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

                AddInteriorWall(shellVerts, shellTris, shellUvs,
                    new Vector3(x0, 0f, z),
                    new Vector3(x1, 0f, z),
                    wallHeight, t);

                i = end + 1;
            }
        }
    }

    bool SamePair(string a, string b, string c, string d)
    {
        // treat (a,b) same as (b,a)
        return (a == c && b == d) || (a == d && b == c);
    }

    bool ShouldWallBetween(string a, string b)
    {
        if (a == b) return false;

        // Always consider "Living" as the default area
        // Entry open to living means no wall there (floor seam only).
        if (entryOpenToLiving)
        {
            if ((a == "Entry" && b == "Living") || (a == "Living" && b == "Entry"))
                return false;
        }

        return true;
    }

    string LabelAt(List<RectXZ> rooms, float x, float z)
    {
        // First match wins (shouldn't overlap)
        for (int i = 0; i < rooms.Count; i++)
        {
            if (rooms[i].Contains(x, z))
                return rooms[i].label;
        }
        return "Living";
    }

    void UniqueSort(List<float> vals)
    {
        vals.Sort();
        // unique with epsilon
        const float eps = 1e-4f;
        int w = 0;
        for (int r = 0; r < vals.Count; r++)
        {
            if (w == 0 || Mathf.Abs(vals[r] - vals[w - 1]) > eps)
                vals[w++] = vals[r];
        }
        if (w < vals.Count) vals.RemoveRange(w, vals.Count - w);
    }

    // ------------------------- Separate Floor Objects -------------------------
    void RebuildFloorObjects(Dictionary<string, FloorBuild> floors)
    {
        Transform root = GetOrCreateFloorsRoot();

        var needed = new HashSet<string>(floors.Keys, StringComparer.OrdinalIgnoreCase);

        // create/update
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
            var parentMr = GetComponent<MeshRenderer>();
            if (mr.sharedMaterial == null && parentMr != null)
                mr.sharedMaterial = parentMr.sharedMaterial;

            var mf = child.GetComponent<MeshFilter>();
            Mesh m = mf.sharedMesh;
            if (m == null)
            {
                m = new Mesh { name = $"Floor_{name}" };
                mf.sharedMesh = m;
            }
            else
            {
                m.Clear();
            }

            m.SetVertices(fb.verts);
            m.SetTriangles(fb.tris, 0);
            m.SetUVs(0, fb.uvs);
            m.RecalculateNormals();
            m.RecalculateBounds();
            m.RecalculateTangents();
        }

        // delete extras
        var toDelete = new List<GameObject>();
        for (int i = 0; i < root.childCount; i++)
        {
            var ch = root.GetChild(i);
            if (!needed.Contains(ch.name))
                toDelete.Add(ch.gameObject);
        }

        foreach (var go in toDelete)
        {
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
    }

    void ClearFloorObjects()
    {
        Transform root = transform.Find("Floors");
        if (root == null) return;

        if (Application.isPlaying) Destroy(root.gameObject);
        else DestroyImmediate(root.gameObject);
    }

    Transform GetOrCreateFloorsRoot()
    {
        Transform root = transform.Find("Floors");
        if (root != null) return root;

        var go = new GameObject("Floors");
        go.transform.SetParent(transform, false);
        return go.transform;
    }

    // ------------------------- Interior Wall Prism -------------------------
    void AddInteriorWall(List<Vector3> verts, List<int> tris, List<Vector2> uvs,
                         Vector3 a, Vector3 b, float wallHeight, float thickness)
    {
        Vector3 dir = (b - a);
        dir.y = 0f;
        float len = dir.magnitude;
        if (len < 0.0001f) return;
        dir /= len;

        Vector3 perp = Vector3.Cross(Vector3.up, dir).normalized;
        Vector3 half = perp * (thickness * 0.5f);

        Vector3 aPos = a + half;
        Vector3 aNeg = a - half;
        Vector3 bPos = b + half;
        Vector3 bNeg = b - half;

        Vector3 up = Vector3.up * wallHeight;

        Vector3 aPosT = aPos + up;
        Vector3 aNegT = aNeg + up;
        Vector3 bPosT = bPos + up;
        Vector3 bNegT = bNeg + up;

        // Long faces
        AddQuad(verts, tris, uvs, aPos, bPos, bPosT, aPosT, perp);
        AddQuad(verts, tris, uvs, bNeg, aNeg, aNegT, bNegT, -perp);

        // Top cap
        AddQuad(verts, tris, uvs, aNegT, bNegT, bPosT, aPosT, Vector3.up);

        // End caps
        AddQuad(verts, tris, uvs, aNeg, aPos, aPosT, aNegT, -dir);
        AddQuad(verts, tris, uvs, bPos, bNeg, bNegT, bPosT, dir);

        // Bottom omitted to avoid z-fighting with floor meshes.
    }

    // ------------------------- Outer Walls (Thick) -------------------------
    void AddThickWallSegment(List<Vector3> verts, List<int> tris, List<Vector2> uvs,
                             Vector3 aInner, Vector3 bInner,
                             Vector3 aOuter, Vector3 bOuter,
                             float wallHeight, Vector3 roomCenter)
    {
        AddThickWallSegmentRange(verts, tris, uvs, aInner, bInner, aOuter, bOuter,
                                 baseY: 0f, topY: wallHeight, roomCenter: roomCenter,
                                 addBottomFace: true);
    }

    void AddThickWallSegmentRange(List<Vector3> verts, List<int> tris, List<Vector2> uvs,
                                  Vector3 aInner, Vector3 bInner,
                                  Vector3 aOuter, Vector3 bOuter,
                                  float baseY, float topY,
                                  Vector3 roomCenter,
                                  bool addBottomFace)
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

        // Inner face
        AddQuad(verts, tris, uvs, aInnerB, bInnerB, bInnerT, aInnerT, inward);

        // Outer face
        AddQuad(verts, tris, uvs, bOuterB, aOuterB, aOuterT, bOuterT, outward);

        // Top face
        AddQuad(verts, tris, uvs, aInnerT, bInnerT, bOuterT, aOuterT, Vector3.up);

        // Bottom face (underside of thickness)
        if (addBottomFace)
            AddQuad(verts, tris, uvs, aOuterB, bOuterB, bInnerB, aInnerB, Vector3.down);
    }

    void AddThickWallSegmentWithDoor(List<Vector3> verts, List<int> tris, List<Vector2> uvs,
                                     Vector3 aInner, Vector3 bInner,
                                     Vector3 aOuter, Vector3 bOuter,
                                     float wallHeight, Vector3 roomCenter,
                                     float dWidth, float dHeight, float dOffset)
    {
        float doorTopY = Mathf.Clamp(dHeight, 0.01f, wallHeight - 0.001f);

        Vector3 wallDir = (bInner - aInner);
        wallDir.y = 0f;
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

        // IMPORTANT: outer door points are offset from inner points (prevents angled jambs)
        Vector3 outward = EdgeOutwardNormal(aInner, bInner, roomCenter);
        Vector3 lOuter = lInner + outward * outerWallThickness;
        Vector3 rOuter = rInner + outward * outerWallThickness;

        // Left solid
        if (leftT > 0.0001f)
            AddThickWallSegment(verts, tris, uvs, aInner, lInner, aOuter, lOuter, wallHeight, roomCenter);

        // Right solid
        if (rightT < segLen - 0.0001f)
            AddThickWallSegment(verts, tris, uvs, rInner, bInner, rOuter, bOuter, wallHeight, roomCenter);

        // Header above door
        AddThickWallSegmentRange(verts, tris, uvs, lInner, rInner, lOuter, rOuter,
                                 baseY: doorTopY, topY: wallHeight,
                                 roomCenter: roomCenter,
                                 addBottomFace: true);

        // Jambs
        Vector3 lInnerTop = new Vector3(lInner.x, doorTopY, lInner.z);
        Vector3 lOuterTop = new Vector3(lOuter.x, doorTopY, lOuter.z);
        Vector3 rInnerTop = new Vector3(rInner.x, doorTopY, rInner.z);
        Vector3 rOuterTop = new Vector3(rOuter.x, doorTopY, rOuter.z);

        Vector3 lInnerB = new Vector3(lInner.x, 0f, lInner.z);
        Vector3 lOuterB = new Vector3(lOuter.x, 0f, lOuter.z);
        Vector3 rInnerB = new Vector3(rInner.x, 0f, rInner.z);
        Vector3 rOuterB = new Vector3(rOuter.x, 0f, rOuter.z);

        AddQuad(verts, tris, uvs, lInnerB, lOuterB, lOuterTop, lInnerTop, wallDir);
        AddQuad(verts, tris, uvs, rOuterB, rInnerB, rInnerTop, rOuterTop, -wallDir);
    }

    // ------------------------- Geometry Helpers -------------------------
    Vector3 EdgeOutwardNormal(Vector3 a, Vector3 b, Vector3 center)
    {
        Vector3 dir = b - a;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0f ? dir.normalized : Vector3.right;

        Vector3 outward = Vector3.Cross(Vector3.up, dir).normalized;

        Vector3 mid = (a + b) * 0.5f;
        Vector3 toOutside = mid - center;
        toOutside.y = 0f;

        if (Vector3.Dot(outward, toOutside) < 0f)
            outward = -outward;

        return outward;
    }

    Vector3 OffsetCorner(Vector3 corner, Vector3 outwardPrev, Vector3 outwardNext, float thickness)
    {
        float d = Mathf.Clamp(Vector3.Dot(outwardPrev, outwardNext), -0.999f, 0.999f);
        float scale = thickness / (1f + d);
        return corner + (outwardPrev + outwardNext) * scale;
    }

    void AddQuad(List<Vector3> verts, List<int> tris, List<Vector2> uvs,
                 Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3,
                 Vector3 desiredNormal)
    {
        int start = verts.Count;

        verts.Add(v0);
        verts.Add(v1);
        verts.Add(v2);
        verts.Add(v3);

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
