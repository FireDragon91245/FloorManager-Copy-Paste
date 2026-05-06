namespace FloorManagerCopyPaste.Models;

/// <summary>JSON-friendly Vector3 replacement.</summary>
internal readonly struct Vec3
{
    private float X { get; init; }
    private float Y { get; init; }
    private float Z { get; init; }

    public static Vec3 From(UnityEngine.Vector3 v) => new() { X = v.x, Y = v.y, Z = v.z };
    public UnityEngine.Vector3 ToUnity() => new(X, Y, Z);
}