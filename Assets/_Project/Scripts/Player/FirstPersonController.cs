using UnityEngine;
using UnityEngine.InputSystem;
using Mirro.Core;

namespace Mirro.Player
{
    /// <summary>
    /// 새 Input System 기반 1인칭 컨트롤러 (WASD 이동, 마우스 시점, Shift 스프린트,
    /// Space 점프, E 상호작용). 커서 잠금/해제와 Esc 일시정지는 PauseMenu가 담당한다.
    /// Keyboard/Mouse 디바이스를 직접 폴링하므로 별도 InputAction 에셋이 필요 없다.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonController : MonoBehaviour
    {
        [Header("Move")]
        public float moveSpeed = 5f;
        public float sprintSpeed = 8f;
        public float gravity = -20f;
        public float jumpHeight = 1.2f;

        [Header("Look")]
        public float lookSensitivity = 0.1f;
        public float minPitch = -85f;
        public float maxPitch = 85f;

        [Header("Interact")]
        public float interactRange = 2.5f;
        public LayerMask interactableMask = ~0;

        public Camera playerCamera;

        /// <summary>번개/칼 아이템 등에 의해 true가 되면 이동/시점/상호작용 입력이 무시된다.</summary>
        public bool IsStunned { get; set; }

        /// <summary>일시정지 메뉴가 열려 있는 동안 true. 입력만 막을 뿐 월드는 계속 진행된다.</summary>
        public bool IsPaused { get; set; }

        private CharacterController _controller;
        private float _verticalVelocity;
        private float _pitch;
        private bool _cursorLocked;

        /// <summary>현재 상하 시선 각도(도). 아래를 볼수록 커진다. 관전자가 이 사람의 시선을 그대로 보도록 네트워크로 알린다.</summary>
        public float Pitch => _pitch;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            // 기본값(0.001)보다 프레임당 중력 이동량이 작아지는 고프레임에서 바닥 판정이 풀려 점프가 막힌다.
            _controller.minMoveDistance = 0f;
            if (playerCamera == null)
                playerCamera = GetComponentInChildren<Camera>();
        }

        private void Start()
        {
            SetCursorLock(true);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard == null || mouse == null) return;

            if (IsStunned || IsPaused) return;

            HandleLook(mouse);
            HandleMove(keyboard);
            HandleInteract(keyboard);
        }

        public void SetCursorLock(bool locked)
        {
            _cursorLocked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void HandleLook(Mouse mouse)
        {
            if (!_cursorLocked || playerCamera == null) return;

            Vector2 delta = mouse.delta.ReadValue() * lookSensitivity;

            transform.Rotate(Vector3.up * delta.x);

            _pitch = Mathf.Clamp(_pitch - delta.y, minPitch, maxPitch);
            playerCamera.transform.localEulerAngles = new Vector3(_pitch, 0f, 0f);
        }

        private void HandleMove(Keyboard keyboard)
        {
            float h = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            float v = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
            bool sprinting = keyboard.leftShiftKey.isPressed;

            Vector3 move = transform.right * h + transform.forward * v;
            if (move.sqrMagnitude > 1f) move.Normalize();

            float speed = sprinting ? sprintSpeed : moveSpeed;

            if (_controller.isGrounded)
            {
                _verticalVelocity = -1f;
                if (keyboard.spaceKey.wasPressedThisFrame)
                    _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }
            _verticalVelocity += gravity * Time.deltaTime;

            Vector3 velocity = move * speed + Vector3.up * _verticalVelocity;
            _controller.Move(velocity * Time.deltaTime);
        }

        private void HandleInteract(Keyboard keyboard)
        {
            if (!keyboard.eKey.wasPressedThisFrame || playerCamera == null) return;

            if (Physics.Raycast(playerCamera.transform.position, playerCamera.transform.forward,
                    out var hit, interactRange, interactableMask, QueryTriggerInteraction.Collide))
            {
                var interactable = hit.collider.GetComponentInParent<IInteractable>();
                interactable?.Interact(gameObject);
            }
        }
    }
}
