using System;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ProceduralApartment : MonoBehaviour
{
    [Header("Apartment Shell")]
    [Min(0.01f)] public float width = 6f;      // interior clear width
    [Min(0.01f)] public float length = 10f;    // interior clear length
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

    private Mesh _mesh;

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

        // 1. Calculate Layout
        ApartmentLayoutGenerator layoutGen = new ApartmentLayoutGenerator(this);

        float hx = width * 0.5f;
        float hz = length * 0.5f;
        float xMin = -hx, xMax = hx, zMin = -hz, zMax = hz;
        float doorX = Mathf.Clamp(-doorOffset, xMin + doorWidth * 0.5f, xMax - doorWidth * 0.5f);

        var rooms = layoutGen.GenerateRoomRectangles(rng, xMin, xMax, zMin, zMax, doorX);

        // 2. Build Mesh
        ApartmentMeshBuilder meshBuilder = new ApartmentMeshBuilder(this, mf, transform);

        // Note: We pass the reference to _mesh so we can persist it or recreate it
        if (_mesh == null) _mesh = new Mesh { name = "ProceduralApartmentMesh" };

        meshBuilder.BuildMesh(rooms, xMin, xMax, zMin, zMax, doorX, _mesh);
    }
}