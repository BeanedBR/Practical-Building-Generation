using System;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ProceduralApartment : MonoBehaviour
{
    [Header("Apartment Shell")]
    [Min(0.01f)] public float width = 6f;
    [Min(0.01f)] public float length = 10f;
    [Min(0.01f)] public float height = 3f;

    [Header("Outer Walls")]
    [Min(0.001f)] public float outerWallThickness = 0.2f;

    [Header("Front Door")]
    public bool cutFrontDoor = true;
    [Min(0.1f)] public float doorWidth = 1.0f;
    [Min(0.1f)] public float doorHeight = 2.1f;
    public float doorOffset = 0f;

    [Header("Interior Rooms")]
    public bool includeKitchen = true;
    public bool includeBathroom = true;
    public bool includeBedroom = true;

    [Header("Randomness")]
    public int seed = 12345;
    public bool randomizeSeedOnPlay = false;

    [Header("Entry/Clearance")]
    public float entryMinWidth = 1.6f; public float entryMaxWidth = 2.8f;
    public float entryMinDepth = 1.4f; public float entryMaxDepth = 3.0f;

    [Header("Kitchen Constraints")]
    public float kitchenMinWidth = 1.8f; public float kitchenMaxWidth = 3.5f;
    public float kitchenMinDepth = 1.8f; public float kitchenMaxDepth = 3.2f;

    [Header("Bathroom Constraints")]
    public float bathMinWidth = 1.5f; public float bathMaxWidth = 2.6f;
    public float bathMinDepth = 1.6f; public float bathMaxDepth = 2.8f;

    [Header("Bedroom Constraints")]
    public float bedMinWidth = 2.6f; public float bedMaxWidth = 4.5f;
    public float bedMinDepth = 2.6f; public float bedMaxDepth = 5.0f;

    [Header("Living Safeguards")]
    public float minLivingDepth = 1.5f;
    public float minLivingWidth = 1.5f;

    [Header("Interior Partitions")]
    public bool generateInteriorWalls = true;
    public float interiorWallThickness = 0.12f;

    [Header("Floors")]
    public bool generateRoomFloors = true;
    public bool doubleSidedFloors = true;
    public bool entryOpenToLiving = true;

    [Header("Floor Materials")]
    public Material livingFloorMat;
    public Material kitchenFloorMat;
    public Material bathroomFloorMat;
    public Material bedroomFloorMat;
    public Material entryFloorMat;

    [Header("Wall Materials")]
    public Material exteriorWallMat; // Index 0
    public Material livingWallMat;   // Index 1
    public Material kitchenWallMat;  // Index 2
    public Material bathroomWallMat; // Index 3
    public Material bedroomWallMat;  // Index 4
    public Material entryWallMat;    // Index 5

    [Tooltip("Generate automatically in editor.")]
    public bool regenerateInEditor = true;

    private Mesh _mesh;

    void OnEnable() => Generate();
    void OnValidate() { if (regenerateInEditor) Generate(); }

    public void Generate()
    {
        if (Application.isPlaying && randomizeSeedOnPlay)
            seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);

        var rng = new System.Random(seed);
        var mf = GetComponent<MeshFilter>();
        var mr = GetComponent<MeshRenderer>();

        // Update Material Array for Submeshes
        Material[] mats = new Material[6];
        mats[0] = exteriorWallMat ? exteriorWallMat : livingWallMat;
        mats[1] = livingWallMat;
        mats[2] = kitchenWallMat ? kitchenWallMat : livingWallMat;
        mats[3] = bathroomWallMat ? bathroomWallMat : livingWallMat;
        mats[4] = bedroomWallMat ? bedroomWallMat : livingWallMat;
        mats[5] = entryWallMat ? entryWallMat : livingWallMat;

        // Fill nulls with default/white if living is also null
        for (int i = 0; i < 6; i++) if (mats[i] == null) mats[i] = new Material(Shader.Find("Standard"));

        mr.sharedMaterials = mats;

        // Generate
        ApartmentLayoutGenerator layoutGen = new ApartmentLayoutGenerator(this);
        float hx = width * 0.5f; float hz = length * 0.5f;
        float xMin = -hx, xMax = hx, zMin = -hz, zMax = hz;
        float doorX = Mathf.Clamp(-doorOffset, xMin + doorWidth * 0.5f, xMax - doorWidth * 0.5f);

        var rooms = layoutGen.GenerateRoomRectangles(rng, xMin, xMax, zMin, zMax, doorX);

        ApartmentMeshBuilder meshBuilder = new ApartmentMeshBuilder(this, mf, transform);
        if (_mesh == null) _mesh = new Mesh { name = "ProceduralApartmentMesh" };
        else _mesh.Clear();

        meshBuilder.BuildMesh(rooms, xMin, xMax, zMin, zMax, doorX, _mesh);
    }
}