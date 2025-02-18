using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Screen = UnityEngine.Device.Screen;

public class DiscSwipeController : MonoBehaviour
{
    public float moveSpeed = 5f; 
    private Vector2 swipeStart;
    private bool isSwiping = false;
    private GameObject selectedMonster = null; 
    [SerializeField] private GameObject puff;
    [SerializeField] private Camera mainCamera;
    [SerializeField] Canvas gameCanvas;
    [SerializeField] private AudioSource jumpSound;
    [SerializeField] AudioSource popSound;

    private int totalMonsters; 
    private int remainingMonsters;
    
    public UIManager uiManager;

    private void Awake()
    {
        Time.timeScale = 1f;
    }

    private void Start()
    {
        totalMonsters = GameObject.FindGameObjectsWithTag("Monster").Length;
        remainingMonsters = totalMonsters;
        Debug.Log(remainingMonsters);
    }

    private void Update()
    {
        DetectSwipe();
    }

    private void DetectSwipe()
    {
        if (Input.GetMouseButtonDown(0))
        {
            swipeStart = Input.mousePosition;
            isSwiping = true;

            // Check if the starting position is a disc
            Ray ray = Camera.main.ScreenPointToRay(swipeStart);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                if (hit.collider.CompareTag("Monster"))
                {
                    selectedMonster = hit.collider.gameObject;
                    Debug.Log($"Monster selected at start: {selectedMonster.name}");
                }
                else
                {
                    Debug.Log("No monster detected at start.");
                    isSwiping = false; 
                }
            }
        }

        if (Input.GetMouseButtonUp(0) && isSwiping)
        {
            Vector2 swipeEnd = Input.mousePosition;
            Vector2 swipeDirection = swipeEnd - swipeStart;

            if (swipeDirection.magnitude > 0.1f && selectedMonster != null) 
            {
                swipeDirection.Normalize();
                DetermineMoveDirection(swipeDirection);
            }
            else
            {
                Debug.Log("Swipe too short or no monster selected, not registering.");
            }

            isSwiping = false;
            selectedMonster = null; // Reset selectedMonster after the swipe
        }
    }

    private void DetermineMoveDirection(Vector2 direction)
    {
        Vector3 moveDirection;

        // Determine cardinal direction
        if (Mathf.Abs(direction.x) > Mathf.Abs(direction.y))
        {
            moveDirection = direction.x > 0 ? Vector3.right : Vector3.left;
        }
        else
        {
            moveDirection = direction.y > 0 ? Vector3.forward : Vector3.back;
        }

        Debug.Log($"Swipe detected. Moving direction: {moveDirection}");

        if (selectedMonster != null)
        {
            StartCoroutine(MoveMonster(selectedMonster, moveDirection));
        }
        else
        {
            Debug.Log("No monster selected to move.");
        }
    }

    private System.Collections.IEnumerator MoveMonster(GameObject monster, Vector3 direction)
{
    Vector3 startScale = monster.transform.localScale;
    Vector3 enlargedScale = startScale * 1.2f;

    // Rotate the monster to face the movement direction and add 90° to Y-axis
    if (direction != Vector3.zero)
    {
        Quaternion lookRotation = Quaternion.LookRotation(direction);
        monster.transform.rotation = lookRotation * Quaternion.Euler(0, 90, 0); // Adding 90° on Y-axis
    }

    while (true)
    {
        Vector3 nextPosition = monster.transform.position + direction;
        Collider[] colliders = Physics.OverlapSphere(nextPosition, 0.5f);
        bool canMove = false;
        bool isHole = false;
        GameObject targetBlock = null;
        GameObject targetHole = null;

        foreach (var collider in colliders)
        {
            if (collider.CompareTag("Block") && collider.transform.childCount == 0)
            {
                canMove = true;
                targetBlock = collider.gameObject;
                break;
            }
            else if (collider.CompareTag("Block") && collider.transform.childCount > 0)
            {
                Debug.Log("Obstacle detected: Stopping movement.");
                CheckAndDestroyMatchingMonsters(monster.transform.position);
            }
            else if (collider.CompareTag("Hole"))
            {
                isHole = true;
                targetHole = collider.gameObject;
                break;
            }
        }

        // Handle hole interaction
        if (isHole)
        {
            string monsterName = monster.name;
            string holeName = targetHole.name;

            if (!monsterName.Equals(holeName.Replace("Hole", "Monster")))
            {
                float moveDuration = 0.3f;
                float elapsedTime = 0f;

                Vector3 initialPosition = monster.transform.position;
                Vector3 holePosition = targetHole.transform.position + new Vector3(0, 0.5f, 0);

                while (elapsedTime < moveDuration)
                {
                    float t = elapsedTime / moveDuration;
                    t = t * t * (3f - 2f * t); // Smoothstep easing
                    monster.transform.position = Vector3.Lerp(initialPosition, holePosition, t);

                    elapsedTime += Time.deltaTime;
                    yield return null;
                }

                monster.transform.position = holePosition;
                uiManager.GameOver();
                Debug.Log($"Monster {monster.name} stopped behind hole: {targetHole.name}");
                yield break;
            }
            else
            {
                Debug.Log($"Monster {monster.name} moving to hole: {targetHole.name}");

                float moveDuration = 0.3f;
                float elapsedTime = 0f;

                Vector3 initialPosition = monster.transform.position;
                Vector3 holePosition = targetHole.transform.position + new Vector3(0, 0.1f, 0);

                while (elapsedTime < moveDuration)
                {
                    float t = elapsedTime / moveDuration;
                    t = t * t * (3f - 2f * t); // Smoothstep easing
                    monster.transform.position = Vector3.Lerp(initialPosition, holePosition, t);

                    elapsedTime += Time.deltaTime;
                    yield return null;
                }

                monster.transform.position = holePosition;
                Debug.Log($"Monster {monster.name} destroyed in hole: {targetHole.name}");
                Destroy(monster);
                popSound.Play();
                CreatePuff(targetHole);

                remainingMonsters--;
                Debug.Log(remainingMonsters);

                if (remainingMonsters == 0)
                {
                    uiManager.TriggerGameWon();
                }

                yield break;
            }
        }

        if (!canMove)
        {
            Debug.Log("No valid block to move to. Stopping.");
            yield break;
        }
        
        // Ensure monster faces the movement direction again before moving
        Quaternion lookRotation = Quaternion.LookRotation(direction);
        monster.transform.rotation = lookRotation * Quaternion.Euler(0, 90, 0); // Adding 90° on Y-axis

        CreateTrailEffect(monster);
        float moveTime = 0.2f;
        float elapsedBlockTime = 0f;

        Vector3 startPosition = monster.transform.position;

        // Scale up for a juicy effect
        monster.transform.localScale = enlargedScale;

        while (elapsedBlockTime < moveTime)
        {
            float t = elapsedBlockTime / moveTime;
            t = t * t * (3f - 2f * t); // Smoothstep easing
            monster.transform.position = Vector3.Lerp(startPosition, nextPosition, t);

            elapsedBlockTime += Time.deltaTime;
            yield return null;
        }

        monster.transform.position = nextPosition;
        monster.transform.localScale = startScale;
        jumpSound.Play();

        if (targetBlock != null)
        {
            monster.transform.SetParent(targetBlock.transform);
            Vector3 customPosition = targetBlock.transform.position + new Vector3(0, 0.1f, 0);
            monster.transform.position = customPosition;

            Debug.Log($"Monster {monster.name} positioned at custom position: {monster.transform.position}");
        }
    }
}


    private void CreateTrailEffect(GameObject monster)
    {
        TrailRenderer trail = monster.GetComponent<TrailRenderer>();
        if (!trail)
        {
            trail = monster.AddComponent<TrailRenderer>();
            trail.time = 1f; // Duration of the trail
            trail.startWidth = 0.3f;
            trail.endWidth = 0f;
            trail.material = new Material(Shader.Find("Sprites/Default"));
            trail.startColor = Color.white;
            trail.endColor = new Color(1, 1, 1, 0);
        }
    }

    private void CreatePuff(GameObject holeObject)
    {
        Vector3 screenPosition = mainCamera.WorldToScreenPoint(holeObject.transform.position);
        GameObject puffObject = Instantiate(puff, gameCanvas.transform);
        RectTransform rectTransform = puffObject.GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            // Convert screen position to Canvas space
            rectTransform.anchoredPosition = ScreenToCanvasPosition(screenPosition, gameCanvas);
            rectTransform.localScale = Vector3.one; // Ensure correct scale
        }

        Destroy(puffObject, 1f);
    }
    private Vector2 ScreenToCanvasPosition(Vector3 screenPosition, Canvas canvas)
    {
        Vector2 canvasPosition;
        RectTransform canvasRect = canvas.GetComponent<RectTransform>();

        // Convert screen position to Canvas space
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect, 
            screenPosition, 
            canvas.worldCamera, 
            out canvasPosition
        );

        return canvasPosition;
    }
    private void CheckAndDestroyMatchingMonsters(Vector3 position)
    {
        HashSet<GameObject> uniqueMatchingMonsters = new HashSet<GameObject>();

        // Check in horizontal direction first (you can choose to check vertical first if preferred)
        AddMatchingMonsters(Vector3.right, position, uniqueMatchingMonsters); // Check right
        AddMatchingMonsters(Vector3.left, position, uniqueMatchingMonsters);  // Check left

        // If no matching discs found horizontally, check vertically
        if (uniqueMatchingMonsters.Count == 0)
        {
            AddMatchingMonsters(Vector3.forward, position, uniqueMatchingMonsters);  // Check forward
            AddMatchingMonsters(Vector3.back, position, uniqueMatchingMonsters);     // Check back
        }

        // If there are 3 or more matching discs (including the original), destroy them
        if (uniqueMatchingMonsters.Count >= 3)
        {
            uniqueMatchingMonsters.Add(GetMonsterAtPosition(position)); // Include the original disc

            foreach (GameObject monster in uniqueMatchingMonsters)
            {
                Destroy(monster);
                CreatePuff(monster); // Optional puff effect
                popSound.Play();
                remainingMonsters--; // Correct decrement
            }

            Debug.Log($"Destroyed {uniqueMatchingMonsters.Count} monsters. Remaining: {remainingMonsters}");

            // Check if all discs are removed
            if (remainingMonsters == 0)
            {
                uiManager.TriggerGameWon();
            }
        }
    }


    private void AddMatchingMonsters(Vector3 direction, Vector3 startPosition, HashSet<GameObject> matches)
    {
        Vector3 nextPosition = startPosition + direction;
        GameObject nextMonster = GetMonsterAtPosition(nextPosition);

        while (nextMonster != null && nextMonster.name == GetMonsterAtPosition(startPosition).name)
        {
            matches.Add(nextMonster);
            nextPosition += direction;
            nextMonster = GetMonsterAtPosition(nextPosition);
        }
    }

// Helper function to get the disc at a specific position
    private GameObject GetMonsterAtPosition(Vector3 position)
    {
        Collider[] colliders = Physics.OverlapSphere(position, 0.5f);
        foreach (var collider in colliders)
        {
            if (collider.CompareTag("Monster"))
                return collider.gameObject;
        }
        return null;
    }

}