using System.Collections.Generic;
using UnityEngine;

// Shared data structures for the Procedural Apartment system:
public struct RectXZ
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

public class FloorBuild
{
    public readonly List<Vector3> verts = new();
    public readonly List<int> tris = new();
    public readonly List<Vector2> uvs = new();
}