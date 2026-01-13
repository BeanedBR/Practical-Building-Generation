using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ProceduralRoom : MonoBehaviour
{
    [Header("Room Generation")]
    [Min(0.01f)] public float width = 6f;
    [Min(0.01f)] public float length = 8f;
    [Min(0.01f)] public float height = 3f;

    [Header("Walls")]
    [Min(0.001f)] public float wallThickness = 0.2f;

    [Header("Floor")]
    public bool doubleSidedFloor = true;

    [Tooltip("Generate automatically when values change in the editor.")]
    public bool regenerateInEditor = true;

    Mesh _mesh;

    void OnEnable() => Generate();

    void OnValidate()
    {
        if (regenerateInEditor) Generate();
    }

    public void Generate()
    {
        var mf = GetComponent<MeshFilter>();

        if (_mesh == null) _mesh = new Mesh { name = "ProceduralRoomMesh" };
        else _mesh.Clear();

        float hx = width * 0.5f;
        float hz = length * 0.5f;

        // Inner rectangle corners (CCW)
        Vector3 c0 = new Vector3(-hx, 0f, -hz);
        Vector3 c1 = new Vector3(hx, 0f, -hz);
        Vector3 c2 = new Vector3(hx, 0f, hz);
        Vector3 c3 = new Vector3(-hx, 0f, hz);

        Vector3 center = (c0 + c1 + c2 + c3) * 0.25f;

        var verts = new List<Vector3>(512);
        var tris = new List<int>(1024);
        var uvs = new List<Vector2>(512);

        // Floor
        AddQuad(verts, tris, uvs, c0, c1, c2, c3, Vector3.up);
        if (doubleSidedFloor)
            AddQuad(verts, tris, uvs, c0, c1, c2, c3, Vector3.down);

        // Compute outward normals per edge (so corners can be mitered / intersected)
        Vector3 n0 = EdgeOutwardNormal(c0, c1, center);
        Vector3 n1 = EdgeOutwardNormal(c1, c2, center);
        Vector3 n2 = EdgeOutwardNormal(c2, c3, center);
        Vector3 n3 = EdgeOutwardNormal(c3, c0, center);

        // Compute shared outer corners by intersecting adjacent offset edges
        Vector3 o0 = OffsetCorner(c0, n3, n0, wallThickness);
        Vector3 o1 = OffsetCorner(c1, n0, n1, wallThickness);
        Vector3 o2 = OffsetCorner(c2, n1, n2, wallThickness);
        Vector3 o3 = OffsetCorner(c3, n2, n3, wallThickness);

        // Thick wall ring (no end caps needed for a closed loop)
        AddThickWallSegment(verts, tris, uvs, c0, c1, o0, o1, height, center);
        AddThickWallSegment(verts, tris, uvs, c1, c2, o1, o2, height, center);
        AddThickWallSegment(verts, tris, uvs, c2, c3, o2, o3, height, center);
        AddThickWallSegment(verts, tris, uvs, c3, c0, o3, o0, height, center);

        _mesh.SetVertices(verts);
        _mesh.SetTriangles(tris, 0);
        _mesh.SetUVs(0, uvs);

        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
        _mesh.RecalculateTangents();

        mf.sharedMesh = _mesh;
    }

    // Builds one wall segment of a closed ring using shared outer corners.
    void AddThickWallSegment(List<Vector3> verts, List<int> tris, List<Vector2> uvs,
                             Vector3 aInner, Vector3 bInner,
                             Vector3 aOuter, Vector3 bOuter,
                             float wallHeight, Vector3 roomCenter)
    {
        Vector3 aInnerTop = aInner + Vector3.up * wallHeight;
        Vector3 bInnerTop = bInner + Vector3.up * wallHeight;
        Vector3 aOuterTop = aOuter + Vector3.up * wallHeight;
        Vector3 bOuterTop = bOuter + Vector3.up * wallHeight;

        // Inward/outward (planar)
        Vector3 mid = (aInner + bInner) * 0.5f;
        Vector3 inward = (roomCenter - mid);
        inward.y = 0f;
        inward = inward.sqrMagnitude > 0f ? inward.normalized : Vector3.forward;
        Vector3 outward = -inward;

        // Inner face (visible inside)
        AddQuad(verts, tris, uvs, aInner, bInner, bInnerTop, aInnerTop, inward);

        // Outer face (visible outside)
        AddQuad(verts, tris, uvs, bOuter, aOuter, aOuterTop, bOuterTop, outward);

        // Top face (caps thickness)
        AddQuad(verts, tris, uvs, aInnerTop, bInnerTop, bOuterTop, aOuterTop, Vector3.up);

        // Bottom face
        AddQuad(verts, tris, uvs, aOuter, bOuter, bInner, aInner, Vector3.down);
    }

    // Outward normal of an edge in XZ plane, forced to point away from center.
    Vector3 EdgeOutwardNormal(Vector3 a, Vector3 b, Vector3 center)
    {
        Vector3 dir = b - a;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0f ? dir.normalized : Vector3.right;

        // For CCW polygons, up x dir gives outward; we still force it away from center to be safe.
        Vector3 outward = Vector3.Cross(Vector3.up, dir).normalized;

        Vector3 mid = (a + b) * 0.5f;
        Vector3 toOutside = mid - center;
        toOutside.y = 0f;

        if (Vector3.Dot(outward, toOutside) < 0f)
            outward = -outward;

        return outward;
    }

    // Intersection of two offset edges at a corner (miter). For 90° corners this is just corner + (nPrev + nNext) * thickness.
    Vector3 OffsetCorner(Vector3 corner, Vector3 outwardPrev, Vector3 outwardNext, float thickness)
    {
        float d = Mathf.Clamp(Vector3.Dot(outwardPrev, outwardNext), -0.999f, 0.999f);
        float scale = thickness / (1f + d); // derived from intersecting the two offset lines
        return corner + (outwardPrev + outwardNext) * scale;
    }

    // Adds a quad as two triangles. Ensures triangle winding matches desiredNormal.
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
