using UnityEngine;

public class DebugCamera : MonoBehaviour
{
	private Quaternion originalRotation;
	private float rotationX;
	private float rotationY;
    private bool isEnabled;

    public void Start ()
    {
        originalRotation = transform.localRotation;

    	Enable();
	}

	public void OnEnable()
    {
		Enable();
	}

	public void OnDisable()
    {
        Disable();
    }

    public void Update()
    {
        if (Input.GetKeyUp(KeyCode.Escape))
        {
            if (isEnabled)
            {
                Disable();
            }
            else
            {
                Enable();
            }
        }

        if (isEnabled)
        {
            float currentSpeed = 10.0f;
            float sensitivity = 3.0f;

            rotationX += Input.GetAxis("Mouse X") * sensitivity;
            rotationY += Input.GetAxis("Mouse Y") * sensitivity;

            rotationY = Mathf.Clamp(rotationY, -89f, 89f);

            Quaternion xq = Quaternion.AngleAxis(rotationX, Vector3.up);
            Quaternion yq = Quaternion.AngleAxis(rotationY, -Vector3.right);

            transform.rotation = originalRotation * xq * yq;

            currentSpeed *= Time.deltaTime;

            transform.position = transform.position +
                transform.forward * Input.GetAxis("Vertical") * currentSpeed +
                    transform.right * Input.GetAxis("Horizontal") * currentSpeed;
        }
    }

	private void Enable()
    {
        isEnabled = true;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Disable()
    {
        isEnabled = false;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
