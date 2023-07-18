﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Animations.Rigging;
#if ENABLE_INPUT_SYSTEM && STARTER_ASSETS_PACKAGES_CHECKED
using UnityEngine.InputSystem;
using UnityEngine.Windows;
#endif

/* Note: animations are called via the controller for both the character and capsule using animator null checks
 */

namespace StarterAssets
{
    [RequireComponent(typeof(CharacterController))]
#if ENABLE_INPUT_SYSTEM && STARTER_ASSETS_PACKAGES_CHECKED
    [RequireComponent(typeof(PlayerInput))]
#endif
    public class ThirdPersonController : MonoBehaviour
    {
        [Header("Player")]
        [Tooltip("Move speed of the character in m/s")]
        public float MoveSpeed = 2.0f;

        [Tooltip("Sprint speed of the character in m/s")]
        public float SprintSpeed = 5.335f;

        [Tooltip("How fast the character turns to face movement direction")]
        [Range(0.0f, 0.3f)]
        public float RotationSmoothTime = 0.12f;

        [Tooltip("Acceleration and deceleration")]
        public float SpeedChangeRate = 10.0f;

        public AudioClip LandingAudioClip;
        public AudioClip[] FootstepAudioClips;
        [Range(0, 1)] public float FootstepAudioVolume = 0.5f;

        [Space(10)]
        [Tooltip("The height the player can jump")]
        public float JumpHeight = 1.2f;

        [Space(10)]
        [Tooltip("The height the player can flip jump")]
        public float FlipJumpHeight = 1.4f;

        [Tooltip("The max absolute value that the characters y-velocity can have before the FlipJumpApex is triggered")]
        public float FlipJumpApexBoundVelocityMagnitude = 0.2f;

        [Tooltip("Hight of the capsule when the characters is in the appex of a flip-jump")]
        public float FlipJumpCapsuleHeight;

        [Tooltip("The character uses its own gravity value. The engine default is -9.81f")]
        public float Gravity = -15.0f;

        [Space(10)]
        [Tooltip("Time required to pass before being able to jump again. Set to 0f to instantly jump again")]
        public float JumpTimeout = 0.50f;

        [Tooltip("Time required to pass before entering the fall state. Useful for walking down stairs")]
        public float FallTimeout = 0.15f;

        [Header("Player Grounded")]
        [Tooltip("If the character is grounded or not. Not part of the CharacterController built in grounded check")]
        public bool Grounded = true;


        [Tooltip("Height of the character when crouched. The center of the player is automatically set to half the height when crouched")]
        public float CrouchHeight;

        [Tooltip("Useful for rough ground")]
        public float GroundedOffset = -0.14f;

        [Tooltip("The radius of the grounded check. Should match the radius of the CharacterController")]
        public float GroundedRadius = 0.28f;

        [Tooltip("What layers the character uses as ground")]
        public LayerMask GroundLayers;

        [Header("Cinemachine")]
        [Tooltip("The follow target set in the Cinemachine Virtual Camera that the camera will follow")]
        public GameObject CinemachineCameraTarget;

        [Tooltip("How far in degrees can you move the camera up")]
        public float TopClamp = 70.0f;

        [Tooltip("How far in degrees can you move the camera down")]
        public float BottomClamp = -30.0f;

        [Tooltip("Additional degress to override the camera. Useful for fine tuning camera position when locked")]
        public float CameraAngleOverride = 0.0f;

        [Tooltip("For locking the camera position on all axis")]
        public bool LockCameraPosition = false;

        [Tooltip("Transform of the character's right shoulder")]
        public Transform rightShoulder;

        [Tooltip("Transform of the character's left shoulder")]
        public Transform leftShoulder;

        [SerializeField] private Weapon _leftHand;
        [SerializeField] private Weapon _rightHand;
        [SerializeField] private Weapon _leftFoot;
        [SerializeField] private Weapon _rightFoot;

       


        // attack
        public float punchRange = 3f;
        public float punchRadius = 2.5f;
        public float punchDmg = 10f;

        [Header("Slide")]
        public float slideDuration = 1f;
        public float slideResetDuration = 2f;
        public float slideSpeed = 10f;
        public float slideHeight;

        [Header("Roll")]
        public float rollSpeed = 10f;
        public float rollDuration = .5f;
        public float rollResetDuration = 2f;
        public float rollHeight;
        

        // cinemachine
        private float _cinemachineTargetYaw;
        private float _cinemachineTargetPitch;

        // player
        private float _speed;
        private float _animationBlend;
        private float _targetRotation = 0.0f;
        private float _rotationVelocity;
        private float _verticalVelocity;
        private float _terminalVelocity = 53.0f;
        private float _standingHeight;
        private Vector3 _standingCenter;

        // timeout deltatime
        private float _jumpTimeoutDelta;
        private float _fallTimeoutDelta;

        // animation IDs
        private int _animIDSpeed;
        private int _animIDGrounded;
        private int _animIDJump;
        private int _animIDFreeFall;
        private int _animIDMotionSpeed;
        private int _animIDRightPunchTrigger;
        private int _animIDLeftPunchTrigger;
        private int _animIDRightKickTrigger;
        private int _animIDLeftKickTrigger;
        private int _animIDHitTrigger;
        private int _animIDCrouch;
        private int _animIDSlide;
        private int _animIDFlip;
        private int _animIDRoll;

        private int _animStateIDIdle;

        public Vector3 WatchTargetVelocity;


#if ENABLE_INPUT_SYSTEM && STARTER_ASSETS_PACKAGES_CHECKED
        private PlayerInput _playerInput;
#endif
        private Animator _animator;
        private CharacterController _controller;
        private StarterAssetsInputs _input;
        private GameObject _mainCamera;
        
        
        private const float _threshold = 0.01f;

        private bool _hasAnimator;

        private bool _punchRight;
        private bool _punchLeft;
        private bool _previouslyAttacking;

        private bool _tryToCrouch;
        private bool _tryToSlide;
        private float _slideTimer;
        private float _slideResetTimer;

        private bool _tryToRoll;
        private float _rollTimer;
        private float _rollResetTimer;
        private bool _previouslyGrounded;
        [SerializeField] private float _timeToFlipJump;

        [Header ("Lock On")]
        [SerializeField] private GameObject LockOnTarget;
        [SerializeField, Range(0f, 1f)] private float LockOnStrength;
        private bool _lockOn;
        [SerializeField] private bool _aimAssist = true;
        [SerializeField, Range(0f, 1f)] private float _aimAssistStrength = .8f;
        [SerializeField] private float _lockOnTargetCloseDistance;


        private bool IsCurrentDeviceMouse
        {
            get
            {
#if ENABLE_INPUT_SYSTEM && STARTER_ASSETS_PACKAGES_CHECKED
                return _playerInput.currentControlScheme == "KeyboardMouse";
#else
				return false;
#endif
            }
        }


        private void Awake()
        {
            // get a reference to our main camera
            if (_mainCamera == null)
            {
                _mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
            }

            _rightHand.OnWeaponCollision += PunchHit;
            _leftHand.OnWeaponCollision += PunchHit;
            _rightFoot.OnWeaponCollision += PunchHit;
            _leftFoot.OnWeaponCollision += PunchHit;
        }

        private void Start()
        {
            _cinemachineTargetYaw = CinemachineCameraTarget.transform.rotation.eulerAngles.y;
            
            _hasAnimator = TryGetComponent(out _animator);
            _controller = GetComponent<CharacterController>();
            _input = GetComponent<StarterAssetsInputs>();
#if ENABLE_INPUT_SYSTEM && STARTER_ASSETS_PACKAGES_CHECKED
            _playerInput = GetComponent<PlayerInput>();
#else
			Debug.LogError( "Starter Assets package is missing dependencies. Please use Tools/Starter Assets/Reinstall Dependencies to fix it");
#endif

            AssignAnimationIDs();

            // reset our timeouts on start
            _jumpTimeoutDelta = JumpTimeout;
            _fallTimeoutDelta = FallTimeout;
            _slideResetTimer = slideResetDuration;
            _rollResetTimer = rollResetDuration;

            _standingCenter = _controller.center;
            _standingHeight = _controller.height; 
        }

        private void Update()
        {
            _hasAnimator = TryGetComponent(out _animator);

            Crouch();
            JumpAndGravity();
            GroundedCheck();
            Move();
            Attack();
            SetPreviously();
        }

        private void SetPreviously()
        {
            _previouslyAttacking = IsAttacking();
            _previouslyGrounded = Grounded;
        }

        private void Crouch()
        {
            bool currCrouch = _tryToCrouch;
            _tryToCrouch = _input.crouch;
            if (_hasAnimator)
            {
                _animator.SetBool(_animIDCrouch, _tryToCrouch);
            }
            if (_input.crouch && !_tryToSlide)
            {
                // Crouch
                _controller.height = CrouchHeight;
                _controller.center = new Vector3(0, CrouchHeight / 2, 0);
            } else if(currCrouch != _tryToCrouch)
            {
                // Uncrouch
                UncrouchCollider();
            }
        }

        private void UncrouchCollider()
        {
            _controller.height = _standingHeight;
            _controller.center = _standingCenter;
        }

        private void LateUpdate()
        {
            CameraRotation();
        }

        private void AssignAnimationIDs()
        {
            _animIDSpeed = Animator.StringToHash("Speed");
            _animIDGrounded = Animator.StringToHash("Grounded");
            _animIDJump = Animator.StringToHash("Jump");
            _animIDFreeFall = Animator.StringToHash("FreeFall");
            _animIDMotionSpeed = Animator.StringToHash("MotionSpeed");
            _animIDRightPunchTrigger = Animator.StringToHash("PunchRightTrigger");
            _animIDLeftPunchTrigger = Animator.StringToHash("PunchLeftTrigger");
            _animIDRightKickTrigger = Animator.StringToHash("KickRightTrigger");
            _animIDLeftKickTrigger = Animator.StringToHash("KickLeftTrigger");
            _animIDCrouch = Animator.StringToHash("Crouch");
            _animIDSlide = Animator.StringToHash("Slide");
            _animIDFlip = Animator.StringToHash("Flip");
            _animIDRoll = Animator.StringToHash("Roll");

            _animStateIDIdle = Animator.StringToHash("Idle Walk Run Blend");
        }

        private void GroundedCheck()
        {
            // set sphere position, with offset
            Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset,
                transform.position.z);
            Grounded = Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers,
                QueryTriggerInteraction.Ignore);

            // update animator if using character
            if (_hasAnimator)
            {
                _animator.SetBool(_animIDGrounded, Grounded);
            }
        }

        private void CameraRotation()
        {
            // if there is an input and camera position is not fixed
            if (_input.look.sqrMagnitude >= _threshold && !LockCameraPosition)
            {
                //Don't multiply mouse input by Time.deltaTime;
                float deltaTimeMultiplier = IsCurrentDeviceMouse ? 1.0f : Time.deltaTime;

                _cinemachineTargetYaw += _input.look.x * deltaTimeMultiplier;
                _cinemachineTargetPitch += _input.look.y * deltaTimeMultiplier;
            }

            // clamp our rotations so our values are limited 360 degrees
            _cinemachineTargetYaw = ClampAngle(_cinemachineTargetYaw, float.MinValue, float.MaxValue);
            _cinemachineTargetPitch = ClampAngle(_cinemachineTargetPitch, BottomClamp, TopClamp);

            // Cinemachine will follow this target
            CinemachineCameraTarget.transform.rotation = Quaternion.Euler(_cinemachineTargetPitch + CameraAngleOverride,
                _cinemachineTargetYaw, 0.0f);
        }

        private void Attack()
        {

            if((_input.punchRight || _input.punchLeft) && _lockOn)
            {
                LockOnAttack();
            }

            //TODO: try anim canceling instead of queueing attacks
            if (_input.punchRight)
            {
                int trigger = _input.isModified ? _animIDRightKickTrigger : _animIDRightPunchTrigger;

                if (_hasAnimator)
                {
                    _animator.SetTrigger(trigger);
                    _animator.SetBool(_animIDCrouch, false);
                    _animator.SetBool(_animIDSlide, false);
                }
                //PunchRight();
                _punchRight = true; // This is used for drawing debug sphere for punch
                _input.punchRight = false;
                _tryToCrouch = false;
                _tryToSlide = false;
            }
            else if (_input.punchLeft)
            {
                int trigger = _input.isModified ? _animIDLeftKickTrigger : _animIDLeftPunchTrigger;


                if (_hasAnimator)
                {
                    _animator.SetTrigger(trigger);
                    _animator.SetBool(_animIDCrouch, false);
                    _animator.SetBool(_animIDSlide, false);
                }
                //PunchLeft();
                _punchLeft = true; // This is used for drawing debug sphere for punch
                _input.punchLeft = false;
                _tryToCrouch = false; 
                _tryToSlide = false; 
            }
        }

        private void LockOnAttack()
        {
            if (!_aimAssist || LockOnTarget == null) { return; }

            Vector2 input = _input.move;
           

            // aim toward target
            _targetRotation = Mathf.Atan2(input.x, input.y) * Mathf.Rad2Deg +
                                    _mainCamera.transform.eulerAngles.y;
            Quaternion lockOnTargetRotation = Quaternion.Euler(new Vector3(0, _targetRotation, 0));
           
            Vector3 targetDirection = LockOnTarget.transform.position - transform.position;
            targetDirection.y = 0f;
            if (targetDirection.magnitude > 0.1f)
            {
                lockOnTargetRotation = Quaternion.LookRotation(targetDirection);
                //transform.rotation = Quaternion.Slerp(transform.rotation, lockOnTargetRotation, RotationSmoothTime);
            }
          



            _targetRotation = Mathf.LerpAngle(lockOnTargetRotation.eulerAngles.y, _targetRotation, _aimAssistStrength);
            float rotation = Mathf.SmoothDampAngle(transform.eulerAngles.y, _targetRotation, ref _rotationVelocity,
                RotationSmoothTime);

            // rotate to face input direction relative to camera position
            transform.rotation = Quaternion.Euler(0.0f, rotation, 0.0f);


            // close distance  to target
            if (targetDirection.magnitude > punchRange)
            {
                Debug.Log("Close distance to target");
                float distanceToPunchRange = targetDirection.magnitude - punchRange;
                //Debug.DrawLine(_controller.transform.position, _controller.transform.position + (targetDirection.normalized * _lockOnTargetCloseDistance), Color.red, 10f);
                _controller.Move(targetDirection.normalized * distanceToPunchRange);//_lockOnTargetCloseDistance);
                _controller.SimpleMove(Vector3.zero); // This is to stop the character from boosting
            }

            // attack target
            // This returns to the normal attack flow
            return;
        }

        private void PunchRight()
        {
            Ray ray = new Ray(rightShoulder.position, transform.forward);
            RaycastHit hit;
            //Generate cast from right shoulder of character
            Physics.SphereCast(ray, punchRadius, out hit, punchRange);

            
            //See if we hit anything special
            if(hit.collider != null)
            {
                Debug.Log($"Right Punched {hit.collider.gameObject}");
                //PunchHit(hit.point, hit.collider);
            }
            else
            {
                Debug.Log("Missed Right Punch");
            }
        }

        private void PunchHit(Collision collision, Vector3 attackingPos, Weapon weapon)
        {


            if (collision == null /*|| !IsAttacking()*/) { return; }

            
            //// Get the ik target & default pos for this hand
            //TwoBoneIKConstraint iKConstraint = weapon.IK;
            //Vector3 defaultRelativePos = weapon.IK_TargetOriginalPos;

            //// Set ik weight to 1
            //iKConstraint.weight = 1;


            //// Lerp ik target back to default position
            //iKConstraint.data.target.position = attackingPos;
            //StartCoroutine(
            //    LerpToTarget(iKConstraint, defaultRelativePos, weapon.animRecoveryTime));
            
            if (collision.gameObject != null && collision.gameObject.GetComponent<HealthSystem>() != null)
            {
                Vector3 hitPos = collision.GetContact(0).point;
                Vector3 forceDir = hitPos - attackingPos;
                collision.gameObject.GetComponent<HealthSystem>().RecieveDmg(punchDmg, gameObject, forceDir.normalized, hitPos);
            }

            _animator.CrossFadeInFixedTime(_animStateIDIdle, 0.1f);
        }

        IEnumerator LerpToTarget(TwoBoneIKConstraint iKConstraint, Vector3 targetPos, float lerpTime)
        {
            Transform transToMove = iKConstraint.data.target;
            float time = 0;
            Vector3 startPos = transToMove.position;
            while (time < lerpTime)
            {
                transToMove.position = Vector3.Lerp(startPos, targetPos + transform.position, time / lerpTime);
                time += Time.deltaTime;
                iKConstraint.weight =1 -  (time / lerpTime);
                yield return null;
            }
            transToMove.position = targetPos + transform.position;
            iKConstraint.weight = 0;
        }

        private bool isPunching()
        {// test why this is returning true when not punching
            bool animRight = _animator.GetCurrentAnimatorStateInfo(0).IsName("PunchingRight");
            bool animLeft = _animator.GetCurrentAnimatorStateInfo(0).IsName("PunchingLeft");

            return _input.punchRight || _input.punchLeft ||
                animRight || animLeft;
        }

        private bool isKicking()
        {
            return _animator.GetCurrentAnimatorStateInfo(0).IsName("LeftKick") ||
                _animator.GetCurrentAnimatorStateInfo(0).IsName("RightKick");
        }

        private void PunchLeft()
        {
            Ray ray = new Ray(leftShoulder.position, transform.forward);
            RaycastHit hit;
            //Generate cast from right shoulder of character
            Physics.SphereCast(ray, punchRadius, out hit, punchRange);


            //See if we hit anything special
            if (hit.point != null && hit.collider != null)
            {
                
                //PunchHit(hit.point, hit.collider);
            }
            else
            {
                Debug.Log("Missed Left Punch");
            }
        }

        private void OnDrawGizmos()
        {


            Gizmos.DrawSphere(rightShoulder.position, punchRadius);
            Gizmos.DrawSphere(rightShoulder.position + (punchRange * transform.forward), punchRadius);
            Gizmos.DrawSphere(leftShoulder.position, punchRadius);
            Gizmos.DrawSphere(leftShoulder.position + (punchRange * transform.forward), punchRadius);


            if (_punchRight)
            {
                Gizmos.DrawSphere(rightShoulder.position, punchRadius);
                Gizmos.DrawSphere(rightShoulder.position + (punchRange * transform.forward), punchRadius);
                _punchRight = false;
                Debug.DebugBreak();
            }

            if(_punchLeft)
            {
                Gizmos.DrawSphere(leftShoulder.position, punchRadius);
                Gizmos.DrawSphere(leftShoulder.position + (punchRange * transform.forward), punchRadius);
                _punchLeft = false;
            }
        }

        private bool CanStartSlide()
        {
            bool resetFinished = _slideResetTimer > slideResetDuration;
            return _speed > MoveSpeed && _tryToCrouch && !_tryToSlide && resetFinished;
        }

        private bool CanStartRoll()
        {
            bool resetFinished = _rollResetTimer > rollResetDuration;
            return !_tryToSlide && resetFinished;
        }

        bool IsAttacking()
        {
            //TODO: add kicks to this check

            return isPunching() || isKicking();
        }
        
        

        private void Move()
        {
            if (_lockOn && IsAttacking()) {
                Debug.Log("early return");
                return; 
            }


            if (_input.lockOn)
            {
                _lockOn = !_lockOn;
                _input.lockOn = false;
            }
            if (_input.sprint)
            {
                if (_lockOn)
                {
                    _lockOn = false;
                    _input.lockOn = false;
                }

                if (CanStartSlide())
                {
                    _tryToSlide = true;
                    _slideTimer = 0f;
              
                    // Change collider dimensions
                    _controller.height = slideHeight;
                    _controller.center = new Vector3(0, slideHeight / 2, 0);
                }
                else if(_slideTimer > slideDuration)
                {
                    _tryToCrouch = false;
                }
            }

            if (_input.roll && Grounded && !_tryToRoll)
            {
                if (CanStartRoll())
                {
                    Debug.Log("start roll");
                    _tryToRoll = true;
                    _rollTimer = 0f;

                    // Change collider dimensions
                    _controller.height = rollHeight;
                    _controller.center = new Vector3(0, rollHeight / 2, 0);
                }
                else if (_slideTimer > slideDuration)
                {
                    _tryToCrouch = false;
                }
            }
            float targetSpeed;

            // TODO: this is horrible, split into functions to make more readable
            if (_tryToRoll || _tryToSlide)
            {
                if (_tryToRoll)
                {
                    targetSpeed = rollSpeed;

                    _rollTimer += Time.deltaTime;

                    _tryToRoll = _rollTimer < rollDuration;

                    Debug.Log($"_rollTimer == {_rollTimer}, rollDurration = {rollDuration}");
                    if (!_tryToRoll)
                    {
                        _controller.height = _standingHeight;
                        _controller.center = new Vector3(0, _standingHeight / 2, 0);
                        _rollResetTimer = 0;
                    }
                }

                // Only continue sliding if still crouching (not a toggle atm)
                // TODO: maybe change this to friction based slide stop, especially if there are multiple surface types
                else // _tryToSlide == true --> run sliding logic
                {
                    targetSpeed = slideSpeed;

                    //CHATGPT ANSWER HERE
                    _slideTimer += Time.deltaTime;

                    // cancle if timer runs out or player isn't holding crouch anymore
                    _tryToSlide = _slideTimer >= slideDuration ? false : _tryToCrouch;

                    if (!_tryToSlide)
                    {
                        Debug.Log("reset collider");
                        // reset collider dimensions
                        _controller.height = _standingHeight;
                        _controller.center = new Vector3(0, _standingHeight / 2, 0);
                        _slideResetTimer = 0;
                    }
                }
            }
            else {
                float slideResetTimerDelta = _tryToSlide ? 0 : Time.deltaTime;
                float rollResetTimerDelta = _tryToRoll ? 0 : Time.deltaTime;
                _slideResetTimer +=  slideResetTimerDelta;
                _rollResetTimer += rollResetTimerDelta;

                // set target speed based on move speed, sprint speed and if sprint is pressed
                targetSpeed = _input.sprint ? SprintSpeed : MoveSpeed;
                if (_tryToCrouch)
                {
                    // limit speed to crouch speed
                    Math.Clamp(targetSpeed, targetSpeed, 6f); //TODO: creat variable for crouch speed
                }

                // a simplistic acceleration and deceleration designed to be easy to remove, replace, or iterate upon

                // note: Vector2's == operator uses approximation so is not floating point error prone, and is cheaper than magnitude
                // if there is no input, set the target speed to 0
                if (_input.move == Vector2.zero) targetSpeed = 0.0f;
            }


            // a reference to the players current horizontal velocity
            float currentHorizontalSpeed = new Vector3(_controller.velocity.x, 0.0f, _controller.velocity.z).magnitude;

            
            float speedOffset = 0.1f;
            float inputMagnitude = _input.analogMovement ? _input.move.magnitude : 1f;

            // accelerate or decelerate to target speed
            if (currentHorizontalSpeed < targetSpeed - speedOffset ||
                currentHorizontalSpeed > targetSpeed + speedOffset)
            {
                // creates curved result rather than a linear one giving a more organic speed change
                // note T in Lerp is clamped, so we don't need to clamp our speed
                _speed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed * inputMagnitude,
                    Time.deltaTime * SpeedChangeRate);

                // round speed to 3 decimal places
                _speed = Mathf.Round(_speed * 1000f) / 1000f;
            }
            else
            {
                _speed = targetSpeed;
            }

            _animationBlend = Mathf.Lerp(_animationBlend, targetSpeed, Time.deltaTime * SpeedChangeRate);
            if (_animationBlend < 0.01f) _animationBlend = 0f;

            WatchTargetVelocity = _input.move;
            Vector2 input = _input.move;

            if (_previouslyAttacking)
            {
                input = Vector2.zero;
            }
            // Convert the input direction from world space to camera space
            Vector3 cameraForward = _mainCamera.transform.forward;
            cameraForward.y = 0f;
            cameraForward.Normalize();

            Vector3 cameraRight = _mainCamera.transform.right;
            cameraRight.y = 0f;
            cameraRight.Normalize();

            
            Vector3 localInput = cameraForward * input.y + cameraRight * input.x;

            // Normalize the input direction to ensure consistent movement speed
            if (localInput.magnitude > 1f)
            {
                localInput.Normalize();
            }

            // Calculate the target velocity based on the input direction and movement speed
            Vector3 targetVelocity = localInput * _speed;
            if (_tryToRoll || _tryToSlide)
            {
                Vector3 forward = transform.forward;
                _targetRotation = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
                Vector3 targetDirection = Quaternion.Euler(0.0f, _targetRotation, 0.0f) * Vector3.forward;
                targetVelocity = targetDirection * _speed;
            }
            targetVelocity = new Vector3(targetVelocity.x, _verticalVelocity, targetVelocity.z);            

            // note: Vector2's != operator uses approximation so is not floating point error prone, and is cheaper than magnitude
            // if there is a move input rotate player when the player is moving
            if (/*input != Vector2.zero && */!_tryToSlide && !_tryToRoll)
            {
                _targetRotation = Mathf.Atan2(input.x, input.y) * Mathf.Rad2Deg +
                                    _mainCamera.transform.eulerAngles.y;
                Quaternion lockOnTargetRotation = Quaternion.Euler(new Vector3(0, _targetRotation, 0));
                if (LockOnTarget != null && _lockOn)
                {
                    Vector3 targetDirection = LockOnTarget.transform.position - transform.position;
                    targetDirection.y = 0f;
                    if (targetDirection.magnitude > 0.1f)
                    {
                        lockOnTargetRotation = Quaternion.LookRotation(targetDirection);
                        //transform.rotation = Quaternion.Slerp(transform.rotation, lockOnTargetRotation, RotationSmoothTime);
                    }

                    // TODO this condition doesn't brake tha game
                }
                


                _targetRotation = Mathf.LerpAngle(lockOnTargetRotation.eulerAngles.y, _targetRotation, LockOnStrength);
                float rotation = Mathf.SmoothDampAngle(transform.eulerAngles.y, _targetRotation, ref _rotationVelocity,
                    RotationSmoothTime);

                // rotate to face input direction relative to camera position
                transform.rotation = Quaternion.Euler(0.0f, rotation, 0.0f);
                
            }


            //WatchTargetVelocity = targetVelocity;
            _controller.Move(targetVelocity * Time.deltaTime);

            
            // update animator if using character
            if (_hasAnimator)
            {
                _animator.SetFloat(_animIDSpeed, _animationBlend);
                _animator.SetFloat(_animIDMotionSpeed, inputMagnitude);
                _animator.SetBool(_animIDCrouch, _tryToCrouch);
                _animator.SetBool(_animIDSlide, _tryToSlide);
                _animator.SetBool(_animIDRoll, _tryToRoll);
            }
        }

        private void SetFlipCollider(bool rollUp)
        {
            if (rollUp)
            {
                // Roll into ball
                _controller.height = FlipJumpCapsuleHeight;
                _controller.center = new Vector3(0, (FlipJumpCapsuleHeight / 2) + .15f, 0);
                return;
            } 
            
            // Unroll from ball
            _controller.height = _standingHeight;
            _controller.center = _standingCenter;
        }

        private void JumpAndGravity()
        {
            if (Grounded)
            {
                // reset the fall timeout timer
                _fallTimeoutDelta = FallTimeout;

                
                if(!_previouslyGrounded)
                {
                    _verticalVelocity = 0f;
                    _input.jump = false;
                    _input.flipJump = false;
                }

                // update animator if using character
                if (_hasAnimator)
                {
                    _animator.SetBool(_animIDJump, false);
                    _animator.SetBool(_animIDFreeFall, false);
                    _animator.SetBool(_animIDFlip, false);
                }

                // stop our velocity dropping infinitely when grounded
                if (_verticalVelocity < 0.0f)
                {
                    _verticalVelocity = -2f;
                }

                if (_input.flipJump && _jumpTimeoutDelta <= 0.0f)
                {
                    // the square root of H * -2 * G = how much velocity needed to reach desired height
                    _verticalVelocity = Mathf.Sqrt(FlipJumpHeight * -2f * Gravity);

                    // update animator if using character
                    if (_hasAnimator)
                    {
                        _animator.SetBool(_animIDFlip, true);
                        _animator.SetBool(_animIDCrouch, false);
                        _animator.SetBool(_animIDSlide, false);
                    }

                    _tryToCrouch = false;
                    _tryToSlide = false;
                }
                // Jump
                else if (_input.jump && _jumpTimeoutDelta <= 0.0f)
                {
                    // the square root of H * -2 * G = how much velocity needed to reach desired height
                    _verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);

                    // update animator if using character
                    if (_hasAnimator)
                    {
                        _animator.SetBool(_animIDJump, true);
                        _animator.SetBool(_animIDCrouch, false);
                        _animator.SetBool(_animIDSlide, false);
                    }

                    _tryToCrouch = false;
                    _tryToSlide = false;
                }

                // jump timeout
                if (_jumpTimeoutDelta >= 0.0f)
                {
                    _jumpTimeoutDelta -= Time.deltaTime;
                }
            }
            else
            {
                // reset the jump timeout timer\
                // TODO: this looks wrong
                _jumpTimeoutDelta = JumpTimeout;
                bool flipJumping = _animator.GetBool(_animIDFlip);

                //if(_hasAnimator && !flipJumping && _input.flipJump && FallTimeout - _fallTimeoutDelta < _timeToFlipJump)
                //{
                //    Debug.Log("flip jump while jumping");
                //    _animator.SetBool(_animIDFlip, true);   
                //}

                // if we are currently flip jumping
                if (_hasAnimator && flipJumping)
                {
                    // Curl or uncurl from a ball depending on if we are within the apex of the jump
                    SetFlipCollider(Math.Abs(_controller.velocity.y) < FlipJumpApexBoundVelocityMagnitude);
                }
                else
                {
                    // If we are not flipping just make sure that we are not still rolled up
                    // TODO: this might interfere with crouching
                    SetFlipCollider(false);
                }
                // fall timeout
                if (_fallTimeoutDelta >= 0.0f)
                {
                    _fallTimeoutDelta -= Time.deltaTime;
                }
                else
                {
                    // update animator if using character
                    if (_hasAnimator)
                    {
                        _animator.SetBool(_animIDFreeFall, true);
                    }
                }

                // if we are not grounded, do not jump
                _input.jump = false;
                _input.flipJump = false;
            }

            // apply gravity over time if under terminal (multiply by delta time twice to linearly speed up over time)
            if (_verticalVelocity < _terminalVelocity)
            {
                _verticalVelocity += Gravity * Time.deltaTime;
            }
        }

        private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
        {
            if (lfAngle < -360f) lfAngle += 360f;
            if (lfAngle > 360f) lfAngle -= 360f;
            return Mathf.Clamp(lfAngle, lfMin, lfMax);
        }

        /*
        private void OnDrawGizmosSelected()
        {
            Color transparentGreen = new Color(0.0f, 1.0f, 0.0f, 0.35f);
            Color transparentRed = new Color(1.0f, 0.0f, 0.0f, 0.35f);

            if (Grounded) Gizmos.color = transparentGreen;
            else Gizmos.color = transparentRed;

            // when selected, draw a gizmo in the position of, and matching radius of, the grounded collider
            Gizmos.DrawSphere(
                new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z),
                GroundedRadius);
        }*/


        private void OnFootstep(AnimationEvent animationEvent)
        {
            if (animationEvent.animatorClipInfo.weight > 0.5f)
            {
                if (FootstepAudioClips.Length > 0)
                {
                    var index = UnityEngine.Random.Range(0, FootstepAudioClips.Length);
                    AudioSource.PlayClipAtPoint(FootstepAudioClips[index], transform.TransformPoint(_controller.center), FootstepAudioVolume);
                }
            }
        }

        private void OnLand(AnimationEvent animationEvent)
        {
            if (animationEvent.animatorClipInfo.weight > 0.5f)
            {
                AudioSource.PlayClipAtPoint(LandingAudioClip, transform.TransformPoint(_controller.center), FootstepAudioVolume);
            }
        }
    }
}