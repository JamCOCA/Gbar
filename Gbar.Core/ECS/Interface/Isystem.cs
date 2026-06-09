using Arch.Core;

namespace Gbar.Core.ECS;

public interface ISystem
{
    void Start(World world);
    void Update(float deltaTime);
    void Destroy();
}
