using UnityEngine;

/// <summary> 正交相机与地图平面（Z 固定）上的视野、钳制工具。 </summary>
public static class SokobanOrthographicCameraUtility
{
    public static bool TryGetViewBoundsOnPlane(
        Camera cam,
        float planeZ,
        out float minX,
        out float maxX,
        out float minY,
        out float maxY)
    {
        minX = minY = float.PositiveInfinity;
        maxX = maxY = float.NegativeInfinity;

        if (cam == null || !cam.orthographic)
            return false;

        var plane = new Plane(Vector3.forward, new Vector3(0f, 0f, planeZ));
        var xMin = float.PositiveInfinity;
        var xMax = float.NegativeInfinity;
        var yMin = float.PositiveInfinity;
        var yMax = float.NegativeInfinity;

        void Corner(Vector2 vp)
        {
            var ray = cam.ViewportPointToRay(new Vector3(vp.x, vp.y, 0f));
            if (!plane.Raycast(ray, out var dist))
                return;
            var p = ray.GetPoint(dist);
            xMin = Mathf.Min(xMin, p.x);
            xMax = Mathf.Max(xMax, p.x);
            yMin = Mathf.Min(yMin, p.y);
            yMax = Mathf.Max(yMax, p.y);
        }

        Corner(new Vector2(0f, 0f));
        Corner(new Vector2(1f, 0f));
        Corner(new Vector2(0f, 1f));
        Corner(new Vector2(1f, 1f));

        if (float.IsPositiveInfinity(xMin))
            return false;

        minX = xMin;
        maxX = xMax;
        minY = yMin;
        maxY = yMax;
        return true;
    }

    public static void ClampPositionToWorldBounds(
        Camera cam,
        float boundMinX,
        float boundMaxX,
        float boundMinY,
        float boundMaxY,
        float planeZ)
    {
        if (cam == null || !cam.orthographic)
            return;

        for (var iter = 0; iter < 8; iter++)
        {
            if (!TryGetViewBoundsOnPlane(cam, planeZ, out var vx0, out var vx1, out var vy0, out var vy1))
                return;

            var dx = Compute1DClampDelta(vx0, vx1, boundMinX, boundMaxX);
            var dy = Compute1DClampDelta(vy0, vy1, boundMinY, boundMaxY);
            if (Mathf.Abs(dx) < 1e-5f && Mathf.Abs(dy) < 1e-5f)
                break;

            var p = cam.transform.position;
            cam.transform.position = new Vector3(p.x + dx, p.y + dy, p.z);
        }
    }

    /// <summary> 地图世界包围盒是否大于当前相机视野（含 padding 余量）。 </summary>
    public static bool MapExceedsOrthographicView(Camera cam, Bounds worldBounds, float padding)
    {
        if (cam == null || !cam.orthographic)
            return false;

        var pad = Mathf.Max(0f, padding);
        var halfH = cam.orthographicSize;
        var halfW = halfH * Mathf.Max(0.0001f, cam.aspect);
        var viewW = halfW * 2f;
        var viewH = halfH * 2f;
        return worldBounds.size.x > viewW - pad || worldBounds.size.y > viewH - pad;
    }

    static float Compute1DClampDelta(float vmin, float vmax, float boundMin, float boundMax)
    {
        var span = vmax - vmin;
        var boundSpan = boundMax - boundMin;
        const float eps = 1e-4f;
        if (span > boundSpan + eps)
            return 0.5f * (boundMin + boundMax - vmin - vmax);

        var delta = 0f;
        if (vmin < boundMin)
            delta += boundMin - vmin;
        if (vmax + delta > boundMax)
            delta += boundMax - (vmax + delta);
        return delta;
    }
}
