using System.Collections;
using GameNetcodeStuff;
using UnityEngine;
using System.Linq;
using UnityEngine.AI;
using CodeRebirth.src.Util;
using Unity.Netcode.Components;
using System;
using Unity.Netcode;
using CodeRebirth.src.MiscScripts;

namespace CodeRebirth.src.Content.Enemies;
public class RedwoodTitanAI : CodeRebirthEnemyAI, IVisibleThreat
{
    public Material[] AlbinoMaterials = null!;    
    public Collider[] DeathColliders = null!;
    public Collider CollisionFootR = null!;
    public Collider CollisionFootL = null!;    
    public ParticleSystem DustParticlesLeft = null!;
    public ParticleSystem DustParticlesRight = null!;
    public ParticleSystem ForestKeeperParticles = null!;
    public ParticleSystem DriftwoodGiantParticles = null!;
    public ParticleSystem OldBirdParticles = null!;
    public ParticleSystem DeathParticles = null!;
    public ParticleSystem BigSmokeEffect = null!;
    public AudioSource creatureSFXFar = null!;
    public AudioClip[] stompSounds = null!;
    public AudioClip[] farStompSounds = null!;
    public AudioClip eatenSound = null!;
    public AudioClip spawnSound = null!;
    public AudioClip roarSound = null!;
    public AudioClip jumpSound = null!;
    public AudioClip kickSound = null!;
    public AudioClip crunchySquishSound = null!;
    public GameObject holdingBone = null!;
    public GameObject eatingArea = null!;
    public AnimationClip eating = null!;
    public AnimationClip kickAnimation = null!;
    public NetworkAnimator networkAnimator = null!;

    private Vector3 eatPosition = Vector3.zero;
    private bool sizeUp = false;
    private bool eatingEnemy = false;
    private float walkingSpeed = 0f;
    private float seeableDistance = 0f;
    private float distanceFromShip = 0f;
    private Transform shipBoundaries = null!;
    private bool testBuild = false; 
    private LineRenderer line = null!;
    private static int instanceNumbers = 0;
    private Collider[] enemyColliders = null!;
    private PlayerControllerB? playerToKick = null;
    private System.Random redwoodRandom = null!;
    [NonSerialized]
    public bool kickingOut = false;
    [NonSerialized]
    public bool kicking = false;
    [NonSerialized]
    public bool jumping = false;

    #region ThreatType
    ThreatType IVisibleThreat.type => ThreatType.ForestGiant;
    int IVisibleThreat.SendSpecialBehaviour(int id) {
        return 0; 
    }
    int IVisibleThreat.GetThreatLevel(Vector3 seenByPosition) {
        return 18;
    }
    int IVisibleThreat.GetInterestLevel() {
        return 0;
    }
    Transform IVisibleThreat.GetThreatLookTransform() {
        return eye;
    }
    Transform IVisibleThreat.GetThreatTransform() {
        return base.transform;
    }
    Vector3 IVisibleThreat.GetThreatVelocity() {
        if (base.IsOwner) {
            return agent.velocity;
        }
        return Vector3.zero;
    }
    float IVisibleThreat.GetVisibility() {
        if (isEnemyDead) {
            return 0f;
        }
        if (agent.velocity.sqrMagnitude > 0f) {
            return 1f;
        }
        return 0.75f;
    }
    #endregion
    enum State {
        Spawn, // Roaring
        Idle, // Idling
        Wandering, // Wandering
        RunningToTarget, // Chasing
        EatingTargetGiant, // Eating
    }
    public override void Start()
    {
        base.Start();
#if DEBUG
        testBuild = true;
#endif
        instanceNumbers++;

        redwoodRandom = new System.Random(StartOfRound.Instance.randomMapSeed + instanceNumbers);
        if (redwoodRandom.Next(0, 2) == 0)
        {
            this.skinnedMeshRenderers[0].materials = AlbinoMaterials;
        }

        this.currentSearch.searchWidth *= 10f;
        this.currentSearch.searchPrecision *= 0.25f;

        walkingSpeed = Plugin.ModConfig.ConfigRedwoodSpeed.Value;
        distanceFromShip = Plugin.ModConfig.ConfigRedwoodShipPadding.Value;
        seeableDistance = Plugin.ModConfig.ConfigRedwoodEyesight.Value;
        shipBoundaries = StartOfRound.Instance.shipBounds.transform;

        enemyColliders = GetComponentsInChildren<Collider>();

        if (testBuild)
        {
            line = gameObject.AddComponent<LineRenderer>();
            line.widthMultiplier = 0.2f; // reduce width of the line
        }

        creatureSFX.PlayOneShot(spawnSound);
        creatureSFXFar.PlayOneShot(spawnSound);
        creatureVoice.PlayOneShot(roarSound);

        agent.speed = 0f;
        if (IsServer)
        {
            this.creatureAnimator.SetBool("walking", false);
            this.creatureAnimator.SetBool("chasing", false);
            this.networkAnimator.SetTrigger("startSpawn");
        }
        SwitchToBehaviourStateOnLocalClient((int)State.Spawn);
        CodeRebirthPlayerManager.OnDoorStateChange += OnShipDoorStateChange;
        StartCoroutine(UpdateAudio());
    }

    private IEnumerator UpdateAudio()
    {
        while (!isEnemyDead) {
            if (GameNetworkManager.Instance.localPlayerController.isInHangarShipRoom)
            {
                creatureSFX.volume = Plugin.ModConfig.ConfigRedwoodNormalVolume.Value * Plugin.ModConfig.ConfigRedwoodInShipVolume.Value;
                creatureSFXFar.volume = Plugin.ModConfig.ConfigRedwoodNormalVolume.Value * Plugin.ModConfig.ConfigRedwoodInShipVolume.Value;
                creatureVoice.volume = Plugin.ModConfig.ConfigRedwoodNormalVolume.Value * Plugin.ModConfig.ConfigRedwoodInShipVolume.Value;
            }
            else
            {
                creatureSFX.volume = Plugin.ModConfig.ConfigRedwoodNormalVolume.Value;
                creatureSFXFar.volume = Plugin.ModConfig.ConfigRedwoodNormalVolume.Value;
                creatureVoice.volume = Plugin.ModConfig.ConfigRedwoodNormalVolume.Value;
            }
            yield return new WaitForSeconds(0.5f);
        }
    }

    private void OnShipDoorStateChange(object sender, bool shipDoorClosed)
    {
        if (shipDoorClosed)
        {
            foreach (PlayerControllerB player in StartOfRound.Instance.allPlayerScripts)
            {
                if (player.isInHangarShipRoom)
                {
                    foreach (Collider? playerCollider in player.GetCRPlayerData().playerColliders!)
                    {
                        foreach (Collider enemyCollider in enemyColliders)
                        {
                            Physics.IgnoreCollision(playerCollider, enemyCollider, true);
                        }
                    }
                }
            }
        }
        else
        {
            foreach (PlayerControllerB player in StartOfRound.Instance.allPlayerScripts)
            {
                foreach (Collider playerCollider in player.GetCRPlayerData().playerColliders!)
                {
                    foreach (Collider enemyCollider in enemyColliders)
                    {
                        Physics.IgnoreCollision(playerCollider, enemyCollider, false);
                    }
                }
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        CodeRebirthPlayerManager.OnDoorStateChange -= OnShipDoorStateChange;
    }

    public override void EnableEnemyMesh(bool enable, bool overrideDoNotSet = false)
    {
        base.EnableEnemyMesh(enable);
    }

    public override void Update()
    {
        base.Update();
        if (isEnemyDead) return;
        if (playerToKick != null)
        {
            if (!kickingOut)
            {
                // Calculate the direction towards the player
                Vector3 directionToPlayer = playerToKick.transform.position - this.transform.position;
                Quaternion lookRotation = Quaternion.LookRotation(directionToPlayer);
                this.transform.rotation = Quaternion.Slerp(this.transform.rotation, lookRotation, Time.deltaTime);
            }
            else if (kickingOut)
            {
                // Assuming you want to rotate towards the right direction of the object
                Vector3 rightDirection = this.transform.right;
                Quaternion lookRotation = Quaternion.LookRotation(rightDirection);
                this.transform.rotation = Quaternion.Slerp(this.transform.rotation, lookRotation, Time.deltaTime);
            }
        }
    }

    public void LateUpdate()
    {
        if (isEnemyDead) return;
        if (currentBehaviourStateIndex == (int)State.EatingTargetGiant && targetEnemy != null)
        {
            this.transform.position = eatPosition;
            var grabPosition = holdingBone.transform.position;
            targetEnemy.transform.position = grabPosition + new Vector3(0, -1f, 0);
            targetEnemy.transform.LookAt(eatingArea.transform.position);
            targetEnemy.transform.position = grabPosition + new Vector3(0, -5.5f, 0);
            // Scale targetEnemy's transform down by 0.9995 everytime Update runs in this if statement
            if (!sizeUp)
            {
                targetEnemy.transform.position = grabPosition + new Vector3(0, -1f, 0);
                targetEnemy.transform.LookAt(eatingArea.transform.position);
                targetEnemy.transform.position = grabPosition + new Vector3(0, -6f, 0);
                var newScale = targetEnemy.transform.localScale;
                newScale.x *= 1.4f;
                newScale.y *= 1.3f;
                newScale.z *= 1.4f;
                targetEnemy.transform.localScale = newScale;
                sizeUp = true;
            }
            targetEnemy.transform.position += new Vector3(0, 0.02f, 0);
            Vector3 currentScale = targetEnemy.transform.localScale;
            currentScale *= 0.9995f;
            targetEnemy.transform.localScale = Vector3.Lerp(targetEnemy.transform.localScale, currentScale, Time.deltaTime);
        }
    }

    public void DoFunnyThingWithNearestPlayer(PlayerControllerB closestPlayer)
    {
        if (kicking || jumping) return;
        float distanceToClosestPlayer = Vector3.Distance(transform.position, closestPlayer.transform.position);
        if (distanceToClosestPlayer <= 8f && UnityEngine.Random.Range(0f, 100f) <= 2.5f)
        {
            this.creatureAnimator.SetBool("walking", false);
            this.creatureAnimator.SetBool("chasing", false);
            JumpInPlace();   
        }
        else if (distanceToClosestPlayer <= 16f && UnityEngine.Random.Range(0f, 100f) <= 2.5f && currentBehaviourStateIndex == (int)State.Wandering)
        {
            this.creatureAnimator.SetBool("walking", false);
            this.creatureAnimator.SetBool("chasing", false);
            DoKickTargetPlayer(closestPlayer);
        }
    }

    public void JumpInPlace()
    {
        if (IsServer) networkAnimator.SetTrigger("startJump");
        creatureSFX.PlayOneShot(jumpSound);
        creatureSFXFar.PlayOneShot(jumpSound);
        jumping = true;
        agent.speed = 0.5f;
        Plugin.ExtendedLogging("Start Jump"); 
    }

    public void DoKickTargetPlayer(PlayerControllerB closestPlayer)
    {
        kicking = true;
        agent.speed = 0.5f;
        if (IsServer) networkAnimator.SetTrigger("startKick");
        playerToKick = closestPlayer;
        creatureSFX.PlayOneShot(kickSound);
        creatureSFXFar.PlayOneShot(kickSound);
        Plugin.ExtendedLogging("Start Kick");
    }

    public override void DoAIInterval()
    {
        if (testBuild)
        { 
            StartCoroutine(DrawPath(line, agent));
        }
        base.DoAIInterval();
        if (isEnemyDead || StartOfRound.Instance.allPlayersDead || !IsHost)
        {
            return;
        }
        switch(currentBehaviourStateIndex)
        {
            case (int)State.Spawn:
                break;
            case (int)State.Idle:
                break;
            case (int)State.Wandering:
                DoWandering();
                break;
            case (int)State.RunningToTarget:
                DoRunningToTarget();
                break;
            case (int)State.EatingTargetGiant:
                break;
            default:
                Plugin.Logger.LogWarning("This Behavior State doesn't exist!");
                break;
        }
    }

    public void DoWandering()
    {
        if (FindClosestAliveGiantInRange(seeableDistance))
        {
            this.creatureAnimator.SetBool("walking", false);
            this.creatureAnimator.SetBool("chasing", false);
            Plugin.ExtendedLogging("Start Target Giant");
            StopSearchRoutine();
            SwitchToBehaviourServerRpc((int)State.RunningToTarget);
            StartCoroutine(SetSpeedForChasingGiant());
            return;
        } // Look for Giants
        PlayerControllerB closestPlayer = GetClosestPlayerToRedwood();
        if (closestPlayer != null)
        {
            DoFunnyThingWithNearestPlayer(closestPlayer);
        }
    }

    public IEnumerator SetSpeedForChasingGiant()
    {
        agent.speed = 0f;
        this.networkAnimator.SetTrigger("startEnrage");
        yield return new WaitForSeconds(6.9f);
        this.creatureAnimator.SetBool("chasing", true);
        agent.angularSpeed = 100f;
        agent.speed = walkingSpeed * 4;
    }

    public void DoRunningToTarget()
    {
        // Keep targetting closest Giant, unless they are over 20 units away and we can't see them.
        if (targetEnemy == null || targetEnemy.isEnemyDead)
        {
            targetEnemy = null;
            Plugin.ExtendedLogging("Stop Target Giant");
            this.creatureAnimator.SetBool("chasing", false);
            this.creatureAnimator.SetBool("walking", true);
            this.agent.angularSpeed = 40f;
            StartSearchRoutine(this.transform.position, 50, this.agent.areaMask);
            agent.speed = walkingSpeed;
            SwitchToBehaviourServerRpc((int)State.Wandering);
            return;
        }
        if (Vector3.Distance(transform.position, targetEnemy.transform.position) >= seeableDistance+10 && !RWHasLineOfSightToPosition(targetEnemy.transform.position, 120, seeableDistance, 5))
        {
            Plugin.ExtendedLogging("Stop Target Giant");
            this.agent.angularSpeed = 40f;
            this.creatureAnimator.SetBool("chasing", false);
            this.creatureAnimator.SetBool("walking", true);
            StartSearchRoutine(this.transform.position, 50, this.agent.areaMask);
            agent.speed = walkingSpeed;
            SwitchToBehaviourServerRpc((int)State.Wandering);
            return;
        }
        SetDestinationToPosition(targetEnemy.transform.position, checkForPath: true);
    }

    public static IEnumerator DrawPath(LineRenderer line, NavMeshAgent agent)
    {
        if (!agent.enabled) yield break;
        yield return new WaitForEndOfFrame();
        line.SetPosition(0, agent.transform.position); //set the line's origin

        line.positionCount = agent.path.corners.Length; //set the array of positions to the amount of corners
        for (var i = 1; i < agent.path.corners.Length; i++)
        {
            line.SetPosition(i, agent.path.corners[i]); //go through each corner and set that to the line renderer's position
        }
    }

    public void DealEnemyDamageFromShockwave(EnemyAI enemy, float distanceFromEnemy)
    {
        // Apply damage based on distance
        if (distanceFromEnemy <= 3f)
        {
            enemy.HitEnemy(2, null, false, -1);
        }
        else if (distanceFromEnemy <= 10f)
        {
            enemy.HitEnemy(1, null, false, -1);
        }

        // Optional: Log the distance and remaining HP for debugging
        Plugin.ExtendedLogging($"Distance: {distanceFromEnemy} HP: {enemy.enemyHP}");
    }

    public PlayerControllerB GetClosestPlayerToRedwood()
    {
        return StartOfRound.Instance.allPlayerScripts
            .Where(player => player.IsSpawned && player.isPlayerControlled && !player.isPlayerDead)
            .OrderBy(x => Vector3.Distance(x.transform.position, transform.position) <= 11)
            .FirstOrDefault(x => RWHasLineOfSightToPosition(x.transform.position, 120, seeableDistance, 5));
    }

    public void ParticlesFromEatingForestKeeper(EnemyAI targetEnemy) {
        if (targetEnemy is ForestGiantAI)
        {
            ForestKeeperParticles.Play(); // Also make them be affected by the world for proper fog stuff?
        }
        else if (targetEnemy is DriftwoodMenaceAI)
        {
            DriftwoodGiantParticles.Play();
        }
        else if (targetEnemy is RadMechAI)
        {
            OldBirdParticles.Play();
        }
        Plugin.ExtendedLogging("Ate: " + targetEnemy.enemyType.enemyName);
        targetEnemy.KillEnemyOnOwnerClient(overrideDestroy: true);
    }

    public bool FindClosestAliveGiantInRange(float range)
    {
        EnemyAI? closestEnemy = null;
        float minDistance = float.MaxValue;

        foreach (EnemyAI enemy in RoundManager.Instance.SpawnedEnemies)
        {
            if ((enemy is ForestGiantAI || enemy is DriftwoodMenaceAI) && !enemy.isEnemyDead)
            {
                float distance = Vector3.Distance(this.transform.position, enemy.transform.position);
                if (distance < range && distance < minDistance && Vector3.Distance(enemy.transform.position, shipBoundaries.position) > distanceFromShip)
                {
                    minDistance = distance;
                    closestEnemy = enemy;
                }
            }
        }
        if (closestEnemy != null)
        {
            SetEnemyTargetServerRpc(RoundManager.Instance.SpawnedEnemies.IndexOf(closestEnemy));
            return true;
        }
        return false;
    }

    public IEnumerator EatTargetEnemy(EnemyAI targetEnemy)
    {
        foreach (AudioSource audioSource in targetEnemy.GetComponents<AudioSource>())
        {
            audioSource.mute = true;
        }
        foreach (AudioSource audioSource in targetEnemy.GetComponentsInChildren<AudioSource>())
        {
            audioSource.mute = true;
        }
        creatureVoice.PlayOneShot(eatenSound, 1);
        if (IsServer)
        {
            this.networkAnimator.SetTrigger("eatEnemyGiant");
            this.creatureAnimator.SetBool("chasing", false);
            this.creatureAnimator.SetBool("walking", false);
        }
        SwitchToBehaviourStateOnLocalClient((int)State.EatingTargetGiant);
        agent.speed = 0f;
        yield return new WaitForSeconds(eating.length + 5f);
        if (isEnemyDead) yield break;
        foreach (EnemyAI enemy in RoundManager.Instance.SpawnedEnemies)
        {
            if (enemy == null || enemy is not RadMechAI) continue;
            RadMechAI rad = (RadMechAI)enemy;
            rad.TryGetComponent(out IVisibleThreat threat);
            if (threat != null && rad.focusedThreatTransform == threat.GetThreatTransform())
            {
                Plugin.ExtendedLogging("Stuff is happening!!");
                rad.targetedThreatCollider = null;
                rad.CheckSightForThreat();
            }
        }
        this.agent.angularSpeed = 40f;
        agent.speed = walkingSpeed;
        if (IsServer)
        {
            this.creatureAnimator.SetBool("chasing", false);
            this.creatureAnimator.SetBool("walking", true);
        }
        StartSearchRoutine(this.transform.position, 50, this.agent.areaMask);
        SwitchToBehaviourStateOnLocalClient((int)State.Wandering);
    }

    public override void OnCollideWithPlayer(Collider other)
    {
        if (isEnemyDead) return;
        PlayerControllerB player = MeetsStandardPlayerCollisionConditions(other);
        if (player == null) return;
        player.KillPlayer(player.velocityLastFrame, true, CauseOfDeath.Crushing, 0, default);
        // play player death particles.
    }

    public override void OnCollideWithEnemy(Collider other, EnemyAI collidedEnemy)
    {
        if (isEnemyDead) return;
        if (collidedEnemy == targetEnemy && !eatingEnemy && currentBehaviourStateIndex == (int)State.RunningToTarget && this.agent.velocity.magnitude >= 1f)
        {
            eatPosition = this.transform.position;
            eatingEnemy = true;
            collidedEnemy.agent.enabled = false;
            foreach (Collider enemyCollider in collidedEnemy.GetComponents<Collider>())
            {
                enemyCollider.enabled = false;
            }
            foreach (Collider enemyCollider in collidedEnemy.GetComponentsInChildren<Collider>())
            {
                enemyCollider.enabled = false;
            }
            Plugin.ExtendedLogging("Eating Giant");
            StartCoroutine(EatTargetEnemy(collidedEnemy));
        }
    }

    public bool RWHasLineOfSightToPosition(Vector3 pos, float width, float range, float proximityAwareness)
    {
        if (Vector3.Distance(eye.position, pos) < range && !Physics.Linecast(eye.position, pos, StartOfRound.Instance.collidersAndRoomMaskAndDefault))
        {
            Vector3 to = pos - eye.position;
            if (Vector3.Angle(eye.forward, to) < width || Vector3.Distance(transform.position, pos) < proximityAwareness)
            {
                return true;
            }
        }
        return false;
    }

    public override void HitEnemy(int force = 1, PlayerControllerB? playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
    {
        base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
        if (isEnemyDead) return;
        if (force >= 6)
        {
            enemyHP -= force;
            // Set on fire and stuff
            if (TargetClosestRadMech(seeableDistance) && currentBehaviourStateIndex == (int)State.Wandering && Plugin.ModConfig.ConfigRedwoodCanEatOldBirds.Value)
            {
                if (IsServer)
                {
                    StopSearchRoutine();
                    this.creatureAnimator.SetBool("walking", false);
                    this.creatureAnimator.SetBool("chasing", false);
                    StartCoroutine(SetSpeedForChasingGiant());
                }
                Plugin.ExtendedLogging("Start Target Giant");
                SwitchToBehaviourStateOnLocalClient((int)State.RunningToTarget);
            }
        }
        else
        {
            enemyHP -= force;
        }

        if (enemyHP <= 0 && !isEnemyDead) {
            if (currentBehaviourStateIndex == (int)State.EatingTargetGiant)
            {
                enemyHP++;
            }
            else
            {
                if (IsOwner)
                {
                    KillEnemyOnOwnerClient();
                }
            }
        }
        Plugin.ExtendedLogging(enemyHP.ToString());
    }

    public override void KillEnemy(bool destroy = false) // todo: if target enemy is dead, stop chasing, set target to null
    { 
        base.KillEnemy(destroy);
        CollisionFootL.enabled = false;
        CollisionFootR.enabled = false;
        if (IsServer) networkAnimator.SetTrigger("startDeath");
        if (targetEnemy != null && targetEnemy.agent != null && !targetEnemy.agent.enabled)
        {
            targetEnemy.agent.enabled = true;
        }
        //SpawnHeartOnDeath(transform.position);
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        instanceNumbers--;
    }
    public bool TargetClosestRadMech(float range)
    {
        EnemyAI? closestEnemy = null;
        float minDistance = float.MaxValue;

        foreach (EnemyAI enemy in RoundManager.Instance.SpawnedEnemies)
        {
            if (enemy is RadMechAI && !enemy.isEnemyDead)
            {
                float distance = Vector3.Distance(transform.position, enemy.transform.position);
                if (distance < range && distance < minDistance)
                {
                    minDistance = distance;
                    closestEnemy = enemy;
                }
            }
        }
        if (closestEnemy != null)
        {
            targetEnemy = closestEnemy;
            return true;
        }
        return false;
    }

    [ServerRpc(RequireOwnership = false)]
    public void OnLandCrunchySoundServerRpc()
    {
        OnLandCrunchySoundClientRpc();
    }

    [ClientRpc]
    public void OnLandCrunchySoundClientRpc()
    {
        creatureSFX.PlayOneShot(crunchySquishSound);
        creatureSFXFar.PlayOneShot(crunchySquishSound);
    }

    public void FinishKicking()
    { // AnimEvent
        kicking = false;
        kickingOut = false;
        Plugin.ExtendedLogging("Kick ended");
        if (IsServer)
        {
            this.creatureAnimator.SetBool("chasing", false);
            this.creatureAnimator.SetBool("walking", true);
        }
        playerToKick = null;
        agent.speed = walkingSpeed;
    }

    public void KickingOutStart()
    { // AnimEvent
        kickingOut = true;
    }

    public void WanderAroundAfterSpawnAnimation() 
    { // AnimEvent
        if (IsServer)
        {
            this.creatureAnimator.SetBool("chasing", false);
            this.creatureAnimator.SetBool("walking", true);
        }
        Plugin.ExtendedLogging("Start Walking Around");
        StartSearchRoutine(this.transform.position, 50, this.agent.areaMask);
        agent.speed = walkingSpeed;
        SwitchToBehaviourStateOnLocalClient((int)State.Wandering);
    }

    public void OnLandFromJump()
    { // AnimEvent
        var localPlayer = GameNetworkManager.Instance.localPlayerController;
        if (Vector3.Distance(CollisionFootL.transform.position, localPlayer.transform.position) <= 10f || Vector3.Distance(CollisionFootR.transform.position, localPlayer.transform.position) <= 10f)
        {
            localPlayer.DamagePlayer(200, false, true, CauseOfDeath.Crushing, 0, false, localPlayer.velocityLastFrame);
            OnLandCrunchySoundServerRpc();
        }
        jumping = false;
        BigSmokeEffect.Play();
        Plugin.ExtendedLogging("End Jump");
        if (IsServer)
        {
            this.creatureAnimator.SetBool("chasing", false);
            this.creatureAnimator.SetBool("walking", true);
        }
        agent.speed = walkingSpeed;
    }

    public void EnableDeathColliders()
    { // AnimEvent
        foreach (Collider deathCollider in DeathColliders)
        {
            deathCollider.gameObject.GetComponent<BetterCooldownTrigger>().enabledScript = true;
        }
    }

    public void DisableDeathColliders()
    { // AnimEvent
        DeathParticles.Play();
        foreach (Collider deathCollider in DeathColliders)
        {
            deathCollider.gameObject.GetComponent<BetterCooldownTrigger>().enabledScript = false;
        }
    }

    public void EatingTargetGiant()
    { // AnimEvent
        if (targetEnemy == null || isEnemyDead) return;
        ParticlesFromEatingForestKeeper(targetEnemy);
        eatingEnemy = false;
        sizeUp = false;
        targetEnemy = null;
    }

    public void ShakePlayerCamera()
    { // AnimEvent
            float distance = Vector3.Distance(transform.position, GameNetworkManager.Instance.localPlayerController.transform.position);
            switch (distance)
            {
                case < 15f:
                
                    HUDManager.Instance.ShakeCamera(ScreenShakeType.Long);
                    HUDManager.Instance.ShakeCamera(ScreenShakeType.VeryStrong);
                    HUDManager.Instance.ShakeCamera(ScreenShakeType.VeryStrong);

                    HUDManager.Instance.ShakeCamera(ScreenShakeType.Big);
                    HUDManager.Instance.ShakeCamera(ScreenShakeType.Small);
                    break;
                case < 30 and >= 15:
                    HUDManager.Instance.ShakeCamera(ScreenShakeType.Big);
                    HUDManager.Instance.ShakeCamera(ScreenShakeType.Big);
                    HUDManager.Instance.ShakeCamera(ScreenShakeType.Small);
                    break;
                case < 50f and >= 30:
                    HUDManager.Instance.ShakeCamera(ScreenShakeType.Small);
                    HUDManager.Instance.ShakeCamera(ScreenShakeType.Small);
                    break;
            }
    }

    public void FootStepSounds()
    { // AnimEvent
        creatureSFX.PlayOneShot(stompSounds[UnityEngine.Random.Range(0, stompSounds.Length)]);
        creatureSFXFar.PlayOneShot(stompSounds[UnityEngine.Random.Range(0, farStompSounds.Length)]);
    }

    public void LeftFootStepInteractions()
    { // AnimEvent
        DustParticlesLeft.Play(); // Play the particle system with the updated color
        creatureSFX.PlayOneShot(stompSounds[UnityEngine.Random.Range(0, stompSounds.Length)]);
        creatureSFXFar.PlayOneShot(stompSounds[UnityEngine.Random.Range(0, farStompSounds.Length)]);
        PlayerControllerB player = GameNetworkManager.Instance.localPlayerController;
        if (player.IsSpawned && player.isPlayerControlled && !player.isPlayerDead && !player.isInHangarShipRoom)
        {
            float distance = Vector3.Distance(CollisionFootL.transform.position, player.transform.position);
            if (distance <= 10f)
            {
                player.DamagePlayer(10, causeOfDeath: CauseOfDeath.Crushing);
            }
        }
        var enemiesList = RoundManager.Instance.SpawnedEnemies; //todo: change this to a spherecast
        foreach (var enemy in enemiesList)
        {
            if (enemy == null || enemy.isEnemyDead || enemy is RedwoodTitanAI) continue;
            var LeftFootDistance = Vector3.Distance(CollisionFootL.transform.position, enemy.transform.position);
            if (LeftFootDistance <= 7.5f)
            {
                DealEnemyDamageFromShockwave(enemy, LeftFootDistance);
            }
        }
    }

    public void RightFootStepInteractions()
    { // AnimEvent
        DustParticlesRight.Play(); // Play the particle system with the updated color
        creatureSFX.PlayOneShot(stompSounds[UnityEngine.Random.Range(0, stompSounds.Length)]);
        creatureSFXFar.PlayOneShot(stompSounds[UnityEngine.Random.Range(0, farStompSounds.Length)]);
        PlayerControllerB player = GameNetworkManager.Instance.localPlayerController;
        if (player.IsSpawned && player.isPlayerControlled && !player.isPlayerDead && !player.isInHangarShipRoom)
        {
            float distance = Vector3.Distance(CollisionFootR.transform.position, player.transform.position);
            if (distance <= 10f)
            {
                player.DamagePlayer(10, causeOfDeath: CauseOfDeath.Crushing);
            }
        }
        var enemiesList = RoundManager.Instance.SpawnedEnemies; //todo: change this to a spherecast
        foreach (var enemy in enemiesList)
        {
            if (enemy == null || enemy.isEnemyDead || enemy is RedwoodTitanAI) continue;
            var RightFootDistance = Vector3.Distance(CollisionFootR.transform.position, enemy.transform.position);
            if (RightFootDistance <= 7.5f)
            {
                DealEnemyDamageFromShockwave(enemy, RightFootDistance);
            }
        }
    }

    public void SpawnHeartOnDeath(Vector3 position)
    {
        if (Plugin.ModConfig.ConfigRedwoodHeartEnabled.Value && IsHost && !Plugin.LGUIsOn)
        {
            CodeRebirthUtils.Instance.SpawnScrapServerRpc("RedwoodHeart", position);
        }
    }
}