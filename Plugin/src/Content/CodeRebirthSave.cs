﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CodeRebirth.src;
using CodeRebirth.src.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;

namespace CodeRebirth.Content;

// PER HOST SAVE, VALUES ARE SYNCED FROM HOST, ONLY EDITABLE ON HOST.
class CodeRebirthSave(string fileName) : SaveableData(fileName) {
	public static CodeRebirthSave Current;

	public int MoonPriceUpgrade { get; set; }

	public Dictionary<ulong, CodeRebirthLocalSave> PlayerData { get; private set; } = [];
	
	public override void Save() {
		EnsureHost();
		base.Save();
	}

	void EnsureHost() {
		if (!CodeRebirthUtils.Instance.IsHost && !CodeRebirthUtils.Instance.IsServer) throw new InvalidOperationException("Only the host should save CodeRebirthSave.");
	}
}

// PER PLAYER
class CodeRebirthLocalSave {
	public int MovementSpeedUpgrades { get; set; }
	public int StaminaUpgrades { get; set; }
	public int CarrySlotUpgrades { get; set; }
}