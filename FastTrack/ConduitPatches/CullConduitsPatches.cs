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
using PeterHan.PLib.Core;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using TranspiledMethod = System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction>;

namespace PeterHan.FastTrack.ConduitPatches {
	/// <summary>
	/// Applied to ConduitFlowVisualizer.RenderMeshContext.ctor to avoid updating liquid and
	/// gas conduits inside tiles when not in a conduit overlay. Makes a small difference on
	/// most bases, but zoomed out pipe spaghetti can make a huge difference!
	/// </summary>
	[HarmonyPatch]
	public static class ConduitFlowVisualizer_RenderMeshContext_Constructor_Patch {
		internal static bool Prepare() => FastTrackOptions.Instance.CullConduits;

		/// <summary>
		/// Targets the ConduitFlowVisualizer.RenderMeshContext constructor.
		/// </summary>
		internal static MethodBase TargetMethod() {
			var meshContext = typeof(ConduitFlowVisualizer).GetNestedType("RenderMeshContext",
				PPatchTools.BASE_FLAGS | BindingFlags.Instance);
			return meshContext?.GetConstructor(PPatchTools.BASE_FLAGS | BindingFlags.Instance,
				null, new Type[] { typeof(ConduitFlowVisualizer), typeof(float),
				typeof(Vector2I), typeof(Vector2I) }, null);
		}

		/// <summary>
		/// Removes all conduits that are inside solid, opaque grid cells.
		/// </summary>
		private static bool FilterConduitsInTiles(bool bounds, int cell, bool showContents) {
			if (!showContents && bounds)
				bounds = cell.IsVisibleCell();
			return bounds;
		}

		/// <summary>
		/// Transpiles the constructor to insert a call to the conduit filter in the list of
		/// conditions for deciding which conduits to render.
		/// </summary>
		internal static TranspiledMethod Transpiler(TranspiledMethod instructions,
				ILGenerator generator) {
			var cellToXY = typeof(Grid).GetMethodSafe(nameof(Grid.CellToXY), true,
				typeof(int));
			var showContentsField = typeof(ConduitFlowVisualizer).GetFieldSafe("showContents",
				false);
			var filter = typeof(ConduitFlowVisualizer_RenderMeshContext_Constructor_Patch).
				GetMethodSafe(nameof(FilterConduitsInTiles), true, typeof(bool), typeof(int),
				typeof(bool));
			// This name is .NET system defined: https://docs.microsoft.com/en-us/dotnet/api/system.decimal.op_lessthanorequal?view=netframework-4.7.1
			var targetMethod = typeof(Vector2I).GetMethodSafe("op_LessThanOrEqual", true,
				typeof(Vector2I), typeof(Vector2I));
			if (filter == null)
				throw new InvalidOperationException("Cannot find replacement method!");
			if (targetMethod == null || cellToXY == null || showContentsField == null) {
				PUtil.LogWarning("Target not found for ConduitFlowVisualizer.RenderMeshContext");
				foreach (var instr in instructions)
					yield return instr;
			} else {
				var showContentsLocal = generator.DeclareLocal(typeof(bool));
				var cellLocal = generator.DeclareLocal(typeof(int));
				// Load outer.showContents and save
				yield return new CodeInstruction(OpCodes.Ldarg_1);
				yield return new CodeInstruction(OpCodes.Ldfld, showContentsField);
				yield return new CodeInstruction(OpCodes.Stloc_S, (byte)showContentsLocal.
					LocalIndex);
				bool patched = false;
				foreach (var instr in instructions) {
					if (instr.Is(OpCodes.Call, cellToXY)) {
						// Copy argument to local
						yield return new CodeInstruction(OpCodes.Dup);
						yield return new CodeInstruction(OpCodes.Stloc_S, (byte)cellLocal.
							LocalIndex);
					}
					yield return instr;
					if (!patched && instr.Is(OpCodes.Call, targetMethod)) {
						// Load the cell
						yield return new CodeInstruction(OpCodes.Ldloc_S, (byte)cellLocal.
							LocalIndex);
						// Load showContents
						yield return new CodeInstruction(OpCodes.Ldloc_S, (byte)
							showContentsLocal.LocalIndex);
						// Call the filter
						yield return new CodeInstruction(OpCodes.Call, filter);
#if DEBUG
						PUtil.LogDebug("Patched ConduitFlowVisualizer.RenderMeshContext");
#endif
						patched = true;
					}
				}
				if (!patched)
					PUtil.LogWarning("Unable to patch ConduitFlowVisualizer.RenderMeshContext");
			}
		}
	}

	/// <summary>
	/// Applied to KAnimGraphTileVisualizer to hide anything visualized with this component
	/// when inside a solid tile. Currently all conduits and travel tubes use this.
	/// </summary>
	[HarmonyPatch(typeof(KAnimGraphTileVisualizer), nameof(KAnimGraphTileVisualizer.Refresh))]
	public static class KAnimGraphTileVisualizer_Refresh_Patch {
		internal static bool Prepare() => FastTrackOptions.Instance.CullConduits;

		/// <summary>
		/// Applied before Refresh runs.
		/// </summary>
		internal static void Prefix(KAnimGraphTileVisualizer __instance) {
			if (__instance != null)
				UpdateGraphIfEntombed.CheckVisible(__instance.
					GetComponent<UpdateGraphIfEntombed>());
		}
	}

	/// <summary>
	/// Applied to SolidConduitFlowVisualizer to hide carts and items that are inside solid
	/// tiles.
	/// </summary>
	[HarmonyPatch(typeof(SolidConduitFlowVisualizer), nameof(SolidConduitFlowVisualizer.
		Render))]
	public static class SolidConduitFlowVisualizer_Render_Patch {
		internal static bool Prepare() => FastTrackOptions.Instance.CullConduits;

		/// <summary>
		/// Removes all conduits that are inside solid, opaque grid cells.
		/// </summary>
		private static Vector2I FilterConduit(int cell, bool showContents) {
			Vector2I xy;
			if (showContents || cell.IsVisibleCell())
				xy = Grid.CellToXY(cell);
			else
				xy = new Vector2I(int.MinValue, int.MinValue);
			return xy;
		}

		/// <summary>
		/// Transpiles Render to not render the cart or item inside solid tiles.
		/// </summary>
		internal static TranspiledMethod Transpiler(TranspiledMethod instructions,
				ILGenerator generator) {
			// Only replace the first instance after this call
			var marker = typeof(SolidConduitFlow.SOAInfo).GetMethodSafe(nameof(
				SolidConduitFlow.SOAInfo.GetCell), false, PPatchTools.AnyArguments);
			var target = typeof(Grid).GetMethodSafe(nameof(Grid.CellToXY), true, typeof(int));
			var replacement = typeof(SolidConduitFlowVisualizer_Render_Patch).GetMethodSafe(
				nameof(FilterConduit), true, typeof(int), typeof(bool));
			var showContentsField = typeof(SolidConduitFlowVisualizer).GetFieldSafe(
				"showContents", false);
			int state = 0;
			if (marker != null && replacement != null && showContentsField != null &&
					target != null) {
				// Create the local for showContents
				var showContents = generator.DeclareLocal(typeof(bool));
				yield return new CodeInstruction(OpCodes.Ldarg_0);
				yield return new CodeInstruction(OpCodes.Ldfld, showContentsField);
				yield return new CodeInstruction(OpCodes.Stloc_S, (byte)showContents.
					LocalIndex);
				foreach (var instr in instructions) {
					if (state == 0 && instr.Is(OpCodes.Callvirt, marker))
						state = 1;
					else if (state == 1 && instr.Is(OpCodes.Call, target)) {
						state = 2;
						yield return new CodeInstruction(OpCodes.Ldloc_S, (byte)showContents.
							LocalIndex);
						instr.operand = replacement;
#if DEBUG
						PUtil.LogDebug("Patched SolidConduitFlowVisualizer.Render");
#endif
					}
					yield return instr;
				}
			} else
				foreach (var instr in instructions)
					yield return instr;
			if (state != 2)
				PUtil.LogWarning("Unable to patch SolidConduitFlowVisualizer.Render");
		}
	}

	/// <summary>
	/// A component to add to wires and conduits that hides them if they are inside a solid
	/// tile.
	/// </summary>
	[SkipSaveFileSerialization]
	public sealed class UpdateGraphIfEntombed : KMonoBehaviour {
		/// <summary>
		/// Checks if a building is in a visible tile.
		/// </summary>
		/// <param name="component">The component to check.</param>
		internal static void CheckVisible(UpdateGraphIfEntombed component) {
			if (component != null) {
				var kbac = component.controller;
				bool show = Grid.PosToCell(component.transform.position).IsVisibleCell() ||
					component.overlayVisible;
				if (kbac.enabled != show)
					kbac.enabled = show;
			}
		}

		/// <summary>
		/// Fired when a solid changes.
		/// </summary>
		/// <param name="item">The graph that was triggered.</param>
		private static void OnSolidChanged(object item) {
			if (item is UpdateGraphIfEntombed component)
				CheckVisible(component);
		}

		/// <summary>
		/// The solid change entry to trigger when the conduit is put inside a solid tile.
		/// </summary>
		private HandleVector<int>.Handle partitionerEntry;

#pragma warning disable IDE0044
#pragma warning disable CS0649
		// These fields are automatically populated by KMonoBehaviour
		[MyCmpReq]
		private KBatchedAnimController controller;
#pragma warning restore CS0649
#pragma warning restore IDE0044

		/// <summary>
		/// Whether the current overlay forces the conduit to be visible.
		/// </summary>
		private bool overlayVisible;

		/// <summary>
		/// The layer to use for checking overlay visibility.
		/// </summary>
		internal ObjectLayer layer;

		internal UpdateGraphIfEntombed() {
			layer = ObjectLayer.Minion;
			overlayVisible = false;
			partitionerEntry = HandleVector<int>.InvalidHandle;
		}

		protected override void OnCleanUp() {
			if (partitionerEntry.IsValid())
				GameScenePartitioner.Instance.Free(ref partitionerEntry);
			base.OnCleanUp();
		}

		protected override void OnSpawn() {
			var gsp = GameScenePartitioner.Instance;
			base.OnSpawn();
			if (gsp != null)
				partitionerEntry = gsp.Add(nameof(UpdateGraphIfEntombed), controller, Grid.
					PosToCell(transform.position), gsp.solidChangedLayer, OnSolidChanged);
		}

		/// <summary>
		/// Updates the visibility based on the current overlay.
		/// </summary>
		/// <param name="visible">true if the building is on-screen and visible in the current
		/// overlay, or false otherwise.</param>
		internal void UpdateOverlay(bool visible) {
			overlayVisible = visible;
			CheckVisible(this);
		}
	}

	/// <summary>
	/// Applied to KAnimGraphTileVisualizer to add a listener for solid changes on spawn.
	/// </summary>
	[HarmonyPatch(typeof(KAnimGraphTileVisualizer), "OnSpawn")]
	public static class KAnimGraphTileVisualizer_OnSpawn_Patch {
		internal static bool Prepare() => FastTrackOptions.Instance.CullConduits;

		/// <summary>
		/// Applied after OnSpawn runs.
		/// </summary>
		internal static void Postfix(KAnimGraphTileVisualizer __instance) {
			var building = __instance.GetComponent<BuildingComplete>();
			if (building != null)
				// Only apply to completed buildings, buildings under construction should be
				// visible even inside any wall
				__instance.gameObject.AddOrGet<UpdateGraphIfEntombed>().layer = building.Def.
					TileLayer;
		}
	}

	/// <summary>
	/// Applied to OverlayModes.Mode to set overlay visibility to true on targets that match
	/// the current overlay.
	/// </summary>
	[HarmonyPatch]
	public static class OverlayModes_Modes_Patch {
		internal static bool Prepare() => FastTrackOptions.Instance.CullConduits;

		/// <summary>
		/// Target the update method of each conduit overlay.
		/// </summary>
		internal static IEnumerable<MethodBase> TargetMethods() {
			const string name = nameof(OverlayModes.Mode.Update);
			yield return typeof(OverlayModes.ConduitMode).GetMethodSafe(name, false);
			yield return typeof(OverlayModes.Logic).GetMethodSafe(name, false);
			yield return typeof(OverlayModes.Power).GetMethodSafe(name, false);
			yield return typeof(OverlayModes.SolidConveyor).GetMethodSafe(name, false);
		}

		/// <summary>
		/// A replacement for the particular AddTargetIfVisible version using SaveLoadRoot.
		/// </summary>
		/// <param name="instance">The object under test.</param>
		/// <param name="visMin">The minimum visible cell coordinate.</param>
		/// <param name="visMax">The maximum visible cell coordinate.</param>
		/// <param name="targets">The location to add the target if it matches.</param>
		/// <param name="layer">The layer to move the target if it matches.</param>
		/// <param name="onAdded">The callback to invoke if it matches.</param>
		/// <param name="shouldAdd">The callback to invoke and check if it matches.</param>
		private static void AddTargetIfVisible(OverlayModes.Mode _, SaveLoadRoot instance,
				Vector2I visMin, Vector2I visMax, ICollection<SaveLoadRoot> targets, int layer,
				Action<SaveLoadRoot> onAdded, Func<KMonoBehaviour, bool> shouldAdd) {
			if (instance != null) {
				Vector2 min = instance.PosMin(), max = instance.PosMax();
				int xMin = (int)min.x, yMin = (int)min.y, yMax = (int)max.y, xMax = (int)max.x;
				if (xMax >= visMin.x && yMax >= visMin.y && xMin <= visMax.x && yMin <= visMax.
						y && !targets.Contains(instance)) {
					bool found = !PropertyTextures.IsFogOfWarEnabled;
					int curWorld = ClusterManager.Instance.activeWorldId, w = Grid.
						WidthInCells, cell = w * yMin + xMin;
					int width = xMax - xMin + 1, height = yMax - yMin + 1;
					// Iterate the extents of the object to see if any cell is visible
					w -= width;
					for (int y = height; y > 0 && !found; y--) {
						for (int x = width; x > 0 && !found; x--) {
							// 20 is hardcoded in original method
							if (Grid.IsValidCell(cell) && Grid.Visible[cell] > 20 &&
									Grid.WorldIdx[cell] == curWorld)
								found = true;
							cell++;
						}
						cell += w;
					}
					if (found) {
						var kmb = instance as KMonoBehaviour;
						if (shouldAdd == null || kmb == null || shouldAdd(kmb)) {
							// Add to list and trigger overlay mode update
							if (kmb != null) {
								var kbac = kmb.GetComponent<KBatchedAnimController>();
								if (kbac != null)
									kbac.SetLayer(layer);
								var updateGraph = kmb.GetComponent<UpdateGraphIfEntombed>();
								if (updateGraph != null)
									updateGraph.UpdateOverlay(true);
							}
							targets.Add(instance);
							onAdded?.Invoke(instance);
						}
					}
				}
			}
		}

		/// <summary>
		/// Transpiles Update to replace calls to a specific AddTargetIfVisible version with
		/// a version that is not only a bit faster, but also sets the conduit visible when it
		/// matches the criteria.
		/// </summary>
		internal static TranspiledMethod Transpiler(TranspiledMethod instructions) {
			return PPatchTools.ReplaceMethodCall(instructions, typeof(OverlayModes.Mode).
				GetMethod("AddTargetIfVisible", PPatchTools.BASE_FLAGS | BindingFlags.
				Instance)?.MakeGenericMethod(typeof(SaveLoadRoot)),
				typeof(OverlayModes_Modes_Patch).GetMethodSafe(nameof(AddTargetIfVisible),
				true, PPatchTools.AnyArguments));
		}
	}

	/// <summary>
	/// Applied to OverlayModes.Mode to restore the overlay visibility when a conduit leaves
	/// visibility in the overlay.
	/// </summary>
	[HarmonyPatch(typeof(OverlayModes.Mode), "ResetDisplayValues",
		typeof(KBatchedAnimController))]
	public static class OverlayModes_Mode_ResetDisplayValues_Patch {
		internal static bool Prepare() => FastTrackOptions.Instance.CullConduits;

		/// <summary>
		/// Applied after ResetDisplayValues runs.
		/// </summary>
		internal static void Postfix(KBatchedAnimController controller) {
			var component = controller.GetComponent<UpdateGraphIfEntombed>();
			if (component != null)
				component.UpdateOverlay(false);
			// SolidConduitFlow.ApplyOverlayVisualization can be used to show the pickupables
			// if they ever get culled behind tiles
		}
	}
}
