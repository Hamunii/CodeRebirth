﻿﻿using System.Collections.Generic;
using CodeRebirth.ItemStuff;
using CodeRebirth.Misc;
using CodeRebirth.Util.Extensions;
using CodeRebirth.Util.Spawning;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using Random = System.Random;

namespace CodeRebirth.MapStuff;

public class ItemCrate : CRHittable {

	[SerializeField]
	public SkinnedMeshRenderer mainRenderer = null!;

	[SerializeField]
	[Header("Hover Tooltips")]
	public string regularHoverTip = "Hold : [E]";
	[SerializeField]
	public string keyHoverTip = "Open : [LMB]";

	[Header("Audio")]
	[SerializeField]
	public AudioSource slowlyOpeningSFX = null!;
	[SerializeField]
	public AudioSource openSFX = null!;
	public InteractTrigger trigger = null!;
	public Pickable pickable = null!;
	private Animator animator = null!;
	private List<PlayerControllerB> playersOpeningBox = new List<PlayerControllerB>(); // rework so that it's faster if more players are opening etc
	public AnimationClip openClip = null!;
	public Rigidbody crateLid;
	public bool opened;
	public NetworkVariable<float> digProgress = new(writePerm: NetworkVariableWritePermission.Owner);
	public NetworkVariable<int> health = new(4);
	public Vector3 originalPosition;
	public Random random = new();
	public enum CrateType {
		Wooden,
		Metal,
		Golden,
	}
	public CrateType crateType;
	
	public void Awake() {
		trigger = GetComponent<InteractTrigger>();
		pickable = GetComponent<Pickable>();
		animator = GetComponent<Animator>();
		trigger.onInteractEarly.AddListener(OnInteractEarly);
		trigger.onInteract.AddListener(OnInteract);
		trigger.onStopInteract.AddListener(OnInteractCancel);

		digProgress.OnValueChanged += UpdateDigPosition;
		
		
		originalPosition = transform.position;
		UpdateDigPosition(0, 0);
	}

	public override void OnNetworkSpawn() {
		if(IsOwner)
			digProgress.Value = random.NextFloat(0.01f, 0.3f);
	}
	
	void UpdateDigPosition(float old, float newValue) {
		if(IsOwner) // :wharg:
			transform.position = originalPosition + (transform.up * newValue * .5f);
		
		Plugin.Logger.LogDebug($"ItemCrate was hit! New digProgress: {newValue}");
		if (crateType == CrateType.Wooden) {
			trigger.interactable = newValue >= 1;
			pickable.enabled = trigger.interactable;
		}
	}

	public void Update() {
		
		if (GameNetworkManager.Instance.localPlayerController.currentlyHeldObjectServer != null && GameNetworkManager.Instance.localPlayerController.currentlyHeldObjectServer.itemProperties.itemName == "Key") {
			trigger.hoverTip = keyHoverTip;
		} else {
			trigger.hoverTip = regularHoverTip;
		}
	}

	public void OnInteractEarly(PlayerControllerB playerController) {
		slowlyOpeningSFX.Play();
	}
	
	public void OnInteract(PlayerControllerB playerController) {
		if (GameNetworkManager.Instance.localPlayerController != playerController) return;
		Open();
		slowlyOpeningSFX.Stop();
	}

	public void OnInteractCancel(PlayerControllerB playerController) {
		slowlyOpeningSFX.Stop();
	}

	public void Open() {
		if(opened) return;
		if (!IsHost) OpenCrateServerRPC();
		else OpenCrate();
	}

	[ServerRpc(RequireOwnership = false)]
	public void OpenCrateServerRPC() {
		OpenCrate();
	}

	public void OpenCrate() {
		for (int i = 0; i < 3; i++) {
			SpawnableItemWithRarity chosenItemWithRarity = random.NextItem(RoundManager.Instance.currentLevel.spawnableScrap);
			Item item = chosenItemWithRarity.spawnableItem;
			GameObject spawned = Instantiate(item.spawnPrefab, transform.position + transform.up*0.6f + transform.right*random.NextFloat(-0.2f, 0.2f) + transform.forward*random.NextFloat(-0.2f, 0.2f), Quaternion.Euler(item.restingRotation), RoundManager.Instance.spawnedScrapContainer);

			GrabbableObject grabbableObject = spawned.GetComponent<GrabbableObject>();
			
			grabbableObject.SetScrapValue((int)(random.Next(item.minValue + 10, item.maxValue + 10) * RoundManager.Instance.scrapValueMultiplier));
			grabbableObject.NetworkObject.Spawn();
			CodeRebirthUtils.Instance.UpdateScanNodeClientRpc(new NetworkObjectReference(spawned), grabbableObject.scrapValue);
		}
		OpenCrateClientRPC();
	}

	[ClientRpc]
	public void OpenCrateClientRPC() {
		OpenCrateLocally();
	}

	public void OpenCrateLocally() {
		pickable.IsLocked = false;
		openSFX.Play();
		trigger.enabled = false;
		GetComponent<Collider>().enabled = false;
		opened = true;

		if (animator != null) {
			animator.SetTrigger("opened");
		}
		if(crateLid != null) {
			crateLid.isKinematic = false;
			crateLid.AddForce(crateLid.transform.up, ForceMode.Impulse);
		}

		if (crateType == CrateType.Metal) {
			// todo: this is just a debug until unity stops having a moment
			foreach (MeshRenderer renderer in GetComponentsInChildren<MeshRenderer>()) {
				renderer.enabled = false;
			}
		}
	}

	[ServerRpc(RequireOwnership = false)]
	void SetNewDigProgressServerRPC(float newDigProgress) {
		digProgress.Value = Mathf.Clamp01(newDigProgress);
	}

	[ServerRpc(RequireOwnership = false)]
	void DamageCrateServerRPC(int damage) {
		health.Value -= damage;

		if (health.Value <= 0) {
			OpenCrate();
		}
	}
	
	public override bool Hit(int force, Vector3 hitDirection, PlayerControllerB? playerWhoHit = null, bool playHitSFX = false, int hitID = -1) {
		if (opened) return false;
		if (digProgress.Value < 1) {
			float progressChange = random.NextFloat(0.15f, 0.25f);
			if (IsOwner) {
				digProgress.Value += progressChange;
			} else {
				SetNewDigProgressServerRPC(digProgress.Value + progressChange);
			}
			
		} else {
			DamageCrateServerRPC(1);
		}
		return true; // this bool literally doesn't get used. i have no clue.
	}
}