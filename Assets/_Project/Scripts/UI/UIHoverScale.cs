using UnityEngine;
using UnityEngine.EventSystems;

namespace Mirro.UI
{
    /// <summary>포인터가 올라가면 부드럽게 커지는 호버 효과. 자식 그래픽 위의 포인터도 부모로 전달되어 동작한다.</summary>
    public class UIHoverScale : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public float hoverScale = 1.06f;
        public float speed = 14f;

        private Vector3 _baseScale = Vector3.one;
        private float _factor = 1f;
        private float _target = 1f;

        private void Awake() => _baseScale = transform.localScale;

        private void OnEnable()
        {
            _factor = _target = 1f;
            transform.localScale = _baseScale;
        }

        private void Update()
        {
            _factor = Mathf.Lerp(_factor, _target, 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime));
            transform.localScale = _baseScale * _factor;
        }

        public void OnPointerEnter(PointerEventData eventData) => _target = hoverScale;

        public void OnPointerExit(PointerEventData eventData) => _target = 1f;
    }
}
