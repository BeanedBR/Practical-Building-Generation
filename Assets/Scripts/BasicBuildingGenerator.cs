using UnityEngine;

public class BasicBuildingGenerator : MonoBehaviour
{
    [Header("Footprint (in segments)")]
    public int width = 8;       // segments along X
    public int depth = 6;       // segments along Z

    [Header("Floors")]
    public int minFloors = 3;
    public int maxFloors = 12;
    public float floorHeight = 3f;

    [Header("Segment Sizing")]
    public float segmentWidth = 2f;   // size of each wall/window unit
    public float segmentThickness = 0.2f;

    [Header("Prefabs (optional)")]
    public GameObject wallSegmentPrefab;   // 1x1-ish wall piece
    public GameObject windowSegmentPrefab; // 1x1-ish window piece

    [Range(0f, 1f)]
    public float windowChance = 0.35f;

    [Header("Misc")]
    public string containerName = "GeneratedBuilding";

    Transform _root;

    [ContextMenu("Generate")]
    public void Generate()
    {
        Clear();

        // Create a container so you can regenerate cleanly
        _root = new GameObject(containerName).transform;
        _root.SetParent(transform, false);

        int floors = Random.Range(minFloors, maxFloors + 1);

        // Build perimeter for each floor
        for (int f = 0; f < floors; f++)
        {
            float y = f * floorHeight;

            // Along +X edge (front)
            for (int x = 0; x < width; x++)
            {
                PlaceSegmentLocal(new Vector3(x * segmentWidth, y, 0f), 0f);
            }
            // Along -X edge (back)
            for (int x = 0; x < width; x++)
            {
                PlaceSegmentLocal(new Vector3(x * segmentWidth, y, (depth - 1) * segmentWidth), 180f);
            }
            // Along +Z edge (right)
            for (int z = 1; z < depth - 1; z++) // avoid double corners
            {
                PlaceSegmentLocal(new Vector3(0f, y, z * segmentWidth), -90f);
            }
            // Along -Z edge (left)
            for (int z = 1; z < depth - 1; z++)
            {
                PlaceSegmentLocal(new Vector3((width - 1) * segmentWidth, y, z * segmentWidth), 90f);
            }
        }

        // Optional: add a simple roof slab
        var roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roof.name = "Roof";
        roof.transform.SetParent(_root, false);
        roof.transform.localScale = new Vector3(width * segmentWidth, segmentThickness, depth * segmentWidth);
        roof.transform.localPosition = new Vector3((width) * segmentWidth * 0.5f, floors * floorHeight, (depth - 1) * segmentWidth * 0.5f);
    }

    void PlaceSegmentLocal(Vector3 localPos, float yRotation)
    {
        GameObject prefab = null;

        // Decide window vs wall
        bool useWindow = Random.value < windowChance && windowSegmentPrefab != null;
        if (useWindow) prefab = windowSegmentPrefab;
        else if (wallSegmentPrefab != null) prefab = wallSegmentPrefab;

        GameObject seg;
        if (prefab != null)
        {
            seg = Instantiate(prefab, _root);
        }
        else
        {
            // Fallback: make a thin cube as a “segment”
            seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            seg.name = useWindow ? "Window" : "Wall";
            seg.transform.localScale = new Vector3(segmentWidth, floorHeight, segmentThickness);
        }

        seg.transform.localPosition = localPos;
        seg.transform.localRotation = Quaternion.Euler(0f, yRotation + 90f, 0f);

        // Nudge forward so the wall sits on the perimeter line
        Vector3 forward = Quaternion.Euler(0f, yRotation, 0f) * Vector3.forward;
        seg.transform.localPosition += forward * (segmentThickness * 0.5f);
        // Center walls on segment cell
        seg.transform.localPosition += new Vector3(segmentWidth * 0.5f, floorHeight * 0.5f, segmentWidth * 0.5f);
    }

    [ContextMenu("Clear")]
    public void Clear()
    {
        var existing = transform.Find(containerName);
        if (existing != null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) DestroyImmediate(existing.gameObject);
            else Destroy(existing.gameObject);
#else
            Destroy(existing.gameObject);
#endif
        }
    }

    void Reset()
    {
        // Auto-generate in editor for a quick preview
#if UNITY_EDITOR
        if (!Application.isPlaying) Generate();
#endif
    }
}
