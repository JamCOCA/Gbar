using System.Numerics;

namespace Gbar.Physics;

/// <summary>
/// 碰撞形状描述：只携带形状参数，不含位姿/质量。由 <see cref="PhysicsWorld"/> 建体时解释。
/// </summary>
public struct PhysicsShape
{
    #region 判别字段

    /// <summary>形状类型：决定以下各参数字段中哪些有效。</summary>
    public PhysicsShapeType Type;

    #endregion

    #region Box 参数

    /// <summary>盒体半尺寸（Box 用，单位为米）。</summary>
    public Vector3 HalfExtents;

    #endregion

    #region Sphere 参数

    /// <summary>球体半径（Sphere 用，单位为米；Capsule 用表示球帽半径）。</summary>
    public float Radius;

    #endregion

    #region Capsule 参数

    /// <summary>胶囊体半柱长（Capsule 用）：圆柱段沿局部 Y 轴的一半长度，不含两端球帽。总高 = 2 × (HalfLength + Radius)。</summary>
    public float HalfLength;

    #endregion

    #region Mesh 参数

    /// <summary>网格顶点数组（Mesh 用，世界建模坐标，单位米）。所有权归调用方，建体时由 <see cref="PhysicsWorld"/> 拷贝进 Bepu 三角形缓冲。</summary>
    public Vector3[]? MeshVertices;

    /// <summary>网格三角形索引（Mesh 用，每 3 个一组构成一个三角形，值指向 <see cref="MeshVertices"/> 下标）。长度必须是 3 的倍数。</summary>
    public int[]? MeshIndices;

    #endregion

    #region 工厂方法

    /// <summary>创建盒体形状（给定三轴半尺寸）。</summary>
    public static PhysicsShape Box(in Vector3 halfExtents) => new()
    {
        Type = PhysicsShapeType.Box,
        HalfExtents = halfExtents,
    };

    /// <summary>创建盒体形状（给定三轴半尺寸）。</summary>
    public static PhysicsShape Box(float halfX, float halfY, float halfZ) => new()
    {
        Type = PhysicsShapeType.Box,
        HalfExtents = new Vector3(halfX, halfY, halfZ),
    };

    /// <summary>创建球体形状（给定半径）。</summary>
    public static PhysicsShape Sphere(float radius) => new()
    {
        Type = PhysicsShapeType.Sphere,
        Radius = radius,
    };

    /// <summary>创建胶囊体形状（给定球帽半径与半柱长，沿局部 Y 轴对称）。</summary>
    /// <param name="radius">球帽半径（米），决定胶囊的粗细。</param>
    /// <param name="halfLength">圆柱段半长（米），不含球帽；0 时退化为球体。总长（含两帽）= 2 × (halfLength + radius)。</param>
    public static PhysicsShape Capsule(float radius, float halfLength) => new()
    {
        Type = PhysicsShapeType.Capsule,
        Radius = radius,
        HalfLength = halfLength,
    };

    /// <summary>创建三角网格形状（静态几何推荐；动态体请改用凸包/复合形状）。</summary>
    /// <param name="vertices">顶点数组（建模坐标，单位米）。本结构只保存引用，调用方应保证建体期间数组不被修改/回收。</param>
    /// <param name="indices">三角形索引，每 3 个一组，长度必须是 3 的倍数；建议共享顶点以减三角形数。</param>
    public static PhysicsShape Mesh(Vector3[] vertices, int[] indices) => new()
    {
        Type = PhysicsShapeType.Mesh,
        MeshVertices = vertices,
        MeshIndices = indices,
    };

    #endregion
}
