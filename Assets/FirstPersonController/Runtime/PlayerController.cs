﻿using System;
using UnityEngine;

namespace FirstPersonController
{
    // NOTE: This code is all very rough and is just used to get
    // the basic features in place. I intend to do a lot of cleanup
    // in here, hence the very messy naming and organization.
    [RequireComponent(typeof(CapsuleBody))]
    public sealed class PlayerController : MonoBehaviour
    {
        private enum State
        {
            Walking,
            Running,
            Crouching,
            Sliding,
        }

        private Transform _transform;
        private IPlayerControllerInput _input;
        private CapsuleBody _body;

        private State _state;
        private State _nextState;

        private bool _grounded;
        private RaycastHit _lastGroundHit;

        private float _verticalVelocity;

        // Control velocity based on movement input and the ground normal
        private Vector3 _controlVelocity;

        // Final computed velocity carried between frames for acceleration
        private Vector3 _velocity;

        private float _targetEyeHeight;

        [SerializeField]
        public float acceleration = 2f;

        [SerializeField]
        public float airDrag = 0.2f;

        [SerializeField]
        public float airControl = 20f;

        [SerializeField]
        private float _defaultColliderHeight = 1.7f;

        [SerializeField]
        private float _defaultEyeHeight = 1.6f;

        [SerializeField]
        private float _crouchColliderHeight = 0.9f;

        [SerializeField]
        private float _crouchEyeHeight = 0.8f;

        [SerializeField]
        private float _eyeHeightAnimationSpeed = 10f;

        [SerializeField]
        private Transform _eyeHeightTransform = default;

        [SerializeField]
        private float _cameraCollisionRadius = 0.2f;

        public float jumpHeight = 1.5f;

        [SerializeField]
        private Transform _leanTransform = default;

        [SerializeField]
        private float _leanDistanceX = 0.65f;

        [SerializeField]
        private float _leanDistanceY = -.05f;

        [SerializeField]
        private float _leanAngle = 10f;

        [SerializeField]
        private float _leanAnimationSpeed = 10f;

        [Serializable]
        public class SlideConfig
        {
            public float speedRequiredToSlide = 3.5f;
            public float colliderHeight = 0.9f;
            public float eyeHeight = 0.8f;
            public float groundFriction = 0.8f;
            public PhysicMaterialCombine groundFrictionCombine = PhysicMaterialCombine.Multiply;
            public float speedThresholdToExit = 0.8f;
        }

        [SerializeField]
        private SlideConfig _sliding = default;

        public PlayerSpeed walkSpeed = new PlayerSpeed(2f, 1f, 0.95f);
        public PlayerSpeed runSpeed = new PlayerSpeed(4.5f, 0.9f, 0.6f);
        public PlayerSpeed crouchSpeed = new PlayerSpeed(0.8f, 1f, 1f);

        public float speed => _velocity.magnitude;

        public void ResetHeight()
        {
            ChangeHeight(_defaultColliderHeight, _defaultEyeHeight);
        }

        public void ChangeHeight(float colliderHeight, float eyeHeight)
        {
            _body.height = colliderHeight;
            _targetEyeHeight = eyeHeight;
        }

        public bool CanStandUp()
        {
            return !_body.WouldCapsuleBeColliding(
                _body.position,
                _defaultColliderHeight
            );
        }

        private void Start()
        {
            ResetHeight();

            _state = State.Walking;
            _nextState = State.Walking;
        }

        private void OnEnable()
        {
            _transform = transform;
            _input = GetComponent<IPlayerControllerInput>();
            _body = GetComponent<CapsuleBody>();
        }

        private void ChangeState(State nextState)
        {
            _nextState = nextState;
        }

        private void ApplyStateChange()
        {
            if (_nextState != _state)
            {
                Debug.Log($"State Change: {_state} -> {_nextState}");

                _state = _nextState;

                switch (_state)
                {
                    case State.Crouching:
                        ChangeHeight(_crouchColliderHeight, _crouchEyeHeight);
                        break;
                    case State.Sliding:
                        ChangeHeight(_sliding.colliderHeight, _sliding.eyeHeight);
                        break;
                    default:
                        ResetHeight();
                        break;
                }
            }
        }

        private void FixedUpdate()
        {
            ApplyStateChange();
            CheckForGround();
            AddGravity();

            switch (_state)
            {
                case State.Walking: UpdateWalking(); break;
                case State.Running: UpdateRunning(); break;
                case State.Crouching: UpdateCrouching(); break;
                case State.Sliding: UpdateSliding(); break;
            }

            ApplyVelocityToBody();
            AdjustEyeHeight();
            ApplyLean();
        }

        private void AddGravity()
        {
            _verticalVelocity += Physics.gravity.y * Time.deltaTime;
        }

        private void UpdateCrouching()
        {
            TryJump();
            ApplyUserInputMovement();
            if (!_input.crouch && CanStandUp())
            {
                if (_input.run)
                {
                    ChangeState(State.Running);
                }
                else
                {
                    ChangeState(State.Walking);
                }
            }
        }

        private void UpdateRunning()
        {
            TryJump();
            ApplyUserInputMovement();
            if (_input.crouch)
            {
                Debug.Log($"Speed: {speed} {_sliding.speedRequiredToSlide}");
                if (speed >= _sliding.speedRequiredToSlide)
                {
                    ChangeState(State.Sliding);
                }
                else
                {
                    ChangeState(State.Crouching);
                }
            }
            else if (!_input.run)
            {
                ChangeState(State.Walking);
            }
        }

        private void UpdateWalking()
        {
            TryJump();
            ApplyUserInputMovement();
            if (_input.run)
            {
                ChangeState(State.Running);
            }
            else if (_input.crouch)
            {
                ChangeState(State.Crouching);
            }
        }

        private void ApplyVelocityToBody()
        {
            _velocity = _controlVelocity;
            _velocity.y += _verticalVelocity;
            _velocity = _body.MoveWithVelocity(_velocity);
        }

        private void TryJump()
        {
            if (_grounded && _input.jump)
            {
                _verticalVelocity = Mathf.Sqrt(2f * jumpHeight * -Physics.gravity.y);
                _grounded = false;
                ChangeState(State.Walking);
            }
        }

        private void UpdateSliding()
        {
            TryJump();

            if (_grounded)
            {
                // Add gravity projected onto the ground plane for acceleration
                var gravity = Physics.gravity;
                var ground = _lastGroundHit.normal;
                _controlVelocity += (gravity - ground * Vector3.Dot(gravity, ground)) * Time.deltaTime;
                _controlVelocity = _body.ApplyGroundFrictionToVelocity(
                    _controlVelocity, 
                    _sliding.groundFrictionCombine, 
                    _sliding.groundFriction
                );
            }
            else
            {
                ApplyAirDrag();
            }

            if (speed <= _sliding.speedThresholdToExit)
            {
                if (_input.run && CanStandUp())
                {
                    ChangeState(State.Running);
                }
                else
                {
                    ChangeState(State.Crouching);
                }
            }
        }

        private void ApplyUserInputMovement()
        {
            var movementRotation = Quaternion.Euler(0, _transform.eulerAngles.y, 0);
            if (_grounded)
            {
                var groundOrientation = Quaternion.FromToRotation(
                    Vector3.up,
                    _lastGroundHit.normal
                );
                movementRotation = groundOrientation * movementRotation;
            }

            var moveInput = _input.moveInput;
            var moveVelocity = movementRotation * new Vector3(moveInput.x, 0, moveInput.y);

            PlayerSpeed speed = walkSpeed;
            switch (_state)
            {
                case State.Running:
                    speed = runSpeed;
                    break;
                case State.Crouching:
                    speed = crouchSpeed;
                    break;
            }

            var targetSpeed = Mathf.Lerp(
                _controlVelocity.magnitude,
                speed.TargetSpeed(moveInput),
                acceleration * Time.deltaTime
            );
            moveVelocity *= targetSpeed;

            if (_grounded)
            {
                // 100% control on ground
                _controlVelocity = moveVelocity;
            }
            else
            {
                if (moveVelocity.sqrMagnitude > 0)
                {
                    moveVelocity = Vector3.ProjectOnPlane(moveVelocity, Vector3.up);
                    _controlVelocity = Vector3.Lerp(
                        _controlVelocity,
                        moveVelocity,
                        airControl * Time.deltaTime
                    );
                }

                ApplyAirDrag();
            }
        }

        private void ApplyAirDrag()
        {
            _controlVelocity *= (1f / (1f + (airDrag * Time.fixedDeltaTime)));
        }

        private void CheckForGround()
        {
            var hitGround = _body.CheckForGround(
                _grounded,
                out _lastGroundHit,
                out var verticalMovementApplied
            );

            // Whenever we hit ground we adjust our eye local coordinate space
            // in the opposite direction of the ground code so that our eyes
            // stay at the same world position and then interpolate where they
            // should be in AdjustEyeHeight later on.
            if (hitGround)
            {
                var eyeLocalPos = _eyeHeightTransform.localPosition;
                eyeLocalPos.y -= verticalMovementApplied;
                _eyeHeightTransform.localPosition = eyeLocalPos;
            }

            // Only grounded if the body detected ground AND we're not moving upwards
            var groundedNow = hitGround && _verticalVelocity <= 0;
            var wasGrounded = _grounded;

            _grounded = groundedNow;

            if (!wasGrounded && groundedNow)
            {
                // TODO: OnBeginGrounded event
            }

            if (groundedNow)
            {
                _verticalVelocity = 0;

                // Reproject our control velocity onto the ground plane without losing magnitude
                var groundNormal = _lastGroundHit.normal;
                _controlVelocity = (_controlVelocity - groundNormal * Vector3.Dot(_controlVelocity, groundNormal)).normalized * _controlVelocity.magnitude;
            }
        }

        private void AdjustEyeHeight()
        {
            var eyeLocalPos = _eyeHeightTransform.localPosition;
            var oldHeight = eyeLocalPos.y;

            // If we want to raise the eyes we check to see if there's something
            // above us. If we hit something, we clamp our eye position. Once
            // we're out from under whatever it is then we will fall through and
            // animate standing up.
            bool didCollide = Physics.SphereCast(
                new Ray(_body.position, Vector3.up),
                _cameraCollisionRadius,
                out var hit,
                _targetEyeHeight,
                ~(1 << gameObject.layer)
            );

            if (didCollide && oldHeight > hit.distance)
            {
                eyeLocalPos.y = hit.distance;
            }
            else
            {
                var remainingDistance = Mathf.Abs(oldHeight - _targetEyeHeight);
                if (remainingDistance < 0.01f)
                {
                    eyeLocalPos.y = _targetEyeHeight;
                }
                else
                {
                    // There's probably a better animation plan here than simple
                    // Lerp but for now it's reasonable.
                    eyeLocalPos.y = Mathf.Lerp(
                        oldHeight,
                        _targetEyeHeight,
                        _eyeHeightAnimationSpeed * Time.deltaTime
                    );
                }
            }

            _eyeHeightTransform.localPosition = eyeLocalPos;

            // If we're in the air we adjust the body relative to the eye height
            // to simulate raising the legs up. Otherwise crouching/uncrouching
            // midair feels super awkward.
            if (!_grounded)
            {
                _body.Translate(new Vector3(0, oldHeight - eyeLocalPos.y, 0));
            }
        }

        private void ApplyLean()
        {
            var amount = _input.lean;

            var eyeLocalRot = _leanTransform.localEulerAngles;
            var desiredEyeRotThisFrame = Mathf.LerpAngle(
                eyeLocalRot.z,
                -amount * _leanAngle,
                _leanAnimationSpeed * Time.deltaTime
            );

            var targetEyeLocalPos = new Vector3(
                amount * _leanDistanceX,
                Mathf.Abs(amount) * _leanDistanceY,
                0
            );
            var desiredEyePosThisFrame = Vector3.Lerp(
                _leanTransform.localPosition,
                targetEyeLocalPos,
                _leanAnimationSpeed * Time.deltaTime
            );

            if (amount != 0)
            {
                var ray = new Ray(
                    _leanTransform.parent.position,
                    transform.TransformDirection(targetEyeLocalPos.normalized)
                );

                var didHit = Physics.SphereCast(
                    ray,
                    _cameraCollisionRadius,
                    out var hit,
                    targetEyeLocalPos.magnitude,
                    ~(1 << gameObject.layer)
                );

                if (didHit && desiredEyePosThisFrame.sqrMagnitude > (hit.distance * hit.distance))
                {
                    desiredEyePosThisFrame = _leanTransform.parent.InverseTransformPoint(
                        ray.origin + ray.direction * hit.distance
                    );

                    // Scale rotation to be the same percentage as our distance
                    desiredEyeRotThisFrame = Mathf.LerpAngle(
                        eyeLocalRot.z,
                        -amount * _leanAngle * (hit.distance / targetEyeLocalPos.magnitude),
                        _leanAnimationSpeed * Time.deltaTime
                    );
                }
            }
            else
            {
                desiredEyePosThisFrame = Vector3.Lerp(
                    _leanTransform.localPosition,
                    Vector3.zero,
                    _leanAnimationSpeed * Time.deltaTime
                );
            }

            _leanTransform.localPosition = desiredEyePosThisFrame;

            eyeLocalRot.z = desiredEyeRotThisFrame;
            _leanTransform.localEulerAngles = eyeLocalRot;
        }

        private void OnGUI()
        {
            GUI.Label(new Rect(10, 10, 0, 0), new GUIContent($"State: {_state}"));
        }
    }
}
