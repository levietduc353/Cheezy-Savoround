using UnityEngine;

/// <summary>
/// Root wrapper deserialized from grid_config.json.
/// Contains all grid definitions in the project.
/// </summary>
[System.Serializable]
public class GridConfigCollection
{
    public GridConfigData[] grids;
}

/// <summary>
/// Data model for a single grid, mapped from JSON.
/// orientation: "portrait"  → rows along X, cols along Z (tall)
///              "landscape" → cols along X, rows along Z (wide)
/// </summary>
[System.Serializable]
public class GridConfigData
{
    public string id;
    public int rows;
    public int columns;
    public float cellSize;
    public float cellSpacing;
    /// <summary>"portrait" or "landscape"</summary>
    public string orientation;
    public Vector3Data gridOriginOffset;
}

/// <summary>
/// Simple serializable Vector3 because Unity's Vector3 is not JSON-serializable by default.
/// </summary>
[System.Serializable]
public class Vector3Data
{
    public float x;
    public float y;
    public float z;

    public Vector3 ToVector3() => new Vector3(x, y, z);
}
