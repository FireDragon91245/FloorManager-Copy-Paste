namespace FloorManagerCopyPaste.Models;

/// <summary>JSON-friendly Vector3 replacement.</summary>
internal readonly struct Vec3
{
    // These must be public: System.Text.Json ignores private properties by default.
    // Keeping them private caused persisted device poses and cable paths to be emitted
    // as {}, which turned every loaded vector into (0, 0, 0).
    public float X { get; init; }
    public float Y { get; init; }
    public float Z { get; init; }

    public static Vec3 From(UnityEngine.Vector3 v) => new() { X = v.x, Y = v.y, Z = v.z };
    public UnityEngine.Vector3 ToUnity() => new(X, Y, Z);
}
