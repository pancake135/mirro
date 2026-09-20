using UnityEngine;

namespace Mirro.Core
{
    /// <summary>깃발 뽑기, 아이템 습득 등 플레이어의 상호작용 대상이 구현하는 인터페이스.</summary>
    public interface IInteractable
    {
        void Interact(GameObject instigator);
    }
}
