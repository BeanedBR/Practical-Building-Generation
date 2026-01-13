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

    [Header("Doorway (on forward wall +Z)")]
    public bool cutDoorway = true;
    [Min(0.1f)] public float doorWidth = 1.0f;
    [Min(0.1f)] public float doorHeight = 2.1f;
    [Tooltip("Offset along the forward wall segment direction (c2 -> c3). Positive moves toward c3.")]
    public float doorOffset = 0f;

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

        var verts = new List<Vector3>(1024);
        var tris = new List<int>(2048);
        var uvs = new List<Vector2>(1024);

        // Floor
        AddQuad(verts, tris, uvs, c0, c1, c2, c3, Vector3.up);
        if (doubleSidedFloor)
            AddQuad(verts, tris, uvs, c0, c1, c2, c3, Vector3.down);

        // Outward normals per edge
        Vector3 n0 = EdgeOutwardNormal(c0, c1, center);
        Vector3 n1 = EdgeOutwardNormal(c1, c2, center);
        Vector3 n2 = EdgeOutwardNormal(c2, c3, center);
        Vector3 n3 = EdgeOutwardNormal(c3, c0, center);

        // Shared outer corners (mitered)
        Vector3 o0 = OffsetCorner(c0, n3, n0, wallThickness);
        Vector3 o1 = OffsetCorner(c1, n0, n1, wallThickness);
        Vector3 o2 = OffsetCorner(c2, n1, n2, wallThickness);
        Vector3 o3 = OffsetCorner(c3, n2, n3, wallThickness);

        // Thick walls
        AddThickWallSegment(verts, tris, uvs, c0, c1, o0, o1, height, center);
        AddThickWallSegment(verts, tris, uvs, c1, c2, o1, o2, height, center);

        // Forward wall (+Z): c2 -> c3
        if (cutDoorway)
            AddThickWallSegmentWithDoor(verts, tris, uvs, c2, c3, o2, o3, height, center, doorWidth, doorHeight, doorOffset);
        else
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

    // Standard thick wall segment (closed on top + bottom of thickness)
    void AddThickWallSegment(List<Vector3> verts, List<int> tris, List<Vector2> uvs,
                             Vector3 aInner, Vector3 bInner,
                             Vector3 aOuter, Vector3 bOuter,
                             float wallHeight, Vector3 roomCenter)
    {
        AddThickWallSegmentRange(verts, tris, uvs, aInner, bInner, aOuter, bOuter,
                                 baseY: 0f, topY: wallHeight, roomCenter: roomCenter,
                                 addBottomFace: true);
    }

    // Thick wall segment only between baseY..topY (used for the header above the door)
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

        // Inward/outward (planar)
        Vector3 mid = (aInner + bInner) * 0.5f;
        Vector3 inward = (roomCenter - mid);
        inward.y = 0f;
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

    // Door cutout on one wall segment (hole goes through thickness)
    void AddThickWallSegmentWithDoor(List<Vector3> verts, List<int> tris, List<Vector2> uvs,
                                     Vector3 aInner, Vector3 bInner,
                                     Vector3 aOuter, Vector3 bOuter,
                                     float wallHeight, Vector3 roomCenter,
                                     float dWidth, float dHeight, float dOffset)
    {
        // Clamp door height so it can't exceed wall height
        float doorTopY = Mathf.Clamp(dHeight, 0.01f, wallHeight - 0.001f);

        // Wall direction (planar) and length
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

        // Center in the segment + user offset, clamped so door fits
        float centerT = (segLen * 0.5f) + dOffset;
        centerT = Mathf.Clamp(centerT, halfDoor, segLen - halfDoor);

        float leftT = centerT - halfDoor;
        float rightT = centerT + halfDoor;

        // Points along inner/outer edges
        Vector3 lInner = aInner + wallDir * leftT;
        Vector3 rInner = aInner + wallDir * rightT;

        Vector3 lOuter = aOuter + wallDir * leftT;
        Vector3 rOuter = aOuter + wallDir * rightT;

        // Left solid section
        if (leftT > 0.0001f)
            AddThickWallSegment(verts, tris, uvs, aInner, lInner, aOuter, lOuter, wallHeight, roomCenter);

        // Right solid section
        if (rightT < segLen - 0.0001f)
            AddThickWallSegment(verts, tris, uvs, rInner, bInner, rOuter, bOuter, wallHeight, roomCenter);

        // Header above the door (from doorTopY to wallHeight), includes underside (bottom face at baseY=doorTopY)
        AddThickWallSegmentRange(verts, tris, uvs, lInner, rInner, lOuter, rOuter,
                                 baseY: doorTopY, topY: wallHeight,
                                 roomCenter: roomCenter,
                                 addBottomFace: true);

        // Jambs (the thickness faces inside the opening) from y=0 to y=doorTopY
        Vector3 lInnerTop = new Vector3(lInner.x, doorTopY, lInner.z);
        Vector3 lOuterTop = new Vector3(lOuter.x, doorTopY, lOuter.z);
        Vector3 rInnerTop = new Vector3(rInner.x, doorTopY, rInner.z);
        Vector3 rOuterTop = new Vector3(rOuter.x, doorTopY, rOuter.z);

        Vector3 lInnerB = new Vector3(lInner.x, 0f, lInner.z);
        Vector3 lOuterB = new Vector3(lOuter.x, 0f, lOuter.z);
        Vector3 rInnerB = new Vector3(rInner.x, 0f, rInner.z);
        Vector3 rOuterB = new Vector3(rOuter.x, 0f, rOuter.z);

        // Opening interior lies between left->right along +wallDir
        AddQuad(verts, tris, uvs, lInnerB, lOuterB, lOuterTop, lInnerTop, wallDir);   // left jamb faces into opening
        AddQuad(verts, tris, uvs, rOuterB, rInnerB, rInnerTop, rOuterTop, -wallDir);   // right jamb faces into opening
    }

    // Outward normal of an edge in XZ plane, forced to point away from center.
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

    // Intersection of two offset edges at a corner (miter)
    Vector3 OffsetCorner(Vector3 corner, Vector3 outwardPrev, Vector3 outwardNext, float thickness)
    {
        float d = Mathf.Clamp(Vector3.Dot(outwardPrev, outwardNext), -0.999f, 0.999f);
        float scale = thickness / (1f + d);
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
