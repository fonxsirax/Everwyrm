using UnityEngine;

/// <summary>
/// Câmera third-person orbital para o dragão.
///  Mouse = orbitar · Scroll = zoom · Esc = liberar cursor · Clique = travar de novo.
/// Em voo: FOV aumenta com a velocidade e a câmera se alinha atrás do dragão
/// quando o mouse fica parado.
/// </summary>
[RequireComponent(typeof(Camera))]
public class DragonCamera : MonoBehaviour
{
    [Header("Alvo")]
    public Transform target;                 // auto: tag "Player"
    [SerializeField] float pivotHeight = 2.4f;

    [Header("Órbita")]
    [SerializeField] float distance = 10f;
    [SerializeField] float minDistance = 3f;
    [SerializeField] float maxDistance = 20f;
    [SerializeField] float sensitivity = 3f;
    [SerializeField] float zoomSpeed = 5f;
    [SerializeField] float minPitch = -25f;
    [SerializeField] float maxPitch = 70f;

    [Header("Suavização")]
    [SerializeField] float followLag = 10f;
    [SerializeField] float autoAlignDelay = 1.5f;
    [SerializeField] float autoAlignSpeed = 1.6f;

    [Header("FOV")]
    [SerializeField] float baseFov = 60f;
    [SerializeField] float flightFov = 74f;

    Camera cam;
    DragonController dragon;
    LayerMask collisionMask;
    float camYaw, camPitch = 15f;
    float lastMouseTime;
    Vector3 smoothPos;

    void Start()
    {
        cam = GetComponent<Camera>();

        if (target == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) target = p.transform;
        }
        if (target != null)
        {
            dragon = target.GetComponentInParent<DragonController>();
            camYaw = target.eulerAngles.y;
            collisionMask = Physics.DefaultRaycastLayers & ~(1 << target.gameObject.layer);
            smoothPos = transform.position;
        }
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (DragonStatsMenu.IsOpen) return; // a ficha controla o cursor

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        if (Cursor.lockState != CursorLockMode.Locked && Input.GetMouseButtonDown(0))
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void LateUpdate()
    {
        if (target == null) return;
        float dt = Time.deltaTime;

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            float mx = Input.GetAxis("Mouse X");
            float my = Input.GetAxis("Mouse Y");
            if (Mathf.Abs(mx) > 0.01f || Mathf.Abs(my) > 0.01f) lastMouseTime = Time.time;
            camYaw += mx * sensitivity;
            camPitch = Mathf.Clamp(camPitch - my * sensitivity, minPitch, maxPitch);
        }

        // em voo, sem mexer o mouse, alinha atrás do dragão
        if (dragon != null && dragon.IsFlying && Time.time - lastMouseTime > autoAlignDelay)
        {
            camYaw = Mathf.LerpAngle(camYaw, target.eulerAngles.y, autoAlignSpeed * dt);
            camPitch = Mathf.Lerp(camPitch, 12f, autoAlignSpeed * dt);
        }

        distance = Mathf.Clamp(distance - Input.GetAxis("Mouse ScrollWheel") * zoomSpeed * distance * 0.35f,
                               minDistance, maxDistance);

        // acompanha o crescimento do dragão (filhote = câmera perto, colossal = longe)
        float sizeScale = Mathf.Max(0.2f, target.lossyScale.y);

        Vector3 pivot = target.position + Vector3.up * pivotHeight * sizeScale;
        Quaternion rot = Quaternion.Euler(camPitch, camYaw, 0f);
        float dist = distance * sizeScale;

        // não atravessar paredes/terreno
        if (Physics.SphereCast(pivot, 0.35f, rot * Vector3.back, out RaycastHit hit,
                dist, collisionMask, QueryTriggerInteraction.Ignore))
            dist = Mathf.Max(hit.distance - 0.1f, minDistance * 0.5f);

        Vector3 desired = pivot + rot * Vector3.back * dist;
        smoothPos = Vector3.Lerp(smoothPos, desired, 1f - Mathf.Exp(-followLag * dt));
        transform.position = smoothPos;
        transform.rotation = Quaternion.LookRotation(pivot - smoothPos, Vector3.up);

        // FOV com sensação de velocidade
        float targetFov = dragon != null && dragon.IsFlying
            ? Mathf.Lerp(baseFov, flightFov, dragon.Speed01)
            : baseFov;
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, 3f * dt);
    }
}
