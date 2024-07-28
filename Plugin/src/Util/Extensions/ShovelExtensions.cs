using System;

namespace CodeRebirth.Util.Extensions;

public static class ShovelExtensions {
	public static int CriticalHit(int force, System.Random random, float critChance) {
		if (random.NextDouble(0, 100) < Math.Clamp(critChance, 0, 100)) {
            Plugin.Logger.LogInfo("Critical Hit!");
            return force * 2;
        }
        return force;
	}
}