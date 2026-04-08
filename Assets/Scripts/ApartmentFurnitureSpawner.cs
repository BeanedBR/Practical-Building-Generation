using System;
using System.Collections.Generic;
using UnityEngine;

public class ApartmentFurnitureSpawner
{
    private ProceduralApartment config;
    private Transform parentTransform;

    // This list acts for spatial awareness. Every time we place a piece of furniture (or a door),
    // we save its bounding box here so future items know to avoid it.
    private List<Bounds> occupiedSpace = new List<Bounds>();

    public ApartmentFurnitureSpawner(ProceduralApartment config, Transform parentTransform)
    {
        this.config = config;
        this.parentTransform = parentTransform;
    }

    public void Spawn(List<RectXZ> rooms, float xMin, float xMax, float zMin, float zMax, System.Random rng, List<Bounds> doorBlockers)
    {
        // Clear furniture so it doesn't spawn on top of older generations:
        ClearFurniture();
        occupiedSpace.Clear();

        // 1. REGISTER DOOR BLOCKERS
        // Invisible door boundaries as "solid furniture" right from the start. 
        // Preventing the algorithm from placing a furniture like a bedroom dresser front of a doorway.
        if (doorBlockers != null)
        {
            foreach (var localDb in doorBlockers)
            {
                // The mesh builder gives the local coordinates, but the spatial awareness needs world coordinates.
                Vector3 worldCenter = parentTransform.TransformPoint(localDb.center);
                Vector3 worldSize = Vector3.Scale(localDb.size, parentTransform.lossyScale);
                occupiedSpace.Add(new Bounds(worldCenter, worldSize));
            }
        }

        Transform root = GetOrCreateFurnitureRoot();

        // 2. SPAWN ROOM BY ROOM
        // Loop through the defined rooms (Kitchen, Bath, Bed) and try to snap items to the specific walls.
        // Each furniture has their own pivots since they have been downloaded from the internet from different creators with individual practices:
        foreach (var room in rooms)
        {
            if (room.label == "Bedroom")
            {
                TrySpawnOnWall(config.bedPrefab, room, rng, root, rooms, xMin, xMax, zMin, zMax);
                TrySpawnOnWall(config.dresserPrefab, room, rng, root, rooms, xMin, xMax, zMin, zMax);

                // The computer desk model was exported sideways, applies a 90-degree twist to fix it
                TrySpawnOnWall(config.computerDeskPrefab, room, rng, root, rooms, xMin, xMax, zMin, zMax, 90f);
            }
            else if (room.label == "Bathroom")
            {
                TrySpawnOnWall(config.toiletPrefab, room, rng, root, rooms, xMin, xMax, zMin, zMax, -90f);

                // The mirror needs to be on the wall, not the floor, pass 1.1f height offset
                TrySpawnOnWall(config.mirrorPrefab, room, rng, root, rooms, xMin, xMax, zMin, zMax, 0f, 1.1f);
            }
            else if (room.label == "Kitchen")
            {
                // Fridge also needs a 90-degree twist to fix
                TrySpawnOnWall(config.fridgePrefab, room, rng, root, rooms, xMin, xMax, zMin, zMax, 90f);
            }
        }

        // 3. SPAWN LIVING ROOM ITEMS
        // The living area isn't a simple rectangle (leftover space), uses a special raycast
        // method to find the walls. Flip the TV 180 degrees so it doesn't face the wall.
        TrySpawnInLivingRoomWall(config.tvSetPrefab, rooms, xMin, xMax, zMin, zMax, rng, root, 180f);
        TrySpawnInLivingRoomWall(config.sofaPrefab, rooms, xMin, xMax, zMin, zMax, rng, root);
        TrySpawnInLivingRoomWall(config.tableSetPrefab, rooms, xMin, xMax, zMin, zMax, rng, root);
    }

    // Attempts to place an object flush against one of the 4 walls of a specific rectangular room.
    private void TrySpawnOnWall(GameObject prefab, RectXZ room, System.Random rng, Transform root, List<RectXZ> rooms, float xMin, float xMax, float zMin, float zMax, float yRotationOffset = 0f, float yPositionOffset = 0f)
    {
        if (prefab == null) return;
        float padding = 0.05f; // Leaves a tiny gap between the furniture and the wall to prevent Z-fighting (flickering textures)

        // Try up to 50 times to find a valid spot. If the room is too cramped, we just give up rather than crashing or infinitely looping.
        for (int i = 0; i < 50; i++)
        {
            // Pick a random wall (0=North, 1=East, 2=South, 3=West) and figure out which way the furniture should face
            int wall = rng.Next(0, 4);
            float angle = 0f;
            if (wall == 0) angle = 180f;      // North wall -> Face South
            else if (wall == 1) angle = 270f; // East wall -> Face West
            else if (wall == 2) angle = 0f;   // Soth wall -> Face North
            else if (wall == 3) angle = 90f;  // West wall -> Face East

            // Instantiate the object invisibly at 0,0,0 first. 
            // To measure its exact physical size after it's been rotated.
            Vector3 originalEuler = prefab.transform.eulerAngles;
            Quaternion rotation = Quaternion.Euler(originalEuler.x, angle + yRotationOffset, originalEuler.z);
            GameObject obj = UnityEngine.Object.Instantiate(prefab, root);
            obj.name = prefab.name;
            obj.transform.localPosition = Vector3.zero;
            obj.transform.localRotation = rotation;

            Bounds initialBounds = GetObjectBounds(obj);
            Vector3 localCenter = parentTransform.InverseTransformPoint(initialBounds.center);
            Vector3 extents = initialBounds.extents;

            // Calculate the ideal center point for the object, offset by its exact half-width/length so the edge touches the wall
            float cx = 0f, cz = 0f;
            if (wall == 0)
            {
                cx = Mathf.Lerp(room.x0 + extents.x, room.x1 - extents.x, (float)rng.NextDouble());
                cz = room.z1 - extents.z - padding;
            }
            else if (wall == 1)
            {
                cx = room.x1 - extents.x - padding;
                cz = Mathf.Lerp(room.z0 + extents.z, room.z1 - extents.z, (float)rng.NextDouble());
            }
            else if (wall == 2)
            {
                cx = Mathf.Lerp(room.x0 + extents.x, room.x1 - extents.x, (float)rng.NextDouble());
                cz = room.z0 + extents.z + padding;
            }
            else if (wall == 3)
            {
                cx = room.x0 + extents.x + padding;
                cz = Mathf.Lerp(room.z0 + extents.z, room.z1 - extents.z, (float)rng.NextDouble());
            }

            // PIVOT CENTERING: Not all 3D models have their pivot point exactly in the center.
            // We calculate the difference so the mesh sits perfectly flush against the wall regardless of how the artist exported it.
            Vector3 targetCenter = new Vector3(cx, localCenter.y, cz);
            Vector3 pivotOffset = obj.transform.localPosition - localCenter;
            obj.transform.localPosition = targetCenter + pivotOffset;

            // FLOOR SNAPPING: Fixes floating or sinking furniture.
            // We find the absolute lowest point of the mesh and shift the whole object up so that lowest point touches the floor.
            Bounds finalBounds = GetObjectBounds(obj);
            float floorWorldY = parentTransform.TransformPoint(Vector3.zero).y;
            float verticalShift = floorWorldY - finalBounds.min.y;
            obj.transform.position += Vector3.up * (verticalShift + yPositionOffset);

            // Check if the placement is legal. If it is, we keep it and break out of the loop.
            if (CheckAndRegisterPlacement(obj, room.label, rooms, xMin, xMax, zMin, zMax)) return;
        }
    }

    // Uses a grid-walking algorithm to find walls for the irregularly shaped Living Room.
    private void TrySpawnInLivingRoomWall(GameObject prefab, List<RectXZ> rooms, float xMin, float xMax, float zMin, float zMax, System.Random rng, Transform root, float yRotationOffset = 0f, float yPositionOffset = 0f)
    {
        if (prefab == null) return;
        float padding = 0.05f;
        float stepSize = 0.2f;

        for (int i = 0; i < 50; i++)
        {
            // Pick a random starting point somewhere safely inside the Living room
            float startX = Mathf.Lerp(xMin + 1f, xMax - 1f, (float)rng.NextDouble());
            float startZ = Mathf.Lerp(zMin + 1f, zMax - 1f, (float)rng.NextDouble());
            if (LabelAt(rooms, startX, startZ) != "Living") continue;

            // Pick a random direction to walk
            int dirIdx = rng.Next(0, 4);
            float dx = 0, dz = 0; float angle = 0f;
            if (dirIdx == 0) { dz = 1f; angle = 180f; }       // Walk North
            else if (dirIdx == 1) { dx = 1f; angle = 270f; }  // Walk East
            else if (dirIdx == 2) { dz = -1f; angle = 0f; }   // Walk South
            else if (dirIdx == 3) { dx = -1f; angle = 90f; }  // Walk West

            // Set up the object to measure its bounds
            Vector3 originalEuler = prefab.transform.eulerAngles;
            Quaternion rotation = Quaternion.Euler(originalEuler.x, angle + yRotationOffset, originalEuler.z);
            GameObject obj = UnityEngine.Object.Instantiate(prefab, root);
            obj.name = prefab.name;
            obj.transform.localPosition = Vector3.zero;
            obj.transform.localRotation = rotation;

            Bounds initialBounds = GetObjectBounds(obj);
            Vector3 localCenter = parentTransform.InverseTransformPoint(initialBounds.center);
            Vector3 extents = initialBounds.extents;

            float cx = startX, cz = startZ;
            bool hitWall = false;

            // Walk forward in our chosen direction until we step out of the living room (hit a partition or exterior wall)
            for (int step = 0; step < 100; step++)
            {
                cx += dx * stepSize;
                cz += dz * stepSize;
                if (cx < xMin || cx > xMax || cz < zMin || cz > zMax || LabelAt(rooms, cx, cz) != "Living")
                {
                    hitWall = true; break;
                }
            }

            if (hitWall)
            {
                // Found a wall: so step backwards just enough to fit the physical size of the furniture ensuring it doesn't clip.
                float margin = (dx != 0) ? extents.x : extents.z;
                cx -= dx * (margin + padding);
                cz -= dz * (margin + padding);

                // Apply our pivot corrections and floor snapping just like the explicit room method:
                Vector3 targetCenter = new Vector3(cx, localCenter.y, cz);
                Vector3 pivotOffset = obj.transform.localPosition - localCenter;
                obj.transform.localPosition = targetCenter + pivotOffset;

                Bounds finalBounds = GetObjectBounds(obj);
                float floorWorldY = parentTransform.TransformPoint(Vector3.zero).y;
                float verticalShift = floorWorldY - finalBounds.min.y;
                obj.transform.position += Vector3.up * (verticalShift + yPositionOffset);

                if (CheckAndRegisterPlacement(obj, "Living", rooms, xMin, xMax, zMin, zMax)) return;
            }
            else
            {
                // Raycast failed (maybe spawned in a weird tight corner), destroy and try again.
                DestroySafe(obj);
            }
        }
    }

    // Safety check: ensures the object is inside the correct room and doesn't overlap other items:
    private bool CheckAndRegisterPlacement(GameObject obj, string roomLabel, List<RectXZ> rooms, float xMin, float xMax, float zMin, float zMax)
    {
        Bounds bounds = GetObjectBounds(obj);

        Vector3 min = parentTransform.InverseTransformPoint(bounds.min);
        Vector3 max = parentTransform.InverseTransformPoint(bounds.max);

        // We inset the check slightly to ignore tiny floating point rounding errors that might say it's 0.0001 units out of bounds
        float p = 0.05f;
        min.x += p; min.z += p;
        max.x -= p; max.z -= p;

        // 1. Total Apartment bounds check
        if (min.x < xMin || max.x > xMax || min.z < zMin || max.z > zMax)
        {
            DestroySafe(obj); return false;
        }

        // 2. 4-Corner Room Check
        // We check all 4 corners of the object. If a desk is too long and pokes through a thin partition wall 
        // into the next room, one of these corners will fail the check and reject the placement.
        if (LabelAt(rooms, min.x, min.z) != roomLabel || LabelAt(rooms, min.x, max.z) != roomLabel ||
            LabelAt(rooms, max.x, min.z) != roomLabel || LabelAt(rooms, max.x, max.z) != roomLabel)
        {
            DestroySafe(obj); return false;
        }

        // 3. Collision Check against other furniture & doors
        bounds.Expand(0.15f); // Add a small buffer so items don't touch perfectly edge-to-edge
        foreach (var placed in occupiedSpace)
        {
            if (bounds.Intersects(placed))
            {
                DestroySafe(obj); return false;
            }
        }

        // Survived all checks: so add it to the permanent list so future objects avoid it.
        occupiedSpace.Add(bounds);
        return true;
    }

    // Recursively grabs the physical dimensions of the object by looking at its meshes.
    private Bounds GetObjectBounds(GameObject obj)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }

        // Fallback just in case the prefab only has colliders and no meshes
        Collider[] colliders = obj.GetComponentsInChildren<Collider>();
        if (colliders.Length > 0)
        {
            Bounds b = colliders[0].bounds;
            for (int i = 1; i < colliders.Length; i++) b.Encapsulate(colliders[i].bounds);
            return b;
        }

        // Ultimate fallback to prevent crashing if someone feeds it an empty GameObject
        return new Bounds(obj.transform.position, new Vector3(1f, 1f, 1f));
    }

    // Helper to query our layout grid
    private string LabelAt(List<RectXZ> rooms, float x, float z)
    {
        foreach (var r in rooms) if (r.Contains(x, z)) return r.label;
        return "Living";
    }

    public void ClearFurniture()
    {
        Transform root = parentTransform.Find("Furniture");
        if (root == null) return;

        // Must loop backwards when destroying children, otherwise the index shifts and we miss items
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            DestroySafe(root.GetChild(i).gameObject);
        }
    }

    // Handles the difference between deleting things while playing vs editing in the Unity Editor
    private void DestroySafe(GameObject obj)
    {
        if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
        else UnityEngine.Object.DestroyImmediate(obj);
    }

    private Transform GetOrCreateFurnitureRoot()
    {
        Transform root = parentTransform.Find("Furniture");
        if (root != null) return root;

        var go = new GameObject("Furniture");
        go.transform.SetParent(parentTransform, false);
        return go.transform;
    }
}