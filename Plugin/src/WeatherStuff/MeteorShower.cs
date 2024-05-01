using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using Unity.Netcode;
using Unity.Collections;

namespace CodeRebirth.WeatherStuff;

public class MeteorShower : MonoBehaviour
{
    [SerializeField] private LayerMask layersToIgnore = 0;
    [SerializeField] private int minTimeBetweenSpawns = 20;
    [SerializeField] private int maxTimeBetweenSpawns = 60;
    [SerializeField] private int maxToSpawn = 1;
    [SerializeField] private int meteorLandRadius = 6;

    private Vector2 meteorSpawnDirection;
    private Vector3 meteorSpawnLocationOffset;

    private float lastTimeUsed;
    private float currentTimeOffset;
    private System.Random random;
    private GameObject meteorPrefab;

    private const int RandomSeedOffset = -53;

    private void OnEnable()
    {
        if (!(NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost)) return;
        
        random = new System.Random(StartOfRound.Instance.randomMapSeed + RandomSeedOffset);
        TimeOfDay.Instance.onTimeSync.AddListener(OnGlobalTimeSync);

        StartCoroutine(DecideSpawnArea());

        // Wait 12-18 seconds before spawning first batch.
        currentTimeOffset = random.Next(12, 18);
    }

    private IEnumerator DecideSpawnArea()
    {
        var spawnDirection = (float)random.NextDouble() * 2 * Mathf.PI;
        meteorSpawnDirection = new Vector2(Mathf.Sin(spawnDirection), Mathf.Cos(spawnDirection));
        meteorSpawnLocationOffset = new Vector3(meteorSpawnDirection.x * random.Next(540, 1200), 350,
            meteorSpawnDirection.y * random.Next(540, 1200));

        StartCoroutine(PlanMeteor(6, false, result =>
        {
            if (!result)
            {
                StartCoroutine(DecideSpawnArea());
            }
        }));
        yield return null;
    }

    private void OnDisable()
    {
        TimeOfDay.Instance.onTimeSync.RemoveListener(OnGlobalTimeSync);
    }

    private void OnGlobalTimeSync()
    {
        var time = TimeOfDay.Instance.globalTime;
        if (time <= lastTimeUsed + currentTimeOffset)
            return;
        lastTimeUsed = time;
        PlanStrikes();
    }

    private void PlanStrikes()
    {
        currentTimeOffset = random.Next(minTimeBetweenSpawns, maxTimeBetweenSpawns);

        var amountToSpawn = random.Next(1, maxToSpawn);
        
        for (var i = 0; i < amountToSpawn; i++)
        {
            StartCoroutine(PlanMeteor());
        }
    }

    private IEnumerator PlanMeteor(int maxAttempts = 4, bool spawn = true, Action<bool> callback = null)
    {
        bool result = false;
        for (var i = 0; i < maxAttempts; i++)
        {
            var initialPos = RoundManager.Instance.outsideAINodes[random.Next(0, RoundManager.Instance.outsideAINodes.Length)].transform.position;
            var landLocation = RoundManager.Instance.GetRandomNavMeshPositionInBoxPredictable(initialPos, meteorLandRadius, RoundManager.Instance.navHit, random);
            var spawnLocation = landLocation + meteorSpawnLocationOffset;

            if (Physics.RaycastAll(spawnLocation, landLocation - spawnLocation, Mathf.Infinity, ~layersToIgnore).Any(hit => hit.transform && hit.transform.tag != "Wood"))
            {
                yield return null;
                continue;
            }

            if (!spawn) 
            {
                result = true;
                break;
            }

            var timeAtSpawn = NetworkManager.Singleton.LocalTime.Time + (random.NextDouble() * 10 + 2);
            SendMeteorSpawnInfo(new MeteorSpawnInfo(timeAtSpawn, spawnLocation, landLocation));

            result = true;
            break;
        }
        
        callback?.Invoke(result);
    }

    private void SendMeteorSpawnInfo(MeteorSpawnInfo info)
    {
        var writer = new FastBufferWriter(24 + 2 * 12, Allocator.Temp); // Double size + two Vector3 sizes
        using (writer)
        {
            writer.WriteValueSafe(info.timeToSpawnAt);
            writer.WriteValueSafe(info.spawnLocation.x);
            writer.WriteValueSafe(info.spawnLocation.y);
            writer.WriteValueSafe(info.spawnLocation.z);
            writer.WriteValueSafe(info.landLocation.x);
            writer.WriteValueSafe(info.landLocation.y);
            writer.WriteValueSafe(info.landLocation.z);
            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage("MeteorSpawn", NetworkManager.Singleton.ConnectedClientsIds, writer, NetworkDelivery.ReliableFragmentedSequenced);
        }
    }

    [Serializable]
    public struct MeteorSpawnInfo
    {
        public double timeToSpawnAt;
        public Vector3 spawnLocation;
        public Vector3 landLocation;

        public MeteorSpawnInfo(double timeToSpawnAt, Vector3 spawnLocation, Vector3 landLocation)
        {
            this.timeToSpawnAt = timeToSpawnAt;
            this.spawnLocation = spawnLocation;
            this.landLocation = landLocation;
        }
    }
}
