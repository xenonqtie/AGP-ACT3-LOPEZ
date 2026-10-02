using System;
using UnityEngine;

public class Jump : MonoBehaviour
{
    [SerializeField] public float timeToApex = 0.35f;
    [SerializeField] public float timeToFall = 0.25f;
    [SerializeField] public float jumpHeight = 3f;
    [SerializeField] private float maxSpeed = 8f;
    [SerializeField] private float maxSpeedOnAir = 4f;
    [SerializeField] private float timeToReachMax = 0.3f;
    [SerializeField] private float timeToStop = 0.1f;
    [SerializeField] private float turnSpeed = 0.5f;
    [SerializeField] private float turnSpeedScalar = 2f;
    [SerializeField] private float wallSlideSpeed = 2f;
    [SerializeField] private float wallCheckDistance = 0.55f;
    [SerializeField] private float maxSlopeAngle = 45f;
    [SerializeField] private float minClimbSpeedRatio = 0.4f;
    [SerializeField] private float forcedSlopeSlideSpeed = 14f;
    [SerializeField] private float steepSlopeSlideAccel = 25f;
    [SerializeField] private float groundCheckDistance = 0.9f;
    [SerializeField] private float groundCheckRadius = 0.25f;

    float acceleration => (isGrounded ? maxSpeed : maxSpeedOnAir) / timeToReachMax;
    float deceleration => (isGrounded ? maxSpeed : maxSpeedOnAir) / timeToStop;
    float turnRate => (maxSpeed * turnSpeedScalar) / turnSpeed;

    float riseG => (2f * jumpHeight) / (timeToApex * timeToApex);
    float fallG => (2f * jumpHeight) / (timeToFall * timeToFall);
    float jumpVelocity => (2f * jumpHeight) / timeToApex;

    private Vector3 velocity = Vector3.zero;
    private bool isGrounded = true;
    private Collider playerCollider;

    private RaycastHit[] hitThisFrame = new RaycastHit[10];
    private Collider[] overlapBuffer = new Collider[10];

    private void Awake()
    {
        playerCollider = GetComponent<Collider>();
    }

    private void Update()
    {
        HitType hitType = HorizontalMovement();
        HandleWallMechanics();

        if (isGrounded && velocity.y < 0)
        {
            velocity.y = 0f;
        }

        if (isGrounded && Input.GetButtonDown("Jump"))
        {
            velocity.y = jumpVelocity;
            isGrounded = false;
        }

        if (!isGrounded && hitType != HitType.Slope)
        {
            velocity.y -= (velocity.y < 0 ? fallG : riseG) * Time.deltaTime;
        }

        transform.position += velocity * Time.deltaTime;

        ResolveCollisions();

        Debug.DrawRay(transform.position, Vector3.down * groundCheckDistance, isGrounded ? Color.green : Color.red);
    }

    private HitType HorizontalMovement()
    {
        float input = Input.GetAxisRaw("Horizontal");
        Vector3 groundNormal;
        HitType hitType = RaycastDown(out groundNormal);

        isGrounded = (hitType == HitType.Ground || hitType == HitType.WalkableSlope);

        switch (hitType)
        {
            case HitType.Ground:
            {
                float targetSpeed = maxSpeed * input;
                float targetAccel = ChooseAccel(input);
                velocity.x = Mathf.MoveTowards(velocity.x, targetSpeed, targetAccel * Time.deltaTime);
                break;
            }

            case HitType.WalkableSlope:
            {
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                {
                    Vector3 downhillDir = Vector3.ProjectOnPlane(Vector3.down, groundNormal).normalized;
                    velocity = downhillDir * forcedSlopeSlideSpeed;
                }
                else
                {
                    bool isClimbingUphill = (input * groundNormal.x < -0.01f);

                    float currentMaxSpeed = maxSpeed;
                    if (isClimbingUphill)
                    {
                        float slopeAngle = Vector3.Angle(groundNormal, Vector3.up);
                        float speedMultiplier = Mathf.Lerp(1f, minClimbSpeedRatio, slopeAngle / maxSlopeAngle);
                        currentMaxSpeed *= speedMultiplier;
                    }

                    float targetSpeed = currentMaxSpeed * input;
                    float targetAccel = ChooseAccel(input);

                    if (Mathf.Approximately(input, 0f))
                    {
                        velocity = Vector3.MoveTowards(velocity, Vector3.zero, deceleration * Time.deltaTime);
                    }
                    else
                    {
                        Vector3 moveDirection = Vector3.ProjectOnPlane(Vector3.right * Mathf.Sign(input), groundNormal).normalized * Mathf.Abs(targetSpeed);
                        velocity = Vector3.MoveTowards(velocity, moveDirection, targetAccel * Time.deltaTime);
                    }
                }
                break;
            }

            case HitType.Slope: 
            {
                Vector3 slideDirection = Vector3.ProjectOnPlane(Vector3.down, groundNormal).normalized;
                float currentSlideSpeed = Mathf.Max(velocity.magnitude, 6f) + (steepSlopeSlideAccel * Time.deltaTime);
                velocity = slideDirection * currentSlideSpeed;
                break;
            }

            case HitType.Air:
            {
                float targetSpeed = maxSpeedOnAir * input;
                float targetAccel = ChooseAccel(input);
                velocity.x = Mathf.MoveTowards(velocity.x, targetSpeed, targetAccel * Time.deltaTime);
                break;
            }
        }

        return hitType;
    }

    private void HandleWallMechanics()
    {
        float input = Input.GetAxisRaw("Horizontal");

        bool wallOnRight = CheckHorizontalWall(Vector3.right, out RaycastHit rightHit);
        bool wallOnLeft = CheckHorizontalWall(Vector3.left, out RaycastHit leftHit);

        bool pushingRight = input > 0 && wallOnRight;
        bool pushingLeft = input < 0 && wallOnLeft;
        bool isPushingWall = pushingRight || pushingLeft;

        if (pushingRight && velocity.x > 0) velocity.x = 0;
        else if (pushingLeft && velocity.x < 0) velocity.x = 0;

        if (!isGrounded && isPushingWall && velocity.y < 0)
        {
            if (velocity.y < -wallSlideSpeed)
            {
                velocity.y = -wallSlideSpeed;
            }
        }
    }

    private void ResolveCollisions()
    {
        if (playerCollider == null) return;

        int count = Physics.OverlapSphereNonAlloc(transform.position, 1.2f, overlapBuffer);

        for (int i = 0; i < count; i++)
        {
            Collider otherCollider = overlapBuffer[i];
            if (otherCollider == null || otherCollider == playerCollider || otherCollider.isTrigger)
                continue;

            if (Physics.ComputePenetration(
                playerCollider, transform.position, transform.rotation,
                otherCollider, otherCollider.transform.position, otherCollider.transform.rotation,
                out Vector3 direction, out float distance))
            {
                transform.position += direction * distance;
            }
        }
    }

    private bool CheckHorizontalWall(Vector3 direction, out RaycastHit wallHit)
    {
        Vector3[] rayOrigins = new Vector3[]
        {
            transform.position + Vector3.up * 0.4f,
            transform.position,
            transform.position - Vector3.up * 0.4f
        };

        foreach (var origin in rayOrigins)
        {
            Ray ray = new Ray(origin, direction);
            if (Physics.Raycast(ray, out wallHit, wallCheckDistance))
            {
                if (wallHit.collider.gameObject == gameObject) continue;

                float surfaceAngle = Vector3.Angle(wallHit.normal, Vector3.up);
                if (surfaceAngle >= 85f && surfaceAngle <= 95f)
                {
                    return true;
                }
            }
        }

        wallHit = default;
        return false;
    }

    private float ChooseAccel(float input)
    {
        bool isTurning = !Mathf.Approximately(Mathf.Sign(input), Mathf.Sign(velocity.x));
        if (Mathf.Abs(input) > 0)
        {
            return isTurning ? turnRate : acceleration;
        }
        return deceleration;
    }

    private HitType RaycastDown(out Vector3 normal)
    {
        Vector3 origin = transform.position + Vector3.up * 0.2f;
        int hits = Physics.SphereCastNonAlloc(origin, groundCheckRadius, Vector3.down, hitThisFrame, groundCheckDistance);

        if (hits > 0)
        {
            for (int i = 0; i < hits; i++)
            {
                var hit = hitThisFrame[i];
                if (hit.collider == null || hit.collider.gameObject == gameObject) continue;

                normal = hit.normal;
                float angle = Vector3.Angle(normal, Vector3.up);

                if (angle <= 0.05f)
                {
                    return HitType.Ground;
                }
                else if (angle <= maxSlopeAngle)
                {
                    return HitType.WalkableSlope;
                }
                else if (angle < 85f)
                {
                    return HitType.Slope;
                }
            }
        }

        normal = Vector3.up;
        return HitType.Air;
    }

    private enum HitType
    {
        Ground = 0,
        Air = 1,
        Slope = 2,
        WalkableSlope = 3,
        Wall = 4
    }
}