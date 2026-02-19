using System;
using System.Collections.Generic;
using UnityEngine;

public class ApartmentLayoutGenerator
{
    private ProceduralApartment config;

    public ApartmentLayoutGenerator(ProceduralApartment config)
    {
        this.config = config;
    }

    public List<RectXZ> GenerateRoomRectangles(System.Random rng, float xMin, float xMax, float zMin, float zMax, float doorX)
    {
        float W = xMax - xMin;
        float L = zMax - zMin;

        // Max front-depth we can spend while still guaranteeing bedroomMinDepth + minLivingDepth
        float maxFrontDepth = L - (config.includeBedroom ? config.bedMinDepth : 0f) - config.minLivingDepth;
        maxFrontDepth = Mathf.Max(0.5f, maxFrontDepth);

        // Sample depths
        float entryDepth = PickInRange(rng, config.entryMinDepth, config.entryMaxDepth, 0.5f, maxFrontDepth);
        float kitchenDepth = config.includeKitchen ? PickInRange(rng, config.kitchenMinDepth, config.kitchenMaxDepth, 0.5f, maxFrontDepth) : 0f;
        float bathDepth = config.includeBathroom ? PickInRange(rng, config.bathMinDepth, config.bathMaxDepth, 0.5f, maxFrontDepth) : 0f;

        float frontStripDepth = Mathf.Max(entryDepth, kitchenDepth, bathDepth);
        frontStripDepth = Mathf.Clamp(frontStripDepth, 0.5f, maxFrontDepth);

        // Ensure entry depth fits in front strip
        entryDepth = Mathf.Min(entryDepth, frontStripDepth);

        // Width budgeting
        float minCenter = Mathf.Clamp(config.entryMinWidth, 0.5f, W - 0.5f);
        float kW = 0f, bW = 0f;

        if (config.includeKitchen) kW = PickInRange(rng, config.kitchenMinWidth, config.kitchenMaxWidth, 0f, W);
        if (config.includeBathroom) bW = PickInRange(rng, config.bathMinWidth, config.bathMaxWidth, 0f, W);

        // If both exist, enforce kW + bW <= W - minCenter
        if (config.includeKitchen && config.includeBathroom)
        {
            float maxSum = Mathf.Max(0.1f, W - minCenter);
            float kMaxFeasible = Mathf.Min(config.kitchenMaxWidth, maxSum - config.bathMinWidth);
            float bMaxFeasible = Mathf.Min(config.bathMaxWidth, maxSum - config.kitchenMinWidth);

            kW = Mathf.Clamp(kW, config.kitchenMinWidth, Mathf.Max(config.kitchenMinWidth, kMaxFeasible));
            bW = Mathf.Clamp(bW, config.bathMinWidth, Mathf.Max(config.bathMinWidth, bMaxFeasible));

            float sum = kW + bW;
            if (sum > maxSum)
            {
                float over = sum - maxSum;
                if (kW >= bW)
                {
                    float newKW = Mathf.Max(config.kitchenMinWidth, kW - over);
                    over -= (kW - newKW);
                    kW = newKW;
                    if (over > 0f) bW = Mathf.Max(config.bathMinWidth, bW - over);
                }
                else
                {
                    float newBW = Mathf.Max(config.bathMinWidth, bW - over);
                    over -= (bW - newBW);
                    bW = newBW;
                    if (over > 0f) kW = Mathf.Max(config.kitchenMinWidth, kW - over);
                }
            }
        }

        // Side logic
        bool kitchenLeft = true;
        bool bathLeft = false;

        if (config.includeKitchen && config.includeBathroom)
        {
            bool optionA_OK = DoorInCenterInterval(doorX, xMin + kW, xMax - bW, config.entryMinWidth);
            bool optionB_OK = DoorInCenterInterval(doorX, xMin + bW, xMax - kW, config.entryMinWidth);

            if (optionA_OK && optionB_OK)
            {
                if (rng.NextDouble() < 0.5) { kitchenLeft = true; bathLeft = false; }
                else { kitchenLeft = false; bathLeft = true; }
            }
            else if (optionA_OK) { kitchenLeft = true; bathLeft = false; }
            else if (optionB_OK) { kitchenLeft = false; bathLeft = true; }
            else
            {
                if (doorX < 0f) { kitchenLeft = false; bathLeft = true; }
                else { kitchenLeft = true; bathLeft = false; }
            }
        }
        else if (config.includeKitchen)
        {
            kitchenLeft = (Mathf.Abs(doorX - xMin) > Mathf.Abs(doorX - xMax)) ? true : false;
            if (rng.NextDouble() < 0.5) kitchenLeft = !kitchenLeft;
        }
        else if (config.includeBathroom)
        {
            bathLeft = (Mathf.Abs(doorX - xMin) > Mathf.Abs(doorX - xMax)) ? true : false;
            if (rng.NextDouble() < 0.5) bathLeft = !bathLeft;
        }

        // Compute center interval available for Entry
        float leftOccupied = 0f;
        float rightOccupied = 0f;

        if (config.includeKitchen) { if (kitchenLeft) leftOccupied += kW; else rightOccupied += kW; }
        if (config.includeBathroom) { if (bathLeft) leftOccupied += bW; else rightOccupied += bW; }

        float entryAvailX0 = xMin + leftOccupied;
        float entryAvailX1 = xMax - rightOccupied;
        float entryAvailW = Mathf.Max(0.01f, entryAvailX1 - entryAvailX0);

        float eW = PickInRange(rng, config.entryMinWidth, config.entryMaxWidth, 0.5f, entryAvailW);
        eW = Mathf.Min(eW, entryAvailW);

        float entryX0 = Mathf.Clamp(doorX - eW * 0.5f, entryAvailX0, entryAvailX1 - eW);
        float entryX1 = entryX0 + eW;
        float entryZ1 = zMax;
        float entryZ0 = zMax - entryDepth;

        var rooms = new List<RectXZ>(8);
        rooms.Add(new RectXZ("Entry", entryX0, entryX1, entryZ0, entryZ1));

        if (config.includeKitchen)
        {
            float x0 = kitchenLeft ? xMin : (xMax - kW);
            float x1 = kitchenLeft ? (xMin + kW) : xMax;
            float z0 = zMax - Mathf.Min(kitchenDepth, frontStripDepth);
            rooms.Add(new RectXZ("Kitchen", x0, x1, z0, zMax));
        }

        if (config.includeBathroom)
        {
            float x0 = bathLeft ? xMin : (xMax - bW);
            float x1 = bathLeft ? (xMin + bW) : xMax;
            float z0 = zMax - Mathf.Min(bathDepth, frontStripDepth);
            rooms.Add(new RectXZ("Bathroom", x0, x1, z0, zMax));
        }

        if (config.includeBedroom)
        {
            float maxBedroomDepth = Mathf.Max(0.5f, L - frontStripDepth - config.minLivingDepth);
            float bedDepth = Mathf.Min(PickInRange(rng, config.bedMinDepth, config.bedMaxDepth, 0.5f, maxBedroomDepth), maxBedroomDepth);

            float maxBedroomWidth = Mathf.Max(0.5f, W - config.minLivingWidth);
            float bedWidth = Mathf.Min(PickInRange(rng, config.bedMinWidth, config.bedMaxWidth, 0.5f, maxBedroomWidth), maxBedroomWidth);

            bool bedLeft;
            if (config.includeBathroom) bedLeft = bathLeft;
            else bedLeft = rng.NextDouble() < 0.5;

            float bx0 = bedLeft ? xMin : (xMax - bedWidth);
            float bx1 = bedLeft ? (xMin + bedWidth) : xMax;
            float bz0 = zMin;
            float bz1 = zMin + bedDepth;

            rooms.Add(new RectXZ("Bedroom", bx0, bx1, bz0, bz1));
        }

        return rooms;
    }

    private bool DoorInCenterInterval(float doorX, float intervalX0, float intervalX1, float requiredMinWidth)
    {
        float w = intervalX1 - intervalX0;
        if (w < requiredMinWidth) return false;
        return doorX >= intervalX0 && doorX <= intervalX1;
    }

    private float PickInRange(System.Random rng, float min, float max, float hardMin, float hardMax)
    {
        float a = Mathf.Max(min, hardMin);
        float b = Mathf.Max(a, Mathf.Min(max, hardMax));
        double t = rng.NextDouble();
        return Mathf.Lerp(a, b, (float)t);
    }
}