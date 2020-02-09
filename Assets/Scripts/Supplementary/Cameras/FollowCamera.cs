using Unity.Mathematics;
using UnityEngine;

public class FollowCamera : MonoBehaviour
{
	//
	// Target transform to be tracked
	//
	
	public Transform targetTransform;
	
	//
	// Offset to be maintained between camera and target
	//

	private float3 offset;
	
	[Range(0.01f, 1.0f)]
	public float smoothFactor = 0.5f;

	public float degreesPerSecond = 180.0f;

	public float maximumYawAngle = 45.0f;

	public float minimumHeight = 0.2f;

	public float heightOffset = 1.0f;
	
	void Start ()
	{
		offset = Convert(transform.position) - TargetPosition;
	}
	
	void LateUpdate ()
	{
        float radiansPerSecond = math.radians(degreesPerSecond);

        float horizontal = GetHorizontalSafe();
        float vertical = GetVerticalSafe();

        if (math.abs(horizontal) >= 0.2f)
        {
            RotateOffset(Time.deltaTime * horizontal * radiansPerSecond, Vector3.up);
        }

        if (math.abs(vertical) >= 0.2f)
        {
            float angleAt = math.abs(math.asin(transform.forward.y));
            float maximumAngle = math.radians(maximumYawAngle);
            float angleDeltaDesired = Time.deltaTime * vertical * radiansPerSecond;
            float angleDeltaClamped =
                CalculateAngleDelta(angleDeltaDesired,
                    maximumAngle - angleAt);

            RotateOffset(angleDeltaClamped, transform.right);
        }

        Vector3 cameraPosition = TargetPosition + offset;

        if (cameraPosition.y <= minimumHeight)
        {
            cameraPosition.y = minimumHeight;
        }

        transform.position = Vector3.Slerp(transform.position, cameraPosition, smoothFactor);

        transform.LookAt(TargetPosition);
	}

    void OnGUI()
    {
        try
        {
            GetHorizontal();
            GetVertical();
        }
        catch
        {
            GUI.Label(new Rect(0, 0, 900, 20), "To control the camera, please configure 'Right Analog Horizontal' and 'Right Analog Vertical' axes in the Input Manager");
        }
    }

    float GetHorizontal()
    {
        return Input.GetAxis("Right Analog Horizontal");
    }

    float GetVertical()
    {
        return Input.GetAxis("Right Analog Vertical");
    }

    float GetHorizontalSafe()
    {
        try
        {
            return GetHorizontal();
        }
        catch
        {
            return 0.0f;
        }
    }

    float GetVerticalSafe()
    {
        try
        {
            return GetVertical();
        }
        catch
        {
            return 0.0f;
        }
    }

    private float CalculateAngleDelta(float angleDeltaDesired, float angleRemaining)
	{
		if (math.dot(transform.forward, Missing.up) >= 0.0f)
		{
			return -math.min(-angleDeltaDesired, angleRemaining);
		}
		else
		{
			return math.min(angleDeltaDesired, angleRemaining);
		}
	}
	
	private void RotateOffset(float angleInRadians, float3 axis)
	{
		offset = math.mul(quaternion.AxisAngle(axis, angleInRadians), offset);
	}

	private static float3 Convert(Vector3 p)
	{
		return p;
	}

	private float3 TargetPosition
	{
		get { return Convert(targetTransform.position) + new float3(0.0f, heightOffset, 0.0f); }
	}
}
