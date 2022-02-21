﻿/*
 * Copyright 2022 Peter Han
 * Permission is hereby granted, free of charge, to any person obtaining a copy of this software
 * and associated documentation files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use, copy, modify, merge, publish,
 * distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all copies or
 * substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING
 * BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
 * DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */

using HarmonyLib;
using System;
using System.Runtime.CompilerServices;

namespace PeterHan.FastTrack.SensorPatches {
	/// <summary>
	/// Applied to BalloonArtistChore.StatesInstance to force update the target cell
	/// while the chore is actually active.
	/// </summary>
	[HarmonyPatch(typeof(BalloonArtistChore.StatesInstance), nameof(BalloonArtistChore.
		StatesInstance.GetBalloonStallCell))]
	public static class BalloonArtistChore_StatesInstance_GetBalloonStallCell_Patch {
		internal static bool Prepare() => FastTrackOptions.Instance.SensorOpts;

		/// <summary>
		/// Applied before GetBalloonStallCell runs.
		/// </summary>
		internal static void Prefix(BalloonStandCellSensor ___balloonArtistCellSensor) {
			if (___balloonArtistCellSensor != null)
				BalloonStandCellSensorUpdater.Update(___balloonArtistCellSensor);
		}
	}

	/// <summary>
	/// Applied to BalloonStandCellSensor.Update to extract the original method body.
	/// </summary>
	[HarmonyPatch(typeof(BalloonStandCellSensor), nameof(BalloonStandCellSensor.Update))]
	internal static class BalloonStandCellSensorUpdater {
		internal static bool Prepare() => FastTrackOptions.Instance.SensorOpts;

		/// <summary>
		/// Applied before Update runs.
		/// </summary>
		internal static bool Prefix() {
			return false;
		}

		[HarmonyReversePatch(HarmonyReversePatchType.Original)]
		[HarmonyPatch(nameof(BalloonStandCellSensor.Update))]
		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static void Update(BalloonStandCellSensor _) {
			// Dummy code to ensure no inlining
			while (System.DateTime.Now.Ticks > 0L)
				throw new NotImplementedException("Reverse patch stub");
		}
	}
}
