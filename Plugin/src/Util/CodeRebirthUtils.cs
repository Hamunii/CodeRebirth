using System;
using CodeRebirth.MapStuff;
using CodeRebirth.EnemyStuff;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using Random = System.Random;
using Unity.Mathematics;
using System.Collections.Generic;

namespace CodeRebirth.Util.Spawning;
internal class CodeRebirthUtils : NetworkBehaviour
{
    static Random random;
    internal static CodeRebirthUtils Instance { get; private set; }
    public static List<GrabbableObject> goldenEggs = new List<GrabbableObject>();
    void Awake()
    {
        Instance = this;
    }
    
    [ServerRpc(RequireOwnership = false)]
    public void SpawnScrapServerRpc(string itemName, Vector3 position, bool isQuest = false, bool defaultRotation = true, int valueIncrease = 0) {
        if (StartOfRound.Instance == null) {
            return;
        }
        
        if (random == null) {
            random = new Random(StartOfRound.Instance.randomMapSeed + 85);
        }

        if (itemName == string.Empty) {
            return;
        }
        Plugin.samplePrefabs.TryGetValue(itemName, out Item item);
        if (item == null) {
            // throw for stacktrace
            throw new NullReferenceException($"'{itemName}' either isn't a CodeRebirth scrap or not registered! This method only handles CodeRebirth scrap!");
        }
        Transform parent = null;
        if (parent == null) {
            parent = StartOfRound.Instance.propsContainer;
        }
        GameObject go = Instantiate(item.spawnPrefab, position + Vector3.up * 0.2f, defaultRotation == true ? Quaternion.Euler(item.restingRotation) : Quaternion.identity, parent);
        go.GetComponent<NetworkObject>().Spawn();
        int value = random.Next(minValue: item.minValue + valueIncrease, maxValue: item.maxValue + valueIncrease);
        var scanNode = go.GetComponentInChildren<ScanNodeProperties>();
        scanNode.scrapValue = value;
        scanNode.subText = $"Value: ${value}";
        go.GetComponent<GrabbableObject>().scrapValue = value;
        UpdateScanNodeClientRpc(new NetworkObjectReference(go), value);
        if (isQuest) go.AddComponent<QuestItem>();
    }

    [ClientRpc]
    public void UpdateScanNodeClientRpc(NetworkObjectReference go, int value) {
        go.TryGet(out NetworkObject netObj);
        if(netObj != null)
        {
            if (netObj.TryGetComponent(out GrabbableObject grabbableObject)) {
                grabbableObject.SetScrapValue(value);
            }
            var scanNode = netObj.GetComponentInChildren<ScanNodeProperties>();
            scanNode.scrapValue = value;
            scanNode.subText = $"Value: ${value}";
        }
    }
}