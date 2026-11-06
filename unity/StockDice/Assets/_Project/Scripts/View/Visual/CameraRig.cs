using UnityEngine;
#if ENABLE_INPUT_SYSTEM && STOCKDICE_INPUTSYSTEM
using UnityEngine.InputSystem;
#endif

namespace StockDice.View
{
    /// <summary>
    /// 부드럽게 따라가는 카메라. 토큰이 있는 변(side)에 맞춰 방향을 돌려서 칸 이름이 항상 바로 읽히게 한다.
    /// 흔들림(Shake)과 마우스 휠 줌을 지원한다.
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public Camera Cam { get; private set; }

        public float Pitch = 50f;
        public float FollowDistance = 19f;
        public float OverviewDistance = 31f;

        private Vector3 _target;
        private Vector3 _targetGoal;
        private Vector3 _targetVel;
        private float _yaw;
        private float _yawGoal;
        private float _yawVel;
        private float _dist = 31f;
        private float _distGoal = 31f;
        private float _distVel;
        private float _userZoom = 1f;
        private float _shakeTime;
        private float _shakeDuration;
        private float _shakeAmp;
        private bool _orbit;
        private float _smooth = 0.35f;

        public static CameraRig Create()
        {
            var go = new GameObject("CameraRig");
            var rig = go.AddComponent<CameraRig>();
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(go.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 38f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Palette.Background;
            camGo.AddComponent<AudioListener>();
            rig.Cam = cam;
            rig.SnapTo(Vector3.zero, 0f, rig.OverviewDistance);
            return rig;
        }

        public void SnapTo(Vector3 target, float yaw, float distance)
        {
            _target = _targetGoal = target;
            _yaw = _yawGoal = yaw;
            _dist = _distGoal = distance;
            _targetVel = Vector3.zero;
            _yawVel = 0f;
            _distVel = 0f;
            Apply(Vector3.zero);
        }

        /// <summary>대상 위치로 부드럽게 이동. side 가 0 이상이면 그 변을 정면으로 본다.</summary>
        public void Focus(Vector3 target, int side = -1, float distance = -1f, float smooth = 0.35f)
        {
            _orbit = false;
            _targetGoal = target;
            if (side >= 0) _yawGoal = side * 90f;
            _distGoal = distance > 0f ? distance : FollowDistance;
            _smooth = smooth;
        }

        public void Overview(float smooth = 0.6f)
        {
            _orbit = false;
            _targetGoal = Vector3.zero;
            _distGoal = OverviewDistance;
            _smooth = smooth;
        }

        /// <summary>시작 화면용: 보드 중앙을 천천히 돈다.</summary>
        public void Orbit(bool enabled)
        {
            _orbit = enabled;
            if (enabled)
            {
                _targetGoal = Vector3.zero;
                _distGoal = OverviewDistance + 2f;
                _smooth = 1f;
            }
        }

        public void Shake(float amplitude, float duration)
        {
            float remaining = _shakeTime < _shakeDuration ? _shakeAmp * (1f - _shakeTime / _shakeDuration) : 0f;
            if (amplitude >= remaining)
            {
                _shakeAmp = amplitude;
                _shakeDuration = duration;
                _shakeTime = 0f;
            }
        }

        private void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            float scroll = ReadScroll();
            if (Mathf.Abs(scroll) > 0.01f) _userZoom = Mathf.Clamp(_userZoom - scroll * 0.08f, 0.6f, 1.5f);

            if (_orbit) _yawGoal += dt * 6f;

            _target = Vector3.SmoothDamp(_target, _targetGoal, ref _targetVel, _smooth, Mathf.Infinity, dt);
            _yaw = Mathf.SmoothDampAngle(_yaw, _yawGoal, ref _yawVel, _smooth * 1.4f, Mathf.Infinity, dt);
            _dist = Mathf.SmoothDamp(_dist, _distGoal, ref _distVel, _smooth * 1.2f, Mathf.Infinity, dt);

            Vector3 shake = Vector3.zero;
            if (_shakeTime < _shakeDuration)
            {
                _shakeTime += dt;
                float k = 1f - Mathf.Clamp01(_shakeTime / _shakeDuration);
                float t = Time.unscaledTime * 38f;
                shake = new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, t) - 0.5f, Mathf.PerlinNoise(t, t * 0.5f) - 0.5f) * (_shakeAmp * k * 2f);
            }
            Apply(shake);
        }

        private void Apply(Vector3 shake)
        {
            if (Cam == null) return;
            var rot = Quaternion.Euler(Pitch, _yaw, 0f);
            Vector3 pos = _target + rot * new Vector3(0f, 0f, -_dist * _userZoom);
            Cam.transform.position = pos + shake;
            Cam.transform.rotation = rot;
        }

        private static float ReadScroll()
        {
#if ENABLE_INPUT_SYSTEM && STOCKDICE_INPUTSYSTEM
            var mouse = Mouse.current;
            return mouse != null ? mouse.scroll.ReadValue().y / 120f : 0f;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.mouseScrollDelta.y;
#else
            return 0f;
#endif
        }
    }
}
