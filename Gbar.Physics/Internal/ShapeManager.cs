using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuUtilities;
using BepuUtilities.Memory;

namespace Gbar.Physics;

/// <summary>
/// 形状构建器（<see cref="PhysicsWorld"/> 内部使用）：
/// 把 <see cref="PhysicsShape"/> 参数解释为 Bepu 形状并注册进 <see cref="Simulation.Shapes"/>，
/// 同时负责 Mesh 三角形缓冲的拷贝（缓冲由构造传入的 <see cref="BufferPool"/> 供养，随 PhysicsWorld 统一回收）。
/// 与刚体生命周期解耦：本类只产出"形状"，不接触 Body/Static。
/// </summary>
internal sealed class ShapeManager
{
    private readonly BufferPool _pool;
    private readonly Simulation _simulation;

    public ShapeManager(BufferPool pool, Simulation simulation)
    {
        _pool = pool;
        _simulation = simulation;
    }

    /// <summary>按形状类型构建 Bepu 形状并注册进 Simulation.Shapes，返回共享形状的索引。</summary>
    public TypedIndex Register(in PhysicsShape shape) => shape.Type switch
    {
        PhysicsShapeType.Box => _simulation.Shapes.Add(new Box(shape.HalfExtents.X * 2f, shape.HalfExtents.Y * 2f, shape.HalfExtents.Z * 2f)),
        PhysicsShapeType.Sphere => _simulation.Shapes.Add(new Sphere(shape.Radius)),
        PhysicsShapeType.Capsule => _simulation.Shapes.Add(new Capsule(shape.Radius, shape.HalfLength)),
        PhysicsShapeType.Mesh => _simulation.Shapes.Add(CreateMesh(in shape)),
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape.Type, "不支持的形状类型。"),
    };

    /// <summary>
    /// 构建 Bepu Mesh：把调用方给的顶点/索引按三角形拷贝进 <see cref="BufferPool"/>，
    /// 返回的 Mesh 由 <see cref="Simulation.Shapes"/> 持有、三角形缓冲由本类持有的 <see cref="BufferPool"/> 供养（Dispose 时统一回收）。
    /// </summary>
    private Mesh CreateMesh(in PhysicsShape shape)
    {
        if (shape.MeshVertices is not { Length: > 0 } vertices)
            throw new ArgumentException("Mesh 形状缺少顶点数据（MeshVertices 为空或长度为 0）。");
        if (shape.MeshIndices is not { Length: > 0 } indices)
            throw new ArgumentException("Mesh 形状缺少索引数据（MeshIndices 为空或长度为 0）。");
        if (indices.Length % 3 != 0)
            throw new ArgumentException($"Mesh 索引长度必须是 3 的倍数（每 3 个一组一个三角形），实际 {indices.Length}。");

        int triangleCount = indices.Length / 3;
        _pool.Take(triangleCount, out Buffer<Triangle> triangles);
        for (int i = 0; i < triangleCount; i++)
        {
            triangles[i] = new Triangle(
                vertices[indices[i * 3]],
                vertices[indices[i * 3 + 1]],
                vertices[indices[i * 3 + 2]]);
        }
        return new Mesh(triangles, Vector3.One, _pool);
    }

    /// <summary>计算形状惯量（仅动态体需要；运动学体质量视为无穷大，惯量字段被 Bepu 忽略）。</summary>
    public static BodyInertia ComputeInertia(in PhysicsShape shape, float mass) => shape.Type switch
    {
        PhysicsShapeType.Box => new Box(shape.HalfExtents.X * 2f, shape.HalfExtents.Y * 2f, shape.HalfExtents.Z * 2f).ComputeInertia(mass),
        PhysicsShapeType.Sphere => new Sphere(shape.Radius).ComputeInertia(mass),
        PhysicsShapeType.Capsule => new Capsule(shape.Radius, shape.HalfLength).ComputeInertia(mass),
        // Mesh 无解析惯量，动态 Mesh 不受支持；请改用 CreateStatic 或盒/球/胶囊近似。
        PhysicsShapeType.Mesh => throw new NotSupportedException("动态 Mesh 刚体不受支持（Bepu Mesh 不提供解析惯量）；请使用 CreateStatic，或改用盒/球/胶囊形状。"),
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape.Type, "不支持的形状类型。"),
    };
}
