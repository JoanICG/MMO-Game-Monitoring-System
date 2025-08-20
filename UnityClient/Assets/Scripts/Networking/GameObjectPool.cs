using System.Collections.Generic;
using UnityEngine;

public class GameObjectPool : MonoBehaviour
{
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private GameObject botPrefab;
    
    private Queue<GameObject> playerPool = new Queue<GameObject>();
    private Queue<GameObject> botPool = new Queue<GameObject>();
    private List<GameObject> activeObjects = new List<GameObject>();
    
    public static GameObjectPool Instance { get; private set; }
    
    private void Awake()
    {
        Instance = this;
        
        // Pre-create player prefabs if not assigned
        if (playerPrefab == null)
        {
            playerPrefab = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            playerPrefab.GetComponent<Renderer>().material.color = Color.blue;
            playerPrefab.SetActive(false);
        }
        
        if (botPrefab == null)
        {
            botPrefab = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            botPrefab.GetComponent<Renderer>().material.color = Color.red;
            botPrefab.transform.localScale = Vector3.one * 1.5f;
            botPrefab.SetActive(false);
        }
        
        // Pre-populate pools
        PrePopulatePools();
    }
    
    private void PrePopulatePools()
    {
        // Create initial pool of 50 player objects
        for (int i = 0; i < 50; i++)
        {
            var playerObj = Instantiate(playerPrefab, transform);
            playerObj.SetActive(false);
            playerPool.Enqueue(playerObj);
        }
        
        // Create initial pool of 200 bot objects
        for (int i = 0; i < 200; i++)
        {
            var botObj = Instantiate(botPrefab, transform);
            botObj.SetActive(false);
            botPool.Enqueue(botObj);
        }
    }
    
    public GameObject GetPlayerObject(bool isBot = false)
    {
        GameObject obj;
        
        if (isBot)
        {
            if (botPool.Count > 0)
            {
                obj = botPool.Dequeue();
            }
            else
            {
                obj = Instantiate(botPrefab, transform);
            }
        }
        else
        {
            if (playerPool.Count > 0)
            {
                obj = playerPool.Dequeue();
            }
            else
            {
                obj = Instantiate(playerPrefab, transform);
            }
        }
        
        obj.SetActive(true);
        activeObjects.Add(obj);
        return obj;
    }
    
    public void ReturnPlayerObject(GameObject obj, bool isBot = false)
    {
        if (obj == null) return;
        
        obj.SetActive(false);
        activeObjects.Remove(obj);
        
        // Reset position and rotation
        obj.transform.position = Vector3.zero;
        obj.transform.rotation = Quaternion.identity;
        
        if (isBot)
        {
            botPool.Enqueue(obj);
        }
        else
        {
            playerPool.Enqueue(obj);
        }
    }
    
    public void ReturnAllObjects()
    {
        for (int i = activeObjects.Count - 1; i >= 0; i--)
        {
            var obj = activeObjects[i];
            if (obj != null)
            {
                bool isBot = obj.name.StartsWith("Bot_");
                ReturnPlayerObject(obj, isBot);
            }
        }
        activeObjects.Clear();
    }
    
    public int GetActiveCount() => activeObjects.Count;
    public int GetPlayerPoolCount() => playerPool.Count;
    public int GetBotPoolCount() => botPool.Count;
}
